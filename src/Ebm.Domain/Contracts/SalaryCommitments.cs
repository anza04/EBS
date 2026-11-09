namespace Ebm.Domain.Contracts;

/// <summary>Salary owed in one 12-month window from the calculation date.</summary>
/// <param name="Contracts">Number of contracts running at some point in the window.</param>
public sealed record CommitmentPeriod(DateOnly From, DateOnly To, long Salary, int Contracts);

/// <summary>
/// What a club has committed to pay its players from <see cref="Date"/> on (docs/CONTRACTS.md §3).
/// </summary>
/// <param name="WageBill">Annual salaries of the contracts running on <see cref="Date"/>.</param>
/// <param name="RemainingSalary">Salary still to be paid under running and upcoming contracts, to their ends.</param>
/// <param name="Periods">The remaining salary split into consecutive 12-month windows, the first starting on <see cref="Date"/>.</param>
/// <param name="MaxSeasonBonuses">Season performance bonuses the club would owe in one season if every condition were met (per-game bonuses excluded).</param>
public sealed record SalaryCommitments(
    DateOnly Date,
    long WageBill,
    long RemainingSalary,
    IReadOnlyList<CommitmentPeriod> Periods,
    long MaxSeasonBonuses,
    int RunningContracts,
    int UpcomingContracts)
{
    /// <summary>Salary owed in the first 12 months.</summary>
    public long NextTwelveMonths => Periods.Count > 0 ? Periods[0].Salary : 0;

    /// <summary>
    /// Totals a club's commitments from its contracts; contracts that no longer run after <paramref name="date"/> are
    /// ignored, and so are contracts of players out on loan (the loan club pays them, docs/TRANSFERS.md §4).
    /// </summary>
    public static SalaryCommitments Calculate(IEnumerable<Contract> contracts, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        var open = contracts.Where(c => c.Status != ContractStatus.OnLoan && (c.IsRunningOn(date) || c.IsUpcomingOn(date))).ToList();
        var running = open.Where(c => c.IsRunningOn(date)).ToList();
        var periods = new List<CommitmentPeriod>();
        if (open.Count > 0)
        {
            var last = open.Max(c => c.EffectiveEndDate);
            for (var from = date; from <= last; from = from.AddYears(1))
            {
                var to = from.AddYears(1).AddDays(-1);
                var inPeriod = open.Where(c => c.StartDate <= to && c.EffectiveEndDate >= from).ToList();
                periods.Add(new CommitmentPeriod(from, to, inPeriod.Sum(c => SalaryCalculator.SalaryBetween(c, from, to)), inPeriod.Count));
            }
        }

        return new SalaryCommitments(
            date,
            running.Sum(c => c.AnnualSalary),
            open.Sum(c => SalaryCalculator.RemainingSalary(c, date)),
            periods,
            open.Sum(c => c.PerformanceBonuses.SeasonTotal),
            running.Count,
            open.Count - running.Count);
    }
}
