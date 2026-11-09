using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;

namespace Ebm.Services.Contracts;

/// <summary>
/// The contract system (docs/CONTRACTS.md): signing, renewal, termination, options, expiry, free agency and salary
/// commitments. One DI scope per call; "today" comes from the shared <see cref="TimeProvider"/>.
/// </summary>
/// <remarks>Writes throw <see cref="Ebm.Domain.Common.DomainException"/> with a player-facing message.</remarks>
public interface IContractService
{
    /// <summary>The club's current and past player contracts and its salary commitments, or <c>null</c> when the club does not exist.</summary>
    Task<ClubContracts?> GetClubContractsAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>The player's contract history and free-agent status, or <c>null</c> when the player does not exist.</summary>
    Task<PlayerContracts?> GetPlayerContractsAsync(PlayerId playerId, CancellationToken cancellationToken = default);

    /// <summary>Players without a running or upcoming contract, best first.</summary>
    Task<IReadOnlyList<FreeAgent>> GetFreeAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>The player's answer to a new contract offer from <paramref name="clubId"/>. Nothing is saved.</summary>
    Task<NegotiationResponse> NegotiateAsync(PlayerId playerId, ClubId clubId, ContractTerms offer, CancellationToken cancellationToken = default);

    /// <summary>The player's answer to a renewal offer for <paramref name="contractId"/>. Nothing is saved.</summary>
    Task<NegotiationResponse> NegotiateRenewalAsync(ContractId contractId, ContractTerms offer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs a player who is free from the contract's start date. The player must accept the terms; the club pays the
    /// signing bonus now. When the contract starts today or earlier the player joins the club in <paramref name="joinAs"/>
    /// (bench, inactive or youth); otherwise he joins on the start date (<see cref="ProcessContractDatesAsync"/>).
    /// </summary>
    Task<ContractId> SignAsync(PlayerId playerId, ClubId clubId, ContractTerms terms, RosterRole joinAs = RosterRole.Bench, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an active contract with a new one at the same club, starting no later than the day after it ends.
    /// The player must accept; the old contract is kept as history with status <see cref="ContractStatus.Renewed"/>.
    /// </summary>
    Task<ContractId> RenewAsync(ContractId contractId, ContractTerms terms, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends a player contract from today and pays the settlement: the remaining salary for a release, or
    /// <paramref name="settlement"/> (default half the remaining salary) for a mutual agreement. The player leaves the
    /// club and becomes a free agent. Returns the settlement paid.
    /// </summary>
    Task<long> TerminateAsync(ContractId contractId, TerminationKind kind, long? settlement = null, CancellationToken cancellationToken = default);

    /// <summary>The club takes up (extends to the option date) or declines its option.</summary>
    Task DecideClubOptionAsync(ContractId contractId, bool exercise, CancellationToken cancellationToken = default);

    /// <summary>The player decides on his option (simplified rule, docs/CONTRACTS.md §4). Returns whether he took it up.</summary>
    Task<bool> DecidePlayerOptionAsync(ContractId contractId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies contract dates up to today: active contracts past their end date expire (the player leaves the club and
    /// becomes a free agent unless he has another contract there), and contracts signed in advance move the player to
    /// his new club once they start. Idempotent; run at startup and, from Step 12, on every game day.
    /// </summary>
    Task<ContractDateChanges> ProcessContractDatesAsync(CancellationToken cancellationToken = default);
}

/// <summary>A contract as the screens show it, on <see cref="Date"/>.</summary>
/// <param name="Status">Status on the date (an active contract past its end date shows as expired).</param>
/// <param name="RemainingSalary">Salary still to be paid from the date to the effective end.</param>
public sealed record ContractLine(
    Contract Contract,
    string Player,
    string Club,
    DateOnly Date,
    ContractStatus Status,
    long RemainingSalary,
    SalaryPayment? NextPayment)
{
    public bool IsRunning => Contract.IsRunningOn(Date);

    public bool IsUpcoming => Contract.IsUpcomingOn(Date);

    /// <summary>"Running", "Starts yyyy-MM-dd", "On loan", "Expired", "Terminated", "Transferred" or "Renewed".</summary>
    public string StatusText => IsUpcoming ? $"Starts {Contract.StartDate:yyyy-MM-dd}" : IsRunning && Status == ContractStatus.Active ? "Running" : PlayerCatalog.DisplayName(Status);
}

/// <param name="Current">Running and upcoming contracts.</param>
/// <param name="History">Every other contract (expired, terminated, renewed), newest first.</param>
public sealed record ClubContracts(
    Club Club,
    DateOnly Date,
    IReadOnlyList<ContractLine> Current,
    IReadOnlyList<ContractLine> History,
    SalaryCommitments Commitments)
{
    /// <summary>Wage budget minus the wage bill; negative when over budget.</summary>
    public long WageBudgetLeft => Club.WageBudget - Commitments.WageBill;
}

/// <param name="CurrentClub">Club the player belongs to (open spell, else running contract); <c>null</c> when he has none.</param>
/// <param name="Contracts">Every contract of the player, newest first.</param>
/// <param name="IsFreeAgent">No running and no upcoming contract: any club may sign him.</param>
public sealed record PlayerContracts(
    Player Player,
    DateOnly Date,
    string? CurrentClub,
    ContractLine? Running,
    ContractLine? Upcoming,
    IReadOnlyList<ContractLine> Contracts,
    bool IsFreeAgent);

/// <param name="LastClub">Club of his most recent contract, if any.</param>
public sealed record FreeAgent(Player Player, int Age, int Overall, string Nationality, string? LastClub);

/// <summary>What <see cref="IContractService.ProcessContractDatesAsync"/> changed.</summary>
public sealed record ContractDateChanges(int Expired, int Started, IReadOnlyList<PlayerId> NewFreeAgents)
{
    public static ContractDateChanges None { get; } = new(0, 0, []);
}
