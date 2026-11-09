using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;
using Ebm.Services.Clubs;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Contracts;

/// <summary>Implements <see cref="IContractService"/> over the repositories, the <see cref="ContractNegotiator"/> and the salary calculator.</summary>
internal sealed class ContractService : IContractService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly PlayerRatingCalculator _calculator;
    private readonly ContractNegotiator _negotiator;
    private readonly TimeProvider _time;

    public ContractService(IServiceScopeFactory scopes, PlayerRatingCalculator calculator, ContractNegotiator negotiator, TimeProvider time)
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

    private DateOnly Today => DateOnly.FromDateTime(_time.GetLocalNow().DateTime);

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
        var players = await data.Players.ListAsync(null, cancellationToken).ConfigureAwait(false);
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
        return _negotiator.Evaluate(Context(player, club, data.Today, null), offer);
    }

    public async Task<NegotiationResponse> NegotiateRenewalAsync(ContractId contractId, ContractTerms offer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(offer);
        using var scope = _scopes.CreateScope();
        var data = new Data(scope.ServiceProvider, Today);
        var (contract, player, club) = await data.PlayerContractAsync(contractId, cancellationToken).ConfigureAwait(false);
        CheckRenewable(contract, offer, data.Today);
        return _negotiator.Evaluate(Context(player, club, data.Today, contract.AnnualSalary), offer);
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
        RequireAcceptance(_negotiator.Evaluate(Context(player, club, data.Today, null), terms));

        var contract = Contract.ForPlayer(ContractId.New(), clubId, playerId, terms);
        data.Contracts.Add(contract);
        club.ChangeBalance(-terms.SigningBonus);
        if (contract.IsRunningOn(data.Today)
            && (await ClubMembership.CurrentClubsAsync(scope.ServiceProvider, data.Today, cancellationToken).ConfigureAwait(false)).GetValueOrDefault(playerId) != clubId)
        {
            await ClubMembership.JoinAsync(scope.ServiceProvider, playerId, clubId, data.Today, joinAs, cancellationToken).ConfigureAwait(false);
        }

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
        RequireAcceptance(_negotiator.Evaluate(Context(player, club, data.Today, contract.AnnualSalary), terms));

        contract.Supersede(terms.StartDate);
        var renewal = Contract.ForPlayer(ContractId.New(), contract.ClubId, player.Id, terms);
        data.Contracts.Add(renewal);
        club.ChangeBalance(-terms.SigningBonus);
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
            throw new DomainValidationException($"The contract is {contract.StatusOn(data.Today).ToString().ToLowerInvariant()}; only an active contract can be terminated.");
        }

        var remaining = SalaryCalculator.RemainingSalary(contract, data.Today);
        var paid = kind == TerminationKind.ClubRelease ? remaining : settlement ?? remaining / 2;
        if (paid < 0 || paid > remaining)
        {
            throw new DomainValidationException($"The settlement must be between 0 and the remaining salary (€ {remaining:N0}).");
        }

        contract.Terminate(data.Today, paid);
        club.ChangeBalance(-paid);

        // The player stays only if another contract with the club runs today (e.g. the one this renewal would replace).
        var others = await data.Contracts.ListAsync(c => c.PlayerId == player.Id && c.ClubId == club.Id && c.Id != contract.Id, cancellationToken).ConfigureAwait(false);
        if (!others.Any(c => c.IsRunningOn(data.Today)))
        {
            await ClubMembership.LeaveAsync(scope.ServiceProvider, player.Id, club.Id, data.Today, cancellationToken).ConfigureAwait(false);
        }

        await data.SaveAsync(cancellationToken).ConfigureAwait(false);
        return paid;
    }

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
        var exercise = _negotiator.ExercisesPlayerOption(Context(player, club, data.Today, null), contract.AnnualSalary);
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
            var history = await data.Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>().ListAsync(null, cancellationToken).ConfigureAwait(false);
            foreach (var contract in running.OrderBy(c => c.StartDate))
            {
                var playerId = contract.PlayerId!.Value;
                var spells = history.Where(s => s.PlayerId == playerId).ToList();
                var open = spells.FirstOrDefault(s => s.IsCurrent);

                // Players whose club history is not tracked (imported contracts only) keep belonging through the contract.
                var moves = open is null ? spells.Count > 0 : open.ClubId != contract.ClubId && open.StartDate < contract.StartDate;
                if (moves)
                {
                    await ClubMembership.JoinAsync(scope.ServiceProvider, playerId, contract.ClubId, contract.StartDate, RosterRole.Bench, cancellationToken).ConfigureAwait(false);
                    history = [.. history, .. await ClubMembership.OpenSpellsAsync(scope.ServiceProvider, playerId, cancellationToken).ConfigureAwait(false)];
                    started++;
                }
            }

            await data.SaveAsync(cancellationToken).ConfigureAwait(false);
            var bound = running.Select(c => c.PlayerId!.Value).ToHashSet();
            freeAgents.AddRange(leavers.Distinct().Where(p => !bound.Contains(p)));
        }

        return expired == 0 && started == 0 ? ContractDateChanges.None : new ContractDateChanges(expired, started, freeAgents);
    }

    private NegotiationContext Context(Player player, Club club, DateOnly today, long? currentSalary)
    {
        var evaluation = _calculator.Evaluate(player);
        return new NegotiationContext(evaluation.Overall, evaluation.PotentialRating, player.Reputation, player.AgeOn(today), club.Reputation, currentSalary);
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
                $"The contract is {contract.StatusOn(today).ToString().ToLowerInvariant()}; only an active contract can be renewed. Offer a new contract instead.");
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

    private static void RequireAcceptance(NegotiationResponse response)
    {
        if (!response.IsAccepted)
        {
            throw new DomainException(response.Message);
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

        public IRepository<Contract, ContractId> Contracts => Repo<Contract, ContractId>();

        public IRepository<Player, PlayerId> Players => Repo<Player, PlayerId>();

        public IRepository<Club, ClubId> Clubs => Repo<Club, ClubId>();

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

        public async Task<(Player Player, Club Club)> PlayerAndClubAsync(PlayerId playerId, ClubId clubId, CancellationToken cancellationToken) => (
            await Players.FindAsync(playerId, cancellationToken).ConfigureAwait(false) ?? throw new DomainException("The player no longer exists in the database."),
            await Clubs.FindAsync(clubId, cancellationToken).ConfigureAwait(false) ?? throw new DomainException("The club no longer exists in the database."));

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
