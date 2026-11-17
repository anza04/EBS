using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Finances;
using Ebm.Domain.Seasons;

namespace Ebm.Services.Finances;

/// <summary>
/// Club finances (Step 23, docs/FINANCES.md): the board's season budgets, the ledger of every income and expense, and
/// financial reports. The career calendar books each day's money in the same transaction as the date change.
/// </summary>
public interface IFinanceService
{
    /// <summary>
    /// Sets the budget of every club without one for <paramref name="seasonId"/> and copies its wage budget to the club.
    /// Idempotent. <paramref name="objectivesMet"/> holds the previous season's sporting verdicts (renewed sponsorship deals
    /// follow them). Returns the number of budgets set.
    /// </summary>
    Task<int> PlanBudgetsAsync(SeasonId seasonId, IReadOnlyDictionary<ClubId, bool>? objectivesMet = null, CancellationToken cancellationToken = default);

    /// <summary>The club's financial report for the financial year holding the game date; <c>null</c> for an unknown club.</summary>
    Task<ClubFinanceReport?> GetReportAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>The club's ledger between two dates (inclusive), newest first.</summary>
    Task<IReadOnlyList<FinancialTransaction>> GetTransactionsAsync(ClubId clubId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>A category's total in a report.</summary>
public sealed record FinanceLine(FinanceCategory Category, long Amount);

/// <summary>
/// A club's finances for one financial year (docs/FINANCES.md §4): actual income and expenditure by category, the budget,
/// salary commitments, the transfer budget left and the board's verdict on the financial target.
/// </summary>
/// <param name="Budget">The board's budget for the year; <c>null</c> when none has been set.</param>
/// <param name="OnTarget">Whether the balance meets the board's financial target now (<c>null</c> without a budget).</param>
/// <param name="TransferBudgetLeft">Transfer budget left this year; <c>null</c> without a budget (only the balance limits).</param>
public sealed record ClubFinanceReport(
    Club Club,
    Season? Season,
    DateOnly From,
    DateOnly To,
    ClubBudget? Budget,
    IReadOnlyList<FinanceLine> Income,
    IReadOnlyList<FinanceLine> Expenditure,
    SalaryCommitments PlayerCommitments,
    long StaffWageBill,
    long? TransferBudgetLeft,
    bool? OnTarget,
    IReadOnlyList<FinancialTransaction> Recent)
{
    public long TotalIncome => Income.Sum(l => l.Amount);

    /// <summary>Total expenditure, as a positive amount.</summary>
    public long TotalExpenditure => -Expenditure.Sum(l => l.Amount);

    public long Result => TotalIncome - TotalExpenditure;
}

/// <summary>Money booked on one game day, over all clubs.</summary>
/// <param name="Costs">Running costs (arena, travel), as a positive amount.</param>
public sealed record FinanceDay(long Salaries, long Income, long Costs);
