using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;
using Ebm.Domain.Transfers;
using Ebm.Services.Contracts;

namespace Ebm.Services.Transfers;

/// <summary>
/// The player market (docs/TRANSFERS.md): search, valuations, transfer and loan negotiations, transfers, loans and
/// loan returns, the transfer history and the AI clubs' transfer window. One DI scope per call; "today" comes from
/// the shared <see cref="TimeProvider"/>. Free agents are signed through <see cref="IContractService.SignAsync"/>.
/// </summary>
/// <remarks>Writes throw <see cref="Ebm.Domain.Common.DomainException"/> with a player-facing message.</remarks>
public interface ITransferService
{
    /// <summary>Players matching <paramref name="search"/>, best overall first, priced for <see cref="PlayerSearch.ForClub"/>.</summary>
    Task<IReadOnlyList<MarketPlayer>> SearchAsync(PlayerSearch search, CancellationToken cancellationToken = default);

    /// <summary>One player as the market sees him, priced for <paramref name="forClub"/>; <c>null</c> when he does not exist.</summary>
    Task<MarketPlayer?> GetMarketPlayerAsync(PlayerId playerId, ClubId? forClub = null, CancellationToken cancellationToken = default);

    /// <summary>The selling club's answer to a transfer fee for a player under contract. Nothing is saved.</summary>
    Task<TransferResponse> NegotiateFeeAsync(PlayerId playerId, ClubId buyerId, long fee, CancellationToken cancellationToken = default);

    /// <summary>The parent club's answer to a loan until <paramref name="until"/> for <paramref name="fee"/>. Nothing is saved.</summary>
    Task<TransferResponse> NegotiateLoanAsync(PlayerId playerId, ClubId borrowerId, DateOnly until, long fee, CancellationToken cancellationToken = default);

    /// <summary>The player's answer to the contract he would sign with <paramref name="buyerId"/> after a transfer. Nothing is saved.</summary>
    Task<NegotiationResponse> NegotiateContractAsync(PlayerId playerId, ClubId buyerId, ContractTerms terms, CancellationToken cancellationToken = default);

    /// <summary>
    /// Buys a player under contract: the selling club must accept the fee (or the fee meets his release clause), the
    /// player must accept <paramref name="terms"/> (starting today), the buyer must be able to pay the fee and the
    /// signing bonus and have room in its first team. The old contract ends as transferred, the fee moves between the
    /// club balances, the new contract starts and the player joins the squad in <paramref name="joinAs"/>.
    /// </summary>
    /// <param name="enforceWageBudget">Refuse (instead of warn) when the new salary takes the wage bill over the wage budget. AI clubs enforce it.</param>
    Task<TransferReceipt> BuyAsync(PlayerId playerId, ClubId buyerId, long fee, ContractTerms terms, RosterRole joinAs = RosterRole.Bench, bool enforceWageBudget = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a player on loan until <paramref name="until"/> (his last day at the loan club): the parent club must
    /// accept the fee, the borrower pays it and the player's salary meanwhile through a loan contract on the same
    /// terms. The player joins the borrower's squad and goes back when the loan ends (<see cref="ProcessTransferDatesAsync"/>) or is recalled.
    /// </summary>
    Task<TransferReceipt> LoanAsync(PlayerId playerId, ClubId borrowerId, DateOnly until, long fee, RosterRole joinAs = RosterRole.Bench, bool enforceWageBudget = false, CancellationToken cancellationToken = default);

    /// <summary>The parent club recalls a loaned player today: the loan contract ends, he rejoins his parent club.</summary>
    Task<TransferReceipt> RecallAsync(TransferId loanId, CancellationToken cancellationToken = default);

    /// <summary>The club's transfer history and its open loans, or <c>null</c> when the club does not exist.</summary>
    Task<ClubTransfers?> GetClubTransfersAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>Every move of one player, newest first.</summary>
    Task<IReadOnlyList<TransferLine>> GetPlayerTransfersAsync(PlayerId playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the calendar up to today: loans whose end date has passed are returned, then contract dates are applied
    /// (<see cref="IContractService.ProcessContractDatesAsync"/>). Idempotent; run at startup and, from Step 12, every game day.
    /// </summary>
    Task<TransferDateChanges> ProcessTransferDatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every club not in <paramref name="managedClubs"/> runs its transfer window with the simple AI rules
    /// (docs/TRANSFERS.md §6): sells surplus players to other AI clubs, signs free agents and buys players to fill its
    /// roster gaps within its balance, wage budget and registration limits. AI clubs never buy from managed clubs.
    /// </summary>
    Task<TransferWindowReport> RunAiTransferWindowAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default);
}

/// <summary>Which players a search returns.</summary>
public enum MarketFilter
{
    Everyone,
    FreeAgents,
    UnderContract,

    /// <summary>Players under contract whose club would loan them out.</summary>
    AvailableForLoan,
}

/// <summary>Search criteria; every field is optional. <paramref name="ForClub"/> excludes that club's own players and prices salaries for it.</summary>
/// <param name="Position">Primary or secondary position.</param>
/// <param name="MaxFee">Highest fee that completes a transfer (release clause or asking fee); free agents always pass.</param>
/// <param name="MaxSalary">Highest annual salary the player would ask the searching club.</param>
public sealed record PlayerSearch(
    string? Name = null,
    Position? Position = null,
    int? MinAge = null,
    int? MaxAge = null,
    int? MinOverall = null,
    int? MaxOverall = null,
    CountryId? NationalityId = null,
    MarketFilter Filter = MarketFilter.Everyone,
    long? MaxFee = null,
    long? MaxSalary = null,
    ClubId? ForClub = null,
    int MaxResults = 200)
{
    public static PlayerSearch Everyone { get; } = new();
}

/// <summary>Contract situation of a player on the market.</summary>
public enum MarketStatus
{
    /// <summary>No running and no upcoming contract: any club may sign him.</summary>
    FreeAgent,

    /// <summary>Under contract (or signed in advance): he can be bought out or, if his club agrees, loaned.</summary>
    UnderContract,

    /// <summary>Out on loan: he must be recalled before he can move again.</summary>
    OnLoan,
}

/// <summary>A player as the market screen shows him, on <paramref name="Date"/>.</summary>
/// <param name="Club">His current club (the loan club while on loan); <c>null</c> for a free agent without a club.</param>
/// <param name="Contract">The contract that binds him: the running one, else the upcoming one; <c>null</c> for a free agent. On loan: the parent contract.</param>
/// <param name="ParentClub">While on loan, the club he belongs to.</param>
/// <param name="Role">His squad role at his club, when known.</param>
/// <param name="Valuation">Market value and what his club asks.</param>
/// <param name="AskingSalary">Annual salary he would ask the searching club (an average club when none).</param>
public sealed record MarketPlayer(
    Player Player,
    DateOnly Date,
    int Age,
    PlayerEvaluation Evaluation,
    string Nationality,
    Club? Club,
    Contract? Contract,
    Club? ParentClub,
    RosterRole? Role,
    MarketStatus Status,
    PlayerValuation Valuation,
    long AskingSalary)
{
    public int Overall => Evaluation.Overall;

    public bool IsFreeAgent => Status == MarketStatus.FreeAgent;

    /// <summary>Last day of the binding contract, if any.</summary>
    public DateOnly? ContractEnd => Contract?.EffectiveEndDate;

    public long Salary => Contract?.AnnualSalary ?? 0;

    public long? ReleaseClause => Contract?.ReleaseClause;
}

/// <summary>What a completed transfer, loan or recall did.</summary>
/// <param name="Warnings">Limits the move strained without blocking it (wage budget, matchday squad size).</param>
public sealed record TransferReceipt(TransferId TransferId, TransferKind Kind, long Fee, ContractId? ContractId, IReadOnlyList<string> Warnings, string Message);

/// <summary>A transfer as the screens show it.</summary>
public sealed record TransferLine(Transfer Transfer, string Player, string? FromClub, string ToClub)
{
    public DateOnly Date => Transfer.Date;

    public TransferKind Kind => Transfer.Kind;

    public long Fee => Transfer.Fee;

    /// <summary>"Free agent", "Transfer", "Release clause", "Loan until yyyy-MM-dd" or "Loan return".</summary>
    public string KindText => Transfer.Kind switch
    {
        TransferKind.FreeAgentSigning => "Free agent",
        TransferKind.Loan => $"Loan until {Transfer.LoanEndDate:yyyy-MM-dd}" + (Transfer.ReturnedOn is { } back ? $" (back {back:yyyy-MM-dd})" : string.Empty),
        TransferKind.LoanReturn => "Loan return",
        TransferKind.ReleaseClause => "Release clause",
        _ => "Transfer",
    };
}

/// <param name="History">Every transfer in or out of the club, newest first.</param>
/// <param name="LoansOut">Open loans of the club's players at other clubs.</param>
/// <param name="LoansIn">Open loans of other clubs' players at the club.</param>
public sealed record ClubTransfers(
    Club Club,
    DateOnly Date,
    IReadOnlyList<TransferLine> History,
    IReadOnlyList<TransferLine> LoansOut,
    IReadOnlyList<TransferLine> LoansIn);

/// <summary>What <see cref="ITransferService.ProcessTransferDatesAsync"/> changed.</summary>
public sealed record TransferDateChanges(int LoansReturned, IReadOnlyList<PlayerId> ReturnedPlayers, ContractDateChanges Contracts)
{
    public static TransferDateChanges None { get; } = new(0, [], ContractDateChanges.None);
}

/// <summary>One AI club's transfer window: what it needed, what it did.</summary>
public sealed record ClubWindowReport(Club Club, IReadOnlyList<string> Needs, IReadOnlyList<string> Actions, int Transactions);

public sealed record TransferWindowReport(DateOnly Date, IReadOnlyList<ClubWindowReport> Clubs)
{
    public int Transactions => Clubs.Sum(c => c.Transactions);
}
