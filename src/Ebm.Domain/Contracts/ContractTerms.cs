namespace Ebm.Domain.Contracts;

/// <summary>
/// The agreed terms of a contract: what is negotiated and what the editor and the importer set.
/// Money is in whole euros. Validated by <see cref="Contract"/> (docs/CONTRACTS.md §1).
/// </summary>
/// <param name="ClubOptionUntil">If set, the club may extend the contract to this date before it ends.</param>
/// <param name="PlayerOptionUntil">If set, the player may extend the contract to this date before it ends.</param>
/// <param name="ReleaseClause">Fee for which another club can buy the player out (used by transfers, Step 08).</param>
public sealed record ContractTerms(
    DateOnly StartDate,
    DateOnly EndDate,
    long AnnualSalary,
    PaymentFrequency PaymentFrequency = PaymentFrequency.Monthly,
    long SigningBonus = 0,
    PerformanceBonuses? Bonuses = null,
    long? ReleaseClause = null,
    DateOnly? ClubOptionUntil = null,
    DateOnly? PlayerOptionUntil = null)
{
    /// <summary>The performance bonuses; never <c>null</c>.</summary>
    public PerformanceBonuses PerformanceBonuses => Bonuses ?? PerformanceBonuses.None;

    /// <summary>Length in years from start to end (365.25 days a year).</summary>
    public double Years => (EndDate.DayNumber - StartDate.DayNumber + 1) / 365.25;
}
