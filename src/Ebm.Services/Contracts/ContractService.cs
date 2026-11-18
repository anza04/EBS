using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Finances;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;
using Ebm.Domain.Transfers;
using Ebm.Services.Clubs;
using Ebm.Services.Data;
using Ebm.Services.Finances;
using Ebm.Services.Transfers;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Contracts;

/// <summary>Implements <see cref="IContractService"/> over the repositories, the <see cref="ContractNegotiator"/> and the salary calculator.</summary>
internal sealed class ContractService : IContractService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PlayerRatingCalculator _calculator;
    private readonly ContractNegotiator _negotiator;
    private readonly GameClock _time;

    public ContractService(IServiceScopeFactory scopes, PlayerRatingCalculator calculator, ContractNegotiator negotiator, GameClock time)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(negotiator);
        ArgumentNullException.ThrowIfNull(time);
        _scopes = scopes;
        _calculator = calculator;
        _negotiator = negotiator;
        _time = time;
    }

    private DateOnly Today => _time.Today;

    public async Task<ClubContracts?> GetClubContractsAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        if (await data.Clubs.FindAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } club)
        {
            return null;
        }

        var contracts = await data.Contracts.ListAsync(c => c.ClubId == clubId && c.PlayerId != null, cancellationToken).ConfigureAwait(false);
        var playerIds = contracts.Select(c => c.PlayerId!.Value).Distinct().ToList();
        var names = (await data.Players.ListAsync(p => playerIds.Contains(p.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id, p => p.FullName);
        var lines = contracts.Select(c => data.Line(c, names.GetValueOrDefault(c.PlayerId!.Value) ?? string.Empty, club.Name)).ToList();
        var current = lines.Where(l => l.IsRunning || l.IsUpcoming).ToList();
        return new ClubContracts(
            club,
            data.Today,
            [.. current.OrderBy(l => l.IsUpcoming).ThenByDescending(l => l.Contract.AnnualSalary).ThenBy(l => l.Player)],
            [.. lines.Except(current).OrderByDescending(l => l.Contract.EffectiveEndDate).ThenBy(l => l.Player)],
            SalaryCommitments.Calculate(contracts, data.Today));
    }

    public async Task<PlayerContracts?> GetPlayerContractsAsync(PlayerId playerId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        if (await data.Players.FindAsync(playerId, cancellationToken).ConfigureAwait(false) is not { } player)
        {
            return null;
        }

        var contracts = await data.Contracts.ListAsync(c => c.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
        var clubIds = contracts.Select(c => c.ClubId).ToList();
        var currentClubId = (await ClubMembership.CurrentClubsAsync(scope.ServiceProvider, data.Today, cancellationToken).ConfigureAwait(false))
            .TryGetValue(playerId, out var cid) ? cid : (ClubId?)null;
        if (currentClubId is { } current)
        {
            clubIds.Add(current);
        }

        var clubs = (await data.Clubs.ListAsync(c => clubIds.Contains(c.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name);
        var lines = contracts
            .Select(c => data.Line(c, player.FullName, clubs.GetValueOrDefault(c.ClubId) ?? string.Empty))
            .OrderByDescending(l => l.Contract.StartDate)
            .ToList();
        var running = lines.Where(l => l.IsRunning).OrderByDescending(l => l.Contract.StartDate).FirstOrDefault();
        var upcoming = lines.Where(l => l.IsUpcoming).OrderBy(l => l.Contract.StartDate).FirstOrDefault();
        return new PlayerContracts(
            player,
            data.Today,
            currentClubId is { } id ? clubs.GetValueOrDefault(id) : null,
            running,
            upcoming,
            lines,
            running is null && upcoming is null);
    }

    public async Task<IReadOnlyList<FreeAgent>> GetFreeAgentsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var contracts = await data.Contracts.ListAsync(c => c.PlayerId != null, cancellationToken).ConfigureAwait(false);
        var bound = contracts.Where(c => c.IsRunningOn(data.Today) || c.IsUpcomingOn(data.Today)).Select(c => c.PlayerId!.Value).ToHashSet();
        var lastClub = contracts.GroupBy(c => c.PlayerId!.Value).ToDictionary(g => g.Key, g => g.MaxBy(c => c.StartDate)!.ClubId);
        var clubs = (await data.Clubs.ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name);
        var countries = (await data.Repo<Country, CountryId>().ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name);
        var players = await data.Players.ListAsync(p => p.RetiredOn == null, cancellationToken).ConfigureAwait(false);
        return
        [
            .. players
                .Where(p => !bound.Contains(p.Id))
                .Select(p => new FreeAgent(
                    p,
                    p.AgeOn(data.Today),
                    _calculator.Evaluate(p).Overall,
                    countries.GetValueOrDefault(p.NationalityId) ?? string.Empty,
                    lastClub.TryGetValue(p.Id, out var club) ? clubs.GetValueOrDefault(club) : null))
                .OrderByDescending(f => f.Overall)
                .ThenBy(f => f.Player.LastName),
        ];
    }

    public async Task<NegotiationResponse> NegotiateAsync(PlayerId playerId, ClubId clubId, ContractTerms offer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var (player, club) = await data.PlayerAndClubAsync(playerId, clubId, cancellationToken).ConfigureAwait(false);
        await CheckFreeAsync(data, player, club, offer, cancellationToken).ConfigureAwait(false);
        return await RoundAsync(data, player, club, null, offer, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NegotiationResponse> NegotiateRenewalAsync(ContractId contractId, ContractTerms offer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var (contract, player, club) = await data.PlayerContractAsync(contractId, cancellationToken).ConfigureAwait(false);
        CheckRenewable(contract, offer, data.Today);
        return await RoundAsync(data, player, club, contract, offer, cancellationToken).ConfigureAwait(false);
    }

    public async Task<NegotiationOverview> GetNegotiationAsync(PlayerId playerId, ClubId clubId, ContractId? renewing = null, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        Contract? current = null;
        Player player;
        Club club;
        if (renewing is { } id)
        {
            (current, player, club) = await data.PlayerContractAsync(id, cancellationToken).ConfigureAwait(false);
            if (current.PlayerId != playerId || current.ClubId != clubId)
            {
                throw new DomainValidationException("The contract belongs to another player or club.");
            }
        }
        else
        {
            (player, club) = await data.PlayerAndClubAsync(playerId, clubId, cancellationToken).ConfigureAwait(false);
        }

        var talks = await TalksAsync(data, player, club, current, cancellationToken).ConfigureAwait(false);
        // The demands a signing must meet now: the concessions of the rounds already played (as SignAsync).
        var demands = _negotiator.Demands(talks.Context with { Round = talks.CurrentRound });
        return new NegotiationOverview(demands, talks.Describe(demands));
    }

    public async Task<IReadOnlyList<NegotiationOverview>> GetNegotiationsAsync(ClubId clubId, IReadOnlyList<PlayerId> playerIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playerIds);
        using var scope = _scopes.CreateScope();
        // One read-only unit of work: the contracts, clubs, agents and squad shared by every player are read once.
        var data = new Data(scope.ServiceProvider, Today) { Memoize = true };
        var overviews = new List<NegotiationOverview>(playerIds.Count);
        foreach (var playerId in playerIds)
        {
            var (player, club) = await data.PlayerAndClubAsync(playerId, clubId, cancellationToken).ConfigureAwait(false);
            var talks = await TalksAsync(data, player, club, null, cancellationToken).ConfigureAwait(false);
            var demands = _negotiator.Demands(talks.Context with { Round = talks.CurrentRound });
            overviews.Add(new NegotiationOverview(demands, talks.Describe(demands)));
        }

        return overviews;
    }

    public async Task<PromiseReview> ReviewPromisesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var today = data.Today;
        var due = (await data.Contracts.ListAsync(c => c.PlayerId != null && (c.PromisedRole != null || c.PromisedMinutes != null), cancellationToken).ConfigureAwait(false))
            .Where(c => c.Status == ContractStatus.Active && c.PromiseReviewDue(today))
            .ToList();
        if (due.Count == 0)
        {
            return PromiseReview.None;
        }

        var from = today.AddDays(-PromiseRules.ReviewDays + 1);
        var clubIds = due.Select(c => c.ClubId).Distinct().ToList();
        var fixtureIds = (await data.Repo<Fixture, FixtureId>().ListAsync(
                f => f.Date >= from && f.Date <= today && (clubIds.Contains(f.HomeClubId) || clubIds.Contains(f.AwayClubId)),
                cancellationToken).ConfigureAwait(false))
            .Select(f => f.Id)
            .ToList();
        var matchIds = (await data.Repo<Match, MatchId>().ListAsync(m => fixtureIds.Contains(m.FixtureId) && !m.IsForfeit, cancellationToken).ConfigureAwait(false))
            .Select(m => m.Id)
            .ToList();
        var lines = await data.Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>().ListAsync(
            s => matchIds.Contains(s.MatchId) && clubIds.Contains(s.ClubId), cancellationToken).ConfigureAwait(false);
        var playerIds = due.Select(c => c.PlayerId!.Value).ToList();
        var players = (await data.Players.ListAsync(p => playerIds.Contains(p.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id);
        var agents = await data.Repo<Agent, AgentId>().ListAsync(null, cancellationToken).ConfigureAwait(false);
        int kept = 0, atRisk = 0, broken = 0;
        foreach (var contract in due)
        {
            var games = lines.Where(l => l.ClubId == contract.ClubId).Select(l => l.MatchId).Distinct().Count();
            if (games < PromiseRules.MinGames || !players.TryGetValue(contract.PlayerId!.Value, out var player)
                || player.InjuryStatus != InjuryStatus.Healthy)
            {
                // Off-season, or he cannot play: the promise is not judged on this window.
                continue;
            }

            var minutes = lines.Where(l => l.ClubId == contract.ClubId && l.PlayerId == player.Id).Sum(l => l.BoxScore.Minutes) / (double)games;
            var status = PromiseRules.Assess(contract.Promises!.TargetMinutes, minutes);
            var newlyBroken = contract.RecordPromiseReview(today, status);
            player.UpdateCondition(
                player.Fitness,
                Math.Clamp(player.Morale + PromiseRules.MoraleChange(status), Player.MinRating, Player.MaxRating),
                player.InjuryStatus,
                player.InjuryReturnDate);
            if (AgentPool.For(player.Id, player.Reputation, agents) is { } agent && (newlyBroken || status == PromiseStatus.Kept))
            {
                (await RelationshipAsync(data, agent, contract.ClubId, cancellationToken).ConfigureAwait(false))
                    .Adjust(newlyBroken ? AgentPool.PromiseBroken : AgentPool.PromiseKept);
            }

            _ = status switch
            {
                PromiseStatus.Kept => kept++,
                PromiseStatus.AtRisk => atRisk++,
                _ => broken++,
            };
        }

        await data.SaveAsync(cancellationToken).ConfigureAwait(false);
        return new PromiseReview(kept, atRisk, broken);
    }

    public async Task<ContractId> SignAsync(PlayerId playerId, ClubId clubId, ContractTerms terms, RosterRole joinAs = RosterRole.Bench, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);
        if (joinAs is not (RosterRole.Bench or RosterRole.Inactive or RosterRole.Youth))
        {
            throw new DomainValidationException("A new player joins the bench, the inactive list or the youth squad; pick his starting or rotation role in the squad.");
        }

        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var (player, club) = await data.PlayerAndClubAsync(playerId, clubId, cancellationToken).ConfigureAwait(false);
        if (terms.StartDate < data.Today)
        {
            throw new DomainValidationException($"A new contract cannot start before today ({data.Today:yyyy-MM-dd}).");
        }

        await CheckFreeAsync(data, player, club, terms, cancellationToken).ConfigureAwait(false);
        var talks = await TalksAsync(data, player, club, null, cancellationToken).ConfigureAwait(false);
        RequireAcceptance(talks, _negotiator.Evaluate(talks.Context with { Round = talks.CurrentRound }, terms));

        var contract = Contract.ForPlayer(ContractId.New(), clubId, playerId, terms);
        var current = (await ClubMembership.CurrentClubsAsync(scope.ServiceProvider, data.Today, cancellationToken).ConfigureAwait(false)).TryGetValue(playerId, out var at) ? at : (ClubId?)null;
        var joins = contract.IsRunningOn(data.Today) && current != clubId;
        if (joins)
        {
            await ClubSeason.CheckRegistrationAsync(scope.ServiceProvider, club, data.Today, joinAs, cancellationToken).ConfigureAwait(false);
        }

        data.Contracts.Add(contract);
        Ledger.Book(scope.ServiceProvider, club, data.Today, FinanceCategory.SigningBonuses, -terms.SigningBonus, $"Signing bonus of {player.FullName}");
        if (joins)
        {
            await ClubMembership.JoinAsync(scope.ServiceProvider, playerId, clubId, data.Today, joinAs, cancellationToken).ConfigureAwait(false);
        }

        // The signing is part of the transfer history (docs/TRANSFERS.md §1).
        data.Repo<Transfer, TransferId>().Add(Transfer.Signing(TransferId.New(), playerId, current == clubId ? null : current, clubId, terms.StartDate, contract.Id));
        await AgreeAsync(data, talks, clubId, cancellationToken).ConfigureAwait(false);
        await data.SaveAsync(cancellationToken).ConfigureAwait(false);
        return contract.Id;
    }

    public async Task<ContractId> RenewAsync(ContractId contractId, ContractTerms terms, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(terms);
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var (contract, player, club) = await data.PlayerContractAsync(contractId, cancellationToken).ConfigureAwait(false);
        CheckRenewable(contract, terms, data.Today);
        var talks = await TalksAsync(data, player, club, contract, cancellationToken).ConfigureAwait(false);
        RequireAcceptance(talks, _negotiator.Evaluate(talks.Context with { Round = talks.CurrentRound }, terms));

        contract.Supersede(terms.StartDate);
        var renewal = Contract.ForPlayer(ContractId.New(), contract.ClubId, player.Id, terms);
        data.Contracts.Add(renewal);
        Ledger.Book(scope.ServiceProvider, club, data.Today, FinanceCategory.SigningBonuses, -terms.SigningBonus, $"Renewal bonus of {player.FullName}");
        await AgreeAsync(data, talks, club.Id, cancellationToken).ConfigureAwait(false);
        await data.SaveAsync(cancellationToken).ConfigureAwait(false);
        return renewal.Id;
    }

    public async Task<long> TerminateAsync(ContractId contractId, TerminationKind kind, long? settlement = null, CancellationToken cancellationToken = default)
    {
        Guard(kind);
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var (contract, player, club) = await data.PlayerContractAsync(contractId, cancellationToken).ConfigureAwait(false);
        if (contract.StatusOn(data.Today) != ContractStatus.Active)
        {
            throw new DomainValidationException($"The contract is {StatusName(contract.StatusOn(data.Today))}; only an active contract can be terminated.");
        }

        var remaining = SalaryCalculator.RemainingSalary(contract, data.Today);
        var minimum = MinimumBuyout(data.Today, player, contract, remaining);
        var paid = kind == TerminationKind.ClubRelease ? remaining : settlement ?? minimum;
        if (paid < 0 || paid > remaining)
        {
            throw new DomainValidationException($"The settlement must be between 0 and the remaining salary (€ {remaining:N0}).");
        }

        if (paid < minimum)
        {
            throw new DomainException($"{player.FullName} refuses the buyout: he wants at least € {minimum:N0} of the € {remaining:N0} still owed to him.");
        }

        contract.Terminate(data.Today, paid);
        Ledger.Book(scope.ServiceProvider, club, data.Today, FinanceCategory.Settlements, -paid, $"Contract settlement of {player.FullName}");

        // The player stays only if another contract with the club runs today (e.g. the one this renewal would replace).
        var others = await data.Contracts.ListAsync(c => c.PlayerId == player.Id && c.ClubId == club.Id && c.Id != contract.Id, cancellationToken).ConfigureAwait(false);
        if (!others.Any(c => c.IsRunningOn(data.Today)))
        {
            await ClubMembership.LeaveAsync(scope.ServiceProvider, player.Id, club.Id, data.Today, cancellationToken).ConfigureAwait(false);
        }

        await data.SaveAsync(cancellationToken).ConfigureAwait(false);
        return paid;
    }

    public async Task<BuyoutResponse> NegotiateBuyoutAsync(ContractId contractId, long settlement, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var (contract, player, _) = await data.PlayerContractAsync(contractId, cancellationToken).ConfigureAwait(false);
        if (contract.StatusOn(data.Today) != ContractStatus.Active)
        {
            throw new DomainValidationException($"The contract is {StatusName(contract.StatusOn(data.Today))}; only an active contract can be bought out.");
        }

        var remaining = SalaryCalculator.RemainingSalary(contract, data.Today);
        var minimum = MinimumBuyout(data.Today, player, contract, remaining);
        return settlement >= minimum && settlement <= remaining
            ? new BuyoutResponse(true, minimum, remaining, $"{player.FullName} accepts € {settlement:N0} to end his contract.")
            : settlement > remaining
                ? new BuyoutResponse(false, minimum, remaining, $"The settlement cannot exceed the remaining salary (€ {remaining:N0}).")
                : new BuyoutResponse(false, minimum, remaining, $"{player.FullName} wants at least € {minimum:N0} of the € {remaining:N0} still owed to him.");
    }

    /// <summary>The least settlement the player accepts to leave: a share of what is still owed to him, by how easily he would earn as much elsewhere.</summary>
    private long MinimumBuyout(DateOnly today, Player player, Contract contract, long remaining) =>
        _negotiator.MinimumBuyout(Context(player, TransferNegotiator.NeutralClubReputation, today, null), contract.AnnualSalary, remaining);

    public async Task DecideClubOptionAsync(ContractId contractId, bool exercise, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var (contract, _, _) = await data.PlayerContractAsync(contractId, cancellationToken).ConfigureAwait(false);
        if (exercise)
        {
            contract.ExerciseOption(OptionHolder.Club, data.Today);
        }
        else
        {
            contract.DeclineOption(OptionHolder.Club, data.Today);
        }

        await data.SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DecidePlayerOptionAsync(ContractId contractId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var (contract, player, club) = await data.PlayerContractAsync(contractId, cancellationToken).ConfigureAwait(false);
        var exercise = _negotiator.ExercisesPlayerOption(await ContextAsync(data, player, club, null, cancellationToken).ConfigureAwait(false), contract.AnnualSalary);
        if (exercise)
        {
            contract.ExerciseOption(OptionHolder.Player, data.Today);
        }
        else
        {
            contract.DeclineOption(OptionHolder.Player, data.Today);
        }

        await data.SaveAsync(cancellationToken).ConfigureAwait(false);
        return exercise;
    }

    public async Task<ContractDateChanges> ProcessContractDatesAsync(CancellationToken cancellationToken = default)
    {
        var today = Today;
        var leavers = new List<PlayerId>();
        int expired;
        using (var scope = _scopes.CreateScope())
        {
            var data = new Data(scope.ServiceProvider, today);
            var due = await data.Contracts.ListAsync(c => c.Status == ContractStatus.Active && c.EndDate < today, cancellationToken).ConfigureAwait(false);
            expired = due.Count;
            foreach (var contract in due)
            {
                contract.Expire(today);
                if (contract.PlayerId is not { } playerId)
                {
                    continue;
                }

                var stays = (await data.Contracts.ListAsync(c => c.PlayerId == playerId && c.ClubId == contract.ClubId && c.Id != contract.Id, cancellationToken).ConfigureAwait(false))
                    .Any(c => c.IsRunningOn(today) || c.IsUpcomingOn(today));
                if (!stays)
                {
                    await ClubMembership.LeaveAsync(scope.ServiceProvider, playerId, contract.ClubId, contract.EndDate, cancellationToken).ConfigureAwait(false);
                    leavers.Add(playerId);
                }
            }

            await data.SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        // Contracts signed in advance: once one runs, its player moves to the new club (saved separately, so the
        // spells and roster spots closed above are already in the database).
        var started = 0;
        var freeAgents = new List<PlayerId>();
        using (var scope = _scopes.CreateScope())
        {
            var data = new Data(scope.ServiceProvider, today);
            var running = (await data.Contracts.ListAsync(Contract.RunningOn(today), cancellationToken).ConfigureAwait(false))
                .Where(c => c.PlayerId is not null)
                .ToList();
            // Only the open spells, and for players without one whether any spell exists (not the whole history every day).
            var open = (await ClubMembership.OpenSpellsAsync(scope.ServiceProvider, null, cancellationToken).ConfigureAwait(false))
                .GroupBy(s => s.PlayerId)
                .ToDictionary(g => g.Key, g => g.First());
            List<PlayerId> withoutOpen = [.. running.Select(c => c.PlayerId!.Value).Distinct().Where(p => !open.ContainsKey(p))];
            var tracked = withoutOpen.Count == 0
                ? []
                : (await data.Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>().ListAsync(s => withoutOpen.Contains(s.PlayerId), cancellationToken).ConfigureAwait(false))
                    .Select(s => s.PlayerId)
                    .ToHashSet();
            foreach (var contract in running.OrderBy(c => c.StartDate))
            {
                var playerId = contract.PlayerId!.Value;
                var spell = open.GetValueOrDefault(playerId);

                // Players whose club history is not tracked (imported contracts only) keep belonging through the contract.
                var moves = spell is null ? tracked.Contains(playerId) : spell.ClubId != contract.ClubId && spell.StartDate < contract.StartDate;
                if (moves)
                {
                    await ClubMembership.JoinAsync(scope.ServiceProvider, playerId, contract.ClubId, contract.StartDate, RosterRole.Bench, cancellationToken).ConfigureAwait(false);
                    open.Remove(playerId); // the new spell is not saved yet: as before, the player then counts as tracked without an open spell
                    tracked.Add(playerId);
                    started++;
                }
            }

            await data.SaveAsync(cancellationToken).ConfigureAwait(false);
            var bound = running.Select(c => c.PlayerId!.Value).ToHashSet();
            freeAgents.AddRange(leavers.Distinct().Where(p => !bound.Contains(p)));
        }

        return expired == 0 && started == 0 ? ContractDateChanges.None : new ContractDateChanges(expired, started, freeAgents);
    }

    /// <summary>The negotiation with <paramref name="club"/>, priced with its attractiveness (docs/TRANSFERS.md §2).</summary>
    private async Task<NegotiationContext> ContextAsync(Data data, Player player, Club club, long? currentSalary, CancellationToken cancellationToken) =>
        Context(player, await data.AttractivenessAsync(club, cancellationToken).ConfigureAwait(false), data.Today, currentSalary);

    private NegotiationContext Context(Player player, int clubReputation, DateOnly today, long? currentSalary)
    {
        var evaluation = _calculator.Evaluate(player);
        return new NegotiationContext(evaluation.Overall, evaluation.PotentialRating, player.Reputation, player.AgeOn(today), clubReputation, currentSalary);
    }

    /// <summary>Refuses an offer to a player who is under contract (or signed in advance) during the offered period.</summary>
    private static async Task CheckFreeAsync(Data data, Player player, Club club, ContractTerms terms, CancellationToken cancellationToken)
    {
        var contracts = await data.Contracts.ListAsync(c => c.PlayerId == player.Id, cancellationToken).ConfigureAwait(false);
        var clash = contracts
            .Where(c => (c.IsRunningOn(data.Today) || c.IsUpcomingOn(data.Today)) && c.StartDate <= terms.EndDate && c.EffectiveEndDate >= terms.StartDate)
            .OrderBy(c => c.StartDate)
            .FirstOrDefault();
        if (clash is null)
        {
            return;
        }

        if (clash.ClubId == club.Id)
        {
            throw new DomainValidationException($"{player.FullName} already has a contract with {club.Name} until {clash.EffectiveEndDate:yyyy-MM-dd}. Renew it instead.");
        }

        var other = await data.Clubs.FindAsync(clash.ClubId, cancellationToken).ConfigureAwait(false);
        throw new DomainValidationException(
            $"{player.FullName} is under contract with {other?.Name ?? "another club"} until {clash.EffectiveEndDate:yyyy-MM-dd}. " +
            $"Offer a contract starting after that date, or wait for the transfer market.");
    }

    private static void CheckRenewable(Contract contract, ContractTerms terms, DateOnly today)
    {
        if (contract.StatusOn(today) != ContractStatus.Active)
        {
            throw new DomainValidationException(
                $"The contract is {StatusName(contract.StatusOn(today))}; only an active contract can be renewed. Offer a new contract instead.");
        }

        if (terms.StartDate < today)
        {
            throw new DomainValidationException($"A renewal cannot start before today ({today:yyyy-MM-dd}).");
        }

        if (terms.StartDate <= contract.StartDate || terms.StartDate > contract.EndDate.AddDays(1))
        {
            throw new DomainValidationException(
                $"A renewal must start after {contract.StartDate:yyyy-MM-dd} and no later than {contract.EndDate.AddDays(1):yyyy-MM-dd}, the day after the current contract ends.");
        }
    }

    private static string StatusName(ContractStatus status) => PlayerCatalog.DisplayName(status).ToLowerInvariant();

    private static void RequireAcceptance(Talks talks, NegotiationResponse response)
    {
        if (talks.Refusal is { } refusal)
        {
            throw new DomainException(refusal);
        }

        if (!response.IsAccepted)
        {
            throw new DomainException(response.Message);
        }
    }

    /// <summary>One round of talks: the player's answer to <paramref name="offer"/>, counted and saved (docs/CONTRACTS.md §4.5).</summary>
    private async Task<NegotiationResponse> RoundAsync(Data data, Player player, Club club, Contract? current, ContractTerms offer, CancellationToken cancellationToken)
    {
        var talks = await TalksAsync(data, player, club, current, cancellationToken).ConfigureAwait(false);
        var response = _negotiator.Evaluate(talks.Context with { Round = talks.NextRound }, offer);
        if (talks.Refusal is { } refusal)
        {
            return response with { Outcome = NegotiationOutcome.Rejected, CounterOffer = null, Message = refusal, Talks = talks.Describe(response.Demands!) };
        }

        var session = talks.Session;
        if (session is null)
        {
            session = new ContractNegotiation(ContractNegotiationId.New(), player.Id, club.Id, current?.Id, data.Today, response.Demands!.Patience);
            data.Repo<ContractNegotiation, ContractNegotiationId>().Add(session);
        }

        if (session.RecordRound(data.Today, response.Outcome))
        {
            if (talks.Agent is { } agent)
            {
                (await RelationshipAsync(data, agent, club.Id, cancellationToken).ConfigureAwait(false)).Adjust(AgentPool.TalksBrokenOff);
            }

            response = response with
            {
                Outcome = NegotiationOutcome.Rejected,
                CounterOffer = null,
                Message = $"{response.Message} {player.FullName} has lost patience and breaks off the talks until {session.BrokenOffUntil:yyyy-MM-dd}.",
            };
        }

        await data.SaveAsync(cancellationToken).ConfigureAwait(false);
        return response with { Talks = talks.Describe(response.Demands!, session) };
    }

    /// <summary>Closes the talks after a signing or renewal; the agent appreciates the deal.</summary>
    private static async Task AgreeAsync(Data data, Talks talks, ClubId clubId, CancellationToken cancellationToken)
    {
        talks.Session?.Agree(data.Today);
        if (talks.Agent is { } agent)
        {
            (await RelationshipAsync(data, agent, clubId, cancellationToken).ConfigureAwait(false)).Adjust(AgentPool.DealSigned);
        }
    }

    /// <summary>
    /// Everything the player weighs in talks with <paramref name="club"/>: his market price there, his expected role
    /// (rank in the club's squad), his agent and the agent's relationship with the club, rival clubs bidding for him
    /// (free agents and contracts ending within 180 days) and the open talks (docs/CONTRACTS.md §4).
    /// </summary>
    private async Task<Talks> TalksAsync(Data data, Player player, Club club, Contract? current, CancellationToken cancellationToken)
    {
        var today = data.Today;
        var context = await ContextAsync(data, player, club, current?.AnnualSalary, cancellationToken).ConfigureAwait(false);
        var agents = await data.AgentsAsync(cancellationToken).ConfigureAwait(false);

        var agent = AgentPool.For(player.Id, player.Reputation, agents);
        var relationship = agent is null
            ? 0
            : (await data.Repo<AgentRelationship, AgentRelationshipId>().ListAsync(r => r.AgentId == agent.Id && r.ClubId == club.Id, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault()?.Score ?? 0;
        var sessions = await data.Repo<ContractNegotiation, ContractNegotiationId>().ListAsync(n => n.PlayerId == player.Id && n.ClubId == club.Id, cancellationToken).ConfigureAwait(false);
        var latest = sessions.OrderByDescending(n => n.LastActivityOn).ThenByDescending(n => n.Rounds).FirstOrDefault();
        var refusedUntil = sessions.Where(n => n.RefusesTalksOn(today)).Select(n => n.BrokenOffUntil).Max();
        var session = latest is not null && latest.IsOpenOn(today) && latest.RenewalOfId == current?.Id ? latest : null;

        var running = await data.RunningAsync(cancellationToken).ConfigureAwait(false);
        var squad = (await data.SquadAsync(club.Id, running, cancellationToken).ConfigureAwait(false)).Where(p => p.Id != player.Id);
        var rank = 1 + squad.Count(p => _calculator.Evaluate(p).Overall > context.Overall);
        var rivals = current is null || current.EndDate <= today.AddDays(180)
            ? await RivalsAsync(data, player, club, context, running, cancellationToken).ConfigureAwait(false)
            : [];

        context = context with
        {
            ExpectedRole = PromiseRules.Expected(rank, context.Age),
            AgentToughness = agent?.Toughness ?? 50,
            Relationship = relationship,
            CompetingOffers = rivals.Count,
            BrokenPromise = current?.PromiseStatus == PromiseStatus.Broken,
        };
        var refusal = refusedUntil is { } until
            ? $"{player.FullName} broke off the talks with {club.Name} and refuses to negotiate until {until:yyyy-MM-dd}."
            : null;
        return new Talks(context, session, agent, relationship, rivals, refusal, refusedUntil);
    }

    /// <summary>
    /// Up to three other clubs that bid for the player: as reputable as him (at most 10 points less), able to pay his
    /// asking salary there within their wage budget, and for whom he is worth at least 75% of their average salary.
    /// </summary>
    private async Task<IReadOnlyList<CompetingOffer>> RivalsAsync(Data data, Player player, Club club, NegotiationContext context, IReadOnlyList<Contract> running, CancellationToken cancellationToken)
    {
        var wages = running.GroupBy(c => c.ClubId).ToDictionary(g => g.Key, g => (Bill: g.Sum(c => c.AnnualSalary), Count: g.Count()));
        var market = context with { CurrentSalary = null };
        return
        [
            .. (await data.ClubsAsync(cancellationToken).ConfigureAwait(false))
                .Where(c => c.Id != club.Id && c.Reputation >= player.Reputation - 10)
                .Select(c => (Club: c, Asking: _negotiator.AskingSalary(market with { ClubReputation = c.Reputation }), Wages: wages.GetValueOrDefault(c.Id)))
                .Where(x => x.Club.WageBudget - x.Wages.Bill >= x.Asking && (x.Wages.Count == 0 || x.Asking >= 0.75 * x.Wages.Bill / x.Wages.Count))
                .OrderByDescending(x => x.Club.Reputation)
                .ThenByDescending(x => x.Asking)
                .ThenBy(x => x.Club.Name, StringComparer.Ordinal)
                .Take(ContractNegotiator.MaxCompetingOffers)
                .Select(x => new CompetingOffer(x.Club.Id, x.Club.Name, x.Asking)),
        ];
    }

    private static async Task<AgentRelationship> RelationshipAsync(Data data, Agent agent, ClubId clubId, CancellationToken cancellationToken)
    {
        var repo = data.Repo<AgentRelationship, AgentRelationshipId>();
        var existing = (await repo.ListAsync(r => r.AgentId == agent.Id && r.ClubId == clubId, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        var created = new AgentRelationship(AgentRelationshipId.New(), agent.Id, clubId);
        repo.Add(created);
        return created;
    }

    private sealed record Talks(
        NegotiationContext Context,
        ContractNegotiation? Session,
        Agent? Agent,
        int Relationship,
        IReadOnlyList<CompetingOffer> Rivals,
        string? Refusal,
        DateOnly? RefusedUntil)
    {
        public int NextRound => (Session?.Rounds ?? 0) + 1;

        public int CurrentRound => Math.Max(1, Session?.Rounds ?? 0);

        public NegotiationTalks Describe(PlayerDemands demands, ContractNegotiation? session = null)
        {
            var s = session ?? Session;
            return new(
                Agent?.Name,
                Agent?.Toughness ?? 50,
                Relationship,
                s?.Rounds ?? 0,
                s?.Patience ?? demands.Patience,
                s?.BrokenOffUntil ?? RefusedUntil,
                Rivals);
        }
    }

    private static void Guard(TerminationKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new DomainValidationException($"Unknown termination kind '{kind}'.");
        }
    }

    /// <summary>Repositories and reads shared by the use cases, within one scope.</summary>
    private sealed class Data(IServiceProvider services, DateOnly today)
    {
        public DateOnly Today { get; } = today;

        public IServiceProvider Services => services;

        public IRepository<Contract, ContractId> Contracts => Repo<Contract, ContractId>();

        public IRepository<Player, PlayerId> Players => Repo<Player, PlayerId>();

        public IRepository<Club, ClubId> Clubs => Repo<Club, ClubId>();

        /// <summary>
        /// Keeps the shared negotiation reads (running contracts, clubs, agents, attractiveness, squads) for the rest of
        /// the unit of work: only for read-only batches (<see cref="GetNegotiationsAsync"/>), never around a change.
        /// </summary>
        public bool Memoize { get; init; }

        private IReadOnlyList<Contract>? _running;
        private IReadOnlyList<Club>? _clubs;
        private IReadOnlyList<Agent>? _agents;
        private readonly Dictionary<ClubId, int> _attractiveness = [];
        private readonly Dictionary<ClubId, IReadOnlyList<Player>> _squads = [];

        /// <summary>Player contracts running today, loans excluded (negotiation context).</summary>
        public async Task<IReadOnlyList<Contract>> RunningAsync(CancellationToken cancellationToken)
        {
            if (Memoize && _running is not null)
            {
                return _running;
            }

            IReadOnlyList<Contract> running = [.. (await Contracts.ListAsync(Contract.RunningOn(Today), cancellationToken).ConfigureAwait(false))
                .Where(c => c.PlayerId is not null && c.Status != ContractStatus.OnLoan)];
            return Memoize ? _running = running : running;
        }

        public async Task<IReadOnlyList<Club>> ClubsAsync(CancellationToken cancellationToken)
        {
            if (Memoize && _clubs is not null)
            {
                return _clubs;
            }

            var clubs = await Clubs.ListAsync(null, cancellationToken).ConfigureAwait(false);
            return Memoize ? _clubs = clubs : clubs;
        }

        /// <summary>The agents; the pool is created once, the first time anyone negotiates.</summary>
        public async Task<IReadOnlyList<Agent>> AgentsAsync(CancellationToken cancellationToken)
        {
            if (Memoize && _agents is not null)
            {
                return _agents;
            }

            var agents = await Repo<Agent, AgentId>().ListAsync(null, cancellationToken).ConfigureAwait(false);
            if (agents.Count == 0)
            {
                agents = AgentPool.Create();
                foreach (var created in agents)
                {
                    Repo<Agent, AgentId>().Add(created);
                }
            }

            return Memoize ? _agents = agents : agents;
        }

        public async Task<int> AttractivenessAsync(Club club, CancellationToken cancellationToken)
        {
            if (Memoize && _attractiveness.TryGetValue(club.Id, out var known))
            {
                return known;
            }

            var value = await ClubSeason.AttractivenessAsync(services, club, Today, cancellationToken).ConfigureAwait(false);
            if (Memoize)
            {
                _attractiveness[club.Id] = value;
            }

            return value;
        }

        /// <summary>The players under a running (non-loan) contract at the club.</summary>
        public async Task<IReadOnlyList<Player>> SquadAsync(ClubId clubId, IReadOnlyList<Contract> running, CancellationToken cancellationToken)
        {
            if (Memoize && _squads.TryGetValue(clubId, out var known))
            {
                return known;
            }

            List<PlayerId> ids = [.. running.Where(c => c.ClubId == clubId).Select(c => c.PlayerId!.Value).Distinct()];
            var squad = await Players.ListAsync(p => ids.Contains(p.Id), cancellationToken).ConfigureAwait(false);
            if (Memoize)
            {
                _squads[clubId] = squad;
            }

            return squad;
        }

        public IRepository<TEntity, TId> Repo<TEntity, TId>()
            where TEntity : Entity<TId>
            where TId : struct, IEntityId<TId> =>
            services.GetRequiredService<IRepository<TEntity, TId>>();

        public Task SaveAsync(CancellationToken cancellationToken) => services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);

        public ContractLine Line(Contract contract, string player, string club) => new(
            contract,
            player,
            club,
            Today,
            contract.StatusOn(Today),
            SalaryCalculator.RemainingSalary(contract, Today),
            contract.IsRunningOn(Today) || contract.IsUpcomingOn(Today) ? SalaryCalculator.NextPayment(contract, Today) : null);

        public async Task<(Player Player, Club Club)> PlayerAndClubAsync(PlayerId playerId, ClubId clubId, CancellationToken cancellationToken)
        {
            var player = await Players.FindAsync(playerId, cancellationToken).ConfigureAwait(false) ?? throw new DomainException("The player no longer exists in the database.");
            if (player.RetiredOn is { } retired)
            {
                throw new DomainValidationException($"{player.FullName} retired on {retired:yyyy-MM-dd}.");
            }

            return (player, await Clubs.FindAsync(clubId, cancellationToken).ConfigureAwait(false) ?? throw new DomainException("The club no longer exists in the database."));
        }

        public async Task<(Contract Contract, Player Player, Club Club)> PlayerContractAsync(ContractId contractId, CancellationToken cancellationToken)
        {
            var contract = await Contracts.FindAsync(contractId, cancellationToken).ConfigureAwait(false)
                ?? throw new DomainException("The contract no longer exists in the database.");
            if (contract.PlayerId is not { } playerId)
            {
                throw new DomainValidationException("This is a staff contract; staff are managed in a later version.");
            }

            var (player, club) = await PlayerAndClubAsync(playerId, contract.ClubId, cancellationToken).ConfigureAwait(false);
            return (contract, player, club);
        }
    }
}
