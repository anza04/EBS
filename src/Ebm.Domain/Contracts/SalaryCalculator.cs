namespace Ebm.Domain.Contracts;

/// <summary>One salary instalment: the pay for <see cref="PeriodStart"/>–<see cref="PeriodEnd"/>, due on the last day of the period.</summary>
public sealed record SalaryPayment(DateOnly PeriodStart, DateOnly PeriodEnd, long Amount)
{
    public DateOnly DueDate => PeriodEnd;
}

/// <summary>
/// Salary arithmetic of a single contract (docs/CONTRACTS.md §3). Pure and deterministic.
/// </summary>
/// <remarks>
/// Salary accrues day by day within each contract year (a year counted from the start date's anniversary), so a full
/// contract year always costs exactly the annual salary, whatever its number of days. Amounts are rounded to whole
/// euros only at the end, and instalments are differences of rounded running totals, so they add up to the total.
/// </remarks>
public static class SalaryCalculator
{
    /// <summary>Salary earned on the days from <paramref name="from"/> to <paramref name="to"/> (inclusive) on which the contract runs.</summary>
    public static long SalaryBetween(Contract contract, DateOnly from, DateOnly to) =>
        Round(Accrued(contract, from, to));

    /// <summary>Salary still to be earned from <paramref name="date"/> (inclusive) to the effective end of the contract.</summary>
    public static long RemainingSalary(Contract contract, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return SalaryBetween(contract, date, contract.EffectiveEndDate);
    }

    /// <summary>Salary for the whole period the contract runs (to its early end, if any).</summary>
    public static long TotalSalary(Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return SalaryBetween(contract, contract.StartDate, contract.EffectiveEndDate);
    }

    /// <summary>Every instalment of the contract, in date order, following its <see cref="PaymentFrequency"/>.</summary>
    public static IReadOnlyList<SalaryPayment> PaymentSchedule(Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var months = MonthsPerInstalment(contract.PaymentFrequency);
        var end = contract.EffectiveEndDate;
        var payments = new List<SalaryPayment>();
        var paid = 0L;
        for (var k = 0; ; k++)
        {
            var periodStart = contract.StartDate.AddMonths(k * months);
            if (periodStart > end)
            {
                break;
            }

            var periodEnd = Min(contract.StartDate.AddMonths((k + 1) * months).AddDays(-1), end);
            var cumulative = SalaryBetween(contract, contract.StartDate, periodEnd);
            payments.Add(new SalaryPayment(periodStart, periodEnd, cumulative - paid));
            paid = cumulative;
        }

        return payments;
    }

    /// <summary>The first instalment due on or after <paramref name="date"/>, or <c>null</c> when none is left.</summary>
    public static SalaryPayment? NextPayment(Contract contract, DateOnly date) =>
        PaymentSchedule(contract).FirstOrDefault(p => p.DueDate >= date);

    public static int MonthsPerInstalment(PaymentFrequency frequency) => frequency switch
    {
        PaymentFrequency.Monthly => 1,
        PaymentFrequency.Quarterly => 3,
        PaymentFrequency.Annually => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(frequency), frequency, "Unknown payment frequency."),
    };

    /// <summary>Unrounded salary for the running days within [from, to].</summary>
    internal static decimal Accrued(Contract contract, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var start = Max(from, contract.StartDate);
        var end = Min(to, contract.EffectiveEndDate);
        if (end < start)
        {
            return 0m;
        }

        var total = 0m;
        for (var year = 0; ; year++)
        {
            var yearStart = contract.StartDate.AddYears(year);
            if (yearStart > end)
            {
                break;
            }

            var nextYear = contract.StartDate.AddYears(year + 1);
            var overlapStart = Max(start, yearStart);
            var overlapEnd = Min(end, nextYear.AddDays(-1));
            if (overlapEnd >= overlapStart)
            {
                var yearDays = nextYear.DayNumber - yearStart.DayNumber;
                total += contract.AnnualSalary * (decimal)(overlapEnd.DayNumber - overlapStart.DayNumber + 1) / yearDays;
            }
        }

        return total;
    }

    private static long Round(decimal value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
