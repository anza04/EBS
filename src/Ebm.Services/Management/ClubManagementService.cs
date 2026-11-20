using Ebm.Domain.Chemistry;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Development;
using Ebm.Domain.Management;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;
using Ebm.Domain.Seasons;
using Ebm.Domain.Staff;
using Ebm.Services.Clubs;
using Ebm.Services.Contracts;
using Ebm.Services.Data;
using Ebm.Services.Tactics;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Management;

/// <summary>Implements <see cref="IClubManagementService"/> with the pure planners of <c>Ebm.Domain.Management</c>.</summary>
internal sealed class ClubManagementService(
    IServiceScopeFactory scopes,
    IClubService clubs,
    ITacticsService tactics,
    IContractService contracts,
    PlayerRatingCalculator calculator,
    ContractNegotiator negotiator,
    GameClock clock) : IClubManagementService
{
    /// <summary>An injury expected to last longer than this many days is covered by a signing.</summary>
    public const int LongTermInjuryDays = 28;

    /// <summary>Free agents whose demands are read together when an AI club fills its squad.</summary>
    private const int NegotiationChunk = 25;

    public async Task<int> PrepareMatchdayAsync(IReadOnlyCollection<ClubId> clubIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clubIds);
        var changes = new Dictionary<ClubId, IReadOnlyList<RosterAssignment>>();
        foreach (var roster in await clubs.GetRostersAsync(clubIds, cancellationToken).ConfigureAwait(false))
        {
            var plan = LineupPlanner.Plan([.. roster.Players.Select(p => new LineupCandidate(p.Player.Id, p.Role, p.Availability, p.Evaluation, SquadBalance(p.Player)))]);
            var current = roster.Players.ToDictionary(p => p.Player.Id);
            var rotation = roster.Rotation.Select(p => p.Player.Id).ToList();
            var same = plan.All(p => current[p.PlayerId].Role == p.Role && current[p.PlayerId].LineupPosition == p.LineupPosition)
                && plan.Where(p => p.Role == RosterRole.Rotation).Select(p => p.PlayerId).SequenceEqual(rotation);
            if (!same)
            {
                changes[roster.Club.Id] = [.. plan.Select(p => new RosterAssignment(p.PlayerId, p.Role, p.LineupPosition))];
            }
        }

        await clubs.SaveRostersAsync(changes, cancellationToken).ConfigureAwait(false);
        return changes.Count;
    }

    public async Task<int> PlanTacticsAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default)
    {
        var planned = 0;
        Dictionary<ClubId, StaffMember> coaches;
        using (var scope = scopes.CreateScope())
        {
            coaches = (await StaffHiring.StaffAsync(scope.ServiceProvider, clock.Today, null, [StaffRole.HeadCoach], cancellationToken).ConfigureAwait(false))
                .GroupBy(x => x.Contract.ClubId).ToDictionary(g => g.Key, g => g.MaxBy(x => x.Member.Ability).Member);
        }

        foreach (var roster in await clubs.GetRostersAsync(await AiClubsAsync(managedClubs, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false))
        {
            var clubId = roster.Club.Id;
            var plan = TacticPlanner.Plan([.. roster.FirstTeam.Select(p => (p.Player, p.Evaluation))], coaches.GetValueOrDefault(clubId));
            var existing = (await tactics.GetClubTacticsAsync(clubId, cancellationToken).ConfigureAwait(false))?.Tactics.FirstOrDefault(t => t.Name == IClubManagementService.AiTacticName);
            var tactic = await tactics.SaveAsync(
                clubId,
                new TacticDraft(existing?.Id, IClubManagementService.AiTacticName, plan.OffensiveSystem, plan.Offense, plan.DefensiveSystem, plan.Defense, plan.Assignments, plan.Situations),
                cancellationToken).ConfigureAwait(false);
            if (!tactic.IsSelected)
            {
                await tactics.SelectAsync(tactic.Id, cancellationToken).ConfigureAwait(false);
            }

            planned++;
        }

        return planned;
    }

    public async Task<int> PlanTrainingAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default)
    {
        var ai = (await AiClubsAsync(managedClubs, cancellationToken).ConfigureAwait(false)).ToHashSet();
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var today = clock.Today;
        var members = await ClubMembership.SavedClubsAsync(s, today, cancellationToken).ConfigureAwait(false);
        var squads = (await Repo<Player, PlayerId>(s).ListUntrackedAsync(p => p.RetiredOn == null, cancellationToken).ConfigureAwait(false))
            .Where(p => members.TryGetValue(p.Id, out var club) && ai.Contains(club))
            .GroupBy(p => members[p.Id])
            .ToDictionary(g => g.Key, g => g.Select(p => (p, calculator.Evaluate(p))).ToList());
        var repo = Repo<TrainingPlan, TrainingPlanId>(s);
        var plans = (await repo.ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.ClubId);
        foreach (var clubId in ai)
        {
            var programme = TrainingPlanner.Plan(squads.GetValueOrDefault(clubId) ?? [], today);
            if (!plans.TryGetValue(clubId, out var plan))
            {
                plan = new TrainingPlan(TrainingPlanId.New(), clubId);
                repo.Add(plan);
            }

            plan.Update(programme.TeamFocus, programme.Intensity, programme.Individuals);
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ai.Count;
    }

    public async Task<int> RenewContractsAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var renewed = 0;
        foreach (var clubId in await AiClubsAsync(managedClubs, cancellationToken).ConfigureAwait(false))
        {
            if (await clubs.GetRosterAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } roster
                || await contracts.GetClubContractsAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } book)
            {
                continue;
            }

            var ranks = roster.FirstTeam.OrderByDescending(p => p.Evaluation.Overall).ThenBy(p => p.Player.Id.Value)
                .Select((p, i) => (p, Rank: i + 1))
                .ToDictionary(x => x.p.Player.Id);
            var wageBill = book.Commitments.WageBill;
            var renewedPlayers = book.Current.Where(l => l.IsUpcoming).Select(l => l.Contract.PlayerId).ToHashSet();
            foreach (var line in book.Current.Where(l => l.IsRunning && l.Contract.Status == ContractStatus.Active && l.Contract.PlayerId is not null))
            {
                var contract = line.Contract;
                if (!ranks.TryGetValue(contract.PlayerId!.Value, out var ranked) || renewedPlayers.Contains(contract.PlayerId)
                    || !RenewalPolicy.Wants(ranked.Rank, ranked.p.Age, contract.EndDate, today))
                {
                    continue;
                }

                var start = contract.EndDate.AddDays(1);
                var offer = new ContractTerms(start, start.AddYears(RenewalPolicy.Years(ranked.p.Age)).AddDays(-1), contract.AnnualSalary);
                var answer = await contracts.NegotiateRenewalAsync(contract.Id, offer, cancellationToken).ConfigureAwait(false);
                var terms = answer.IsAccepted ? offer
                    : answer is { Outcome: NegotiationOutcome.CounterOffer, CounterOffer: { } counter } ? counter
                    : null;
                // A counter-offer may ask for promises, a release clause or a longer deal: the club accepts at most one year more.
                if (terms is null
                    || terms.Years > RenewalPolicy.Years(ranked.p.Age) + 1.01
                    || terms.AnnualSalary > contract.AnnualSalary * RenewalPolicy.MaxRaise
                    || wageBill + terms.AnnualSalary - contract.AnnualSalary > roster.Club.WageBudget)
                {
                    continue;
                }

                await contracts.RenewAsync(contract.Id, terms, cancellationToken).ConfigureAwait(false);
                wageBill += terms.AnnualSalary - contract.AnnualSalary;
                renewed++;
            }
        }

        return renewed;
    }

    public async Task<int> ReleaseSurplusAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var released = 0;
        List<Contract> running;
        using (var scope = scopes.CreateScope())
        {
            running = [.. (await Repo<Contract, ContractId>(scope.ServiceProvider).ListAsync(Contract.RunningOn(today), cancellationToken).ConfigureAwait(false))
                .Where(c => c.PlayerId is not null)];
        }

        foreach (var roster in await clubs.GetRostersAsync(await AiClubsAsync(managedClubs, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false))
        {
            var clubId = roster.Club.Id;
            var firstTeam = roster.FirstTeam.ToList();
            var excess = firstTeam.Count - SquadRules.Default.MaxPlayers;
            // Weakest first; an unhappy player counts BalanceBonus points weaker (docs/CHEMISTRY.md §7).
            foreach (var player in firstTeam.OrderBy(p => p.Evaluation.Overall - SquadBalance(p.Player)).ThenBy(p => p.Player.Id.Value))
            {
                if (excess <= 0)
                {
                    break;
                }

                // Only a player with one contract, at this club (never a loan).
                var own = running.Where(c => c.PlayerId == player.Player.Id).ToList();
                if (own is not [{ Status: ContractStatus.Active } contract] || contract.ClubId != clubId)
                {
                    continue;
                }

                await contracts.TerminateAsync(contract.Id, TerminationKind.MutualAgreement, cancellationToken: cancellationToken).ConfigureAwait(false);
                excess--;
                released++;
            }
        }

        return released;
    }

    public async Task<int> FillSquadsAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var signed = 0;
        var shortOf = await ClubsShortOfPlayersAsync(managedClubs, cancellationToken).ConfigureAwait(false);
        if (shortOf.Count == 0)
        {
            return 0;
        }

        // Best first, read once: a signed player leaves the pool (nothing else in it changes while squads are filled).
        var pool = await contracts.GetFreeAgentsAsync(cancellationToken).ConfigureAwait(false);
        var taken = new HashSet<PlayerId>();
        Dictionary<ClubId, long> wageBills;
        using (var scope = scopes.CreateScope())
        {
            // The club pays its players' salaries, loans in included; a player it loaned out is paid by the loan club.
            wageBills = (await Repo<Contract, ContractId>(scope.ServiceProvider).ListUntrackedAsync(Contract.RunningOn(today), cancellationToken).ConfigureAwait(false))
                .Where(c => c.PlayerId is not null && c.Status != ContractStatus.OnLoan)
                .GroupBy(c => c.ClubId)
                .ToDictionary(g => g.Key, g => g.Sum(c => c.AnnualSalary));
        }

        var end = today.AddYears(1).AddDays(-1);
        foreach (var roster in await clubs.GetRostersAsync(shortOf, cancellationToken).ConfigureAwait(false))
        {
            var clubId = roster.Club.Id;
            var firstTeam = roster.FirstTeam.Count();
            var missing = Math.Max(SquadRules.Default.MinPlayers - firstTeam, InjuryCover(roster, today));
            var balance = roster.Club.Balance;
            var room = roster.Club.WageBudget - wageBills.GetValueOrDefault(clubId);
            var next = 0;
            while (missing > 0)
            {
                // The best free agent whose salary fits the wage budget left (and the balance); the cheapest one the
                // balance covers only while the club is below the squad minimum (docs/AI_MANAGEMENT.md).
                var (pick, fallback) = await FindSigningAsync(clubId, pool, taken, next, today, end, balance, room, cancellationToken).ConfigureAwait(false);
                var below = firstTeam < SquadRules.Default.MinPlayers;
                if (pick is null && below && next > 0)
                {
                    // The cheapest player may be among those skipped before the last signing.
                    (_, fallback) = await FindSigningAsync(clubId, pool, taken, 0, today, end, balance, room, cancellationToken).ConfigureAwait(false);
                }
                if ((pick ?? (below ? fallback : null)) is not { } choice)
                {
                    break;
                }

                var playerId = pool[choice.Index].Player.Id;
                try
                {
                    await contracts.SignAsync(playerId, clubId, choice.Terms, RosterRole.Bench, cancellationToken).ConfigureAwait(false);
                }
                catch (DomainException)
                {
                    // E.g. injury cover after the registration deadline (only the squad minimum is exempt): no signing.
                    break;
                }

                taken.Add(playerId);
                balance -= choice.Terms.AnnualSalary;
                room -= choice.Terms.AnnualSalary;
                firstTeam++;
                missing--;
                signed++;
                // Better players skipped so far stay out of reach; after a minimum-squad signing the whole pool is read again.
                next = pick is not null ? choice.Index + 1 : 0;
            }
        }

        return signed;
    }

    /// <summary>
    /// From pool position <paramref name="from"/>: the first (best) free agent whose demands fit the balance and the wage
    /// budget left, else the cheapest one the balance covers. Demands are read in chunks (<see cref="NegotiationChunk"/>).
    /// </summary>
    private async Task<(Signing? Pick, Signing? Fallback)> FindSigningAsync(
        ClubId clubId,
        IReadOnlyList<FreeAgent> pool,
        HashSet<PlayerId> taken,
        int from,
        DateOnly today,
        DateOnly end,
        long balance,
        long room,
        CancellationToken cancellationToken)
    {
        Signing? fallback = null;
        var next = from;
        while (next < pool.Count)
        {
            List<int> chunk = [];
            for (var j = next; j < pool.Count && chunk.Count < NegotiationChunk; j++)
            {
                if (!taken.Contains(pool[j].Player.Id))
                {
                    chunk.Add(j);
                }
            }

            if (chunk.Count == 0)
            {
                break;
            }

            var talks = await contracts.GetNegotiationsAsync(clubId, [.. chunk.Select(j => pool[j].Player.Id)], cancellationToken).ConfigureAwait(false);
            for (var i = 0; i < chunk.Count; i++)
            {
                var terms = negotiator.Meet(new ContractTerms(today, end, 0), talks[i].Demands);
                if (terms.AnnualSalary > balance || talks[i].Talks.BrokenOffUntil is not null)
                {
                    continue;
                }

                if (terms.AnnualSalary <= room)
                {
                    return (new Signing(chunk[i], terms), fallback);
                }

                if (fallback is null || terms.AnnualSalary < fallback.Terms.AnnualSalary)
                {
                    fallback = new Signing(chunk[i], terms);
                }
            }

            next = chunk[^1] + 1;
        }

        return (null, fallback);
    }

    private sealed record Signing(int Index, ContractTerms Terms);

    public async Task<int> ReplenishFreeAgentsAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        var freeAgents = (await contracts.GetFreeAgentsAsync(cancellationToken).ConfigureAwait(false)).Count;
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var clubList = await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false);
        var missing = clubList.Count * IClubManagementService.FreeAgentsPerClub - freeAgents;
        var playerRepo = Repo<Player, PlayerId>(s);
        var players = await playerRepo.ListAsync(null, cancellationToken).ConfigureAwait(false);
        if (missing <= 0 || clubList.Count == 0 || players.Count == 0)
        {
            return 0;
        }

        var taken = players.Select(DuplicateKeys.Of).ToHashSet();
        var pools = players.GroupBy(p => p.NationalityId)
            .ToDictionary(g => g.Key, g => new NamePool(g.Key, [.. g.Select(p => p.FirstName).Distinct()], [.. g.Select(p => p.LastName).Distinct()]));
        var generator = new PlayerGenerator(HashCode.Combine(today.DayNumber, players.Count));
        var random = new Random(today.DayNumber);
        for (var i = 0; i < missing; i++)
        {
            // Mostly the clubs' own nationalities, in proportion to the clubs of each country.
            var country = clubList[random.Next(clubList.Count)].CountryId;
            var names = pools.GetValueOrDefault(country) ?? pools.Values.First();
            var player = generator.Create(names, generator.NextPosition(), today, (f, l, d) => taken.Contains(DuplicateKeys.Player(f, l, d)));
            taken.Add(DuplicateKeys.Of(player));
            playerRepo.Add(player);
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return missing;
    }

    public async Task<IReadOnlyList<ClubId>> ClubsShortOfPlayersAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default)
    {
        return
        [
            .. (await clubs.GetRostersAsync(await AiClubsAsync(managedClubs, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false))
                .Where(roster => roster.FirstTeam.Count() < SquadRules.Default.MinPlayers
                    || roster.FirstTeam.Count(p => p.Availability != PlayerAvailability.Unavailable) < SquadRules.Starters
                    || InjuryCover(roster, clock.Today) > 0)
                .Select(roster => roster.Club.Id),
        ];
    }

    public async Task SetObjectivesAsync(SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var entries = await Repo<CompetitionParticipant, CompetitionParticipantId>(s).ListAsync(p => p.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var leagues = (await Repo<Competition, CompetitionId>(s).ListAsync(c => c.Type == CompetitionType.League, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
        var clubRepo = Repo<Club, ClubId>(s);
        var all = (await clubRepo.ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
        foreach (var group in entries.Where(e => leagues.ContainsKey(e.CompetitionId) && all.ContainsKey(e.ClubId)).GroupBy(e => e.CompetitionId))
        {
            var rules = leagues[group.Key].ParseRules();
            var ranked = group.Select(e => all[e.ClubId]).OrderByDescending(c => c.Reputation).ThenBy(c => c.Id.Value).ToList();
            for (var i = 0; i < ranked.Count; i++)
            {
                var club = ranked[i];
                var expectation = SeasonObjectives.For(i + 1, ranked.Count, rules.Playoffs?.Teams ?? 0, rules.Promotion.Places, rules.Relegation.Direct);
                club.UpdateProfile(club.City, club.Reputation, club.Balance, club.WageBudget, expectation);
            }
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Squad balance (docs/CHEMISTRY.md §7): an unhappy player gets minutes ahead of a slightly better teammate and is released first.</summary>
    private static int SquadBalance(Player player) => player.Morale <= ChemistryModel.UnhappyMorale ? ChemistryModel.BalanceBonus : 0;

    private async Task<IReadOnlyList<ClubId>> AiClubsAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(managedClubs);
        using var scope = scopes.CreateScope();
        return
        [
            .. (await Repo<Club, ClubId>(scope.ServiceProvider).ListAsync(null, cancellationToken).ConfigureAwait(false))
                .Where(c => !managedClubs.Contains(c.Id))
                .OrderBy(c => c.Name)
                .Select(c => c.Id),
        ];
    }

    /// <summary>
    /// Signings an AI club needs to cover long-term injuries (docs/INJURIES.md §5): players expected back in more than
    /// <see cref="LongTermInjuryDays"/> days do not count towards the minimum squad; limited by the room left in the squad.
    /// </summary>
    internal static int InjuryCover(ClubRoster roster, DateOnly today)
    {
        var firstTeam = roster.FirstTeam.ToList();
        var fit = firstTeam.Count(p => !(p.Player.InjuryStatus == InjuryStatus.Injured && p.Player.InjuryReturnDate > today.AddDays(LongTermInjuryDays)));
        return Math.Clamp(SquadRules.Default.MinPlayers - fit, 0, Math.Max(0, SquadRules.Default.MaxPlayers - firstTeam.Count));
    }

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();
}
