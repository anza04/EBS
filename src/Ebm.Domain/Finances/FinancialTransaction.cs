using Ebm.Domain.Clubs;
using Ebm.Domain.Common;

namespace Ebm.Domain.Finances;

/// <summary>What a club's income or expenditure is for (docs/FINANCES.md §2).</summary>
public enum FinanceCategory
{
    PlayerSalaries,
    StaffSalaries,
    SigningBonuses,

    /// <summary>Contract settlements and buyouts.</summary>
    Settlements,

    /// <summary>Transfer and option-to-buy fees, received or paid.</summary>
    TransferFees,

    /// <summary>Loan fees and loan salary contributions (and their refunds).</summary>
    LoanFees,
    Sponsorship,
    TicketSales,
    PrizeMoney,

    /// <summary>Arena upkeep (monthly) and matchday operations.</summary>
    ArenaCosts,
    TravelCosts,
}

public readonly record struct FinancialTransactionId(Guid Value) : IEntityId<FinancialTransactionId>
{
    public static FinancialTransactionId New() => new(Guid.CreateVersion7());

    public static FinancialTransactionId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// One booking in a club's ledger: income (positive) or expenditure (negative). Every game-driven change of
/// <see cref="Club.Balance"/> writes one (docs/FINANCES.md §1). History, never deleted. Daily salaries are one row per
/// club, category and day.
/// </summary>
public sealed class FinancialTransaction : Entity<FinancialTransactionId>
{
    public const int DescriptionMaxLength = 200;

    public FinancialTransaction(FinancialTransactionId id, ClubId clubId, DateOnly date, FinanceCategory category, long amount, string? description = null)
        : base(id)
    {
        if (amount == 0)
        {
            throw new DomainValidationException("A financial transaction needs an amount.");
        }

        ClubId = Guard.Required(clubId, nameof(clubId));
        Date = date;
        Category = Guard.Defined(category, "Finance category");
        Amount = amount;
        Description = string.IsNullOrWhiteSpace(description) ? null : Guard.Text(description.Length > DescriptionMaxLength ? description[..DescriptionMaxLength] : description, "Description", DescriptionMaxLength);
    }

    private FinancialTransaction()
    {
    }

    public ClubId ClubId { get; private set; }

    public DateOnly Date { get; private set; }

    public FinanceCategory Category { get; private set; }

    /// <summary>Whole euros: positive = income, negative = expenditure.</summary>
    public long Amount { get; private set; }

    public string? Description { get; private set; }
}
