using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Management;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Domain.Seasons;
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
    GameClock clock) : IClubManagementService
{
    public async Task<int> PrepareMatchdayAsync(IReadOnlyCollection<ClubId> clubIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clubIds);
        var changed = 0;
        foreach (var clubId in clubIds)
        {
            if (await clubs.GetRosterAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } roster)
            {
                continue;
            }

            var plan = LineupPlanner.Plan([.. roster.Players.Select(p => new LineupCandidate(p.Player.Id, p.Role, p.Availability, p.Evaluation))]);
            var current = roster.Players.ToDictionary(p => p.Player.Id);
            var rotation = roster.Rotation.Select(p => p.Player.Id).ToList();
            var same = plan.All(p => current[p.PlayerId].Role == p.Role && current[p.PlayerId].LineupPosition == p.LineupPosition)
                && plan.Where(p => p.Role == RosterRole.Rotation).Select(p => p.PlayerId).SequenceEqual(rotation);
            if (!same)
            {
                await clubs.SaveRosterAsync(clubId, [.. plan.Select(p => new RosterAssignment(p.PlayerId, p.Role, p.LineupPosition))], cancellationToken).ConfigureAwait(false);
                changed++;
            }
        }

        return changed;
    }

    public async Task<int> PlanTacticsAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default)
    {
        var planned = 0;
        foreach (var clubId in await AiClubsAsync(managedClubs, cancellationToken).ConfigureAwait(false))
        {
            if (await clubs.GetRosterAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } roster)
            {
                continue;
            }

            var plan = TacticPlanner.Plan([.. roster.FirstTeam.Select(p => (p.Player, p.Evaluation))]);
            var existing = (await tactics.GetClubTacticsAsync(clubId, cancellationToken).ConfigureAwait(false))?.Tactics.FirstOrDefault(t => t.Name == IClubManagementService.AiTacticName);
            var tactic = await tactics.SaveAsync(
                clubId,
                new TacticDraft(existing?.Id, IClubManagementService.AiTacticName, plan.OffensiveSystem, plan.Offense, plan.DefensiveSystem, plan.Defense, plan.Assignments),
                cancellationToken).ConfigureAwait(false);
            if (!tactic.IsSelected)
            {
                await tactics.SelectAsync(tactic.Id, cancellationToken).ConfigureAwait(false);
            }

            planned++;
        }

        return planned;
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
                if (terms is null
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

        foreach (var clubId in await AiClubsAsync(managedClubs, cancellationToken).ConfigureAwait(false))
        {
            if (await clubs.GetRosterAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } roster)
            {
                continue;
            }

            var firstTeam = roster.FirstTeam.ToList();
            var excess = firstTeam.Count - SquadRules.Default.MaxPlayers;
            foreach (var player in firstTeam.OrderBy(p => p.Evaluation.Overall).ThenBy(p => p.Player.Id.Value))
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
        foreach (var clubId in await ClubsShortOfPlayersAsync(managedClubs, cancellationToken).ConfigureAwait(false))
        {
            var roster = (await clubs.GetRosterAsync(clubId, cancellationToken).ConfigureAwait(false))!;
            var missing = SquadRules.Default.MinPlayers - roster.FirstTeam.Count();
            var balance = roster.Club.Balance;
            foreach (var agent in await contracts.GetFreeAgentsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (missing <= 0)
                {
                    break;
                }

                var end = today.AddYears(1).AddDays(-1);
                var asking = (await contracts.NegotiateAsync(agent.Player.Id, clubId, new ContractTerms(today, end, 1), cancellationToken).ConfigureAwait(false)).AskingSalary;
                if (asking > balance)
                {
                    continue;
                }

                await contracts.SignAsync(agent.Player.Id, clubId, new ContractTerms(today, end, asking), RosterRole.Bench, cancellationToken).ConfigureAwait(false);
                balance -= asking;
                missing--;
                signed++;
            }
        }

        return signed;
    }

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
        var shortOf = new List<ClubId>();
        foreach (var clubId in await AiClubsAsync(managedClubs, cancellationToken).ConfigureAwait(false))
        {
            if (await clubs.GetRosterAsync(clubId, cancellationToken).ConfigureAwait(false) is { } roster
                && (roster.FirstTeam.Count() < SquadRules.Default.MinPlayers
                    || roster.FirstTeam.Count(p => p.Availability != PlayerAvailability.Unavailable) < SquadRules.Starters))
            {
                shortOf.Add(clubId);
            }
        }

        return shortOf;
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

    /// <summary>Books the salaries due and the month-end income of <paramref name="date"/> in the unit of work of <paramref name="s"/>.</summary>
    internal static async Task<(long Salaries, long Income)> AddFinancesAsync(IServiceProvider s, DateOnly date, CancellationToken cancellationToken)
    {
        var clubRepo = Repo<Club, ClubId>(s);
        var all = (await clubRepo.ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
        long salaries = 0, income = 0;
        foreach (var contract in await Repo<Contract, ContractId>(s).ListAsync(Contract.RunningOn(date), cancellationToken).ConfigureAwait(false))
        {
            var due = ClubFinances.SalaryDue(contract, date);
            if (due > 0 && all.TryGetValue(contract.ClubId, out var payer))
            {
                payer.ChangeBalance(-due);
                salaries += due;
            }
        }

        if (ClubFinances.IsIncomeDay(date))
        {
            foreach (var club in all.Values)
            {
                var amount = ClubFinances.MonthlyIncome(club);
                club.ChangeBalance(amount);
                income += amount;
            }
        }

        return (salaries, income);
    }

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

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();
}
