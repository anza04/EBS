using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;
using Ebm.Domain.Staff;

namespace Ebm.Domain.Contracts;

/// <summary>
/// An employment contract between a club and exactly one person: a player or a staff member.
/// Old contracts are kept as history.
/// </summary>
public sealed class Contract : Entity<ContractId>
{
    /// <summary>Upper bound for an annual salary in euros, to catch data-entry errors.</summary>
    public const long MaxAnnualSalary = 100_000_000;

    private Contract(ContractId id, ClubId clubId, PlayerId? playerId, StaffMemberId? staffMemberId, DateOnly startDate, DateOnly endDate, long annualSalary)
        : base(id)
    {
        ClubId = Guard.Required(clubId, nameof(clubId));
        PlayerId = Guard.Optional(playerId, nameof(playerId));
        StaffMemberId = Guard.Optional(staffMemberId, nameof(staffMemberId));
        UpdateTerms(startDate, endDate, annualSalary);
    }

    private Contract()
    {
    }

    public ClubId ClubId { get; private set; }

    /// <summary>Set for player contracts; exactly one of <see cref="PlayerId"/> and <see cref="StaffMemberId"/> is set.</summary>
    public PlayerId? PlayerId { get; private set; }

    /// <summary>Set for staff contracts; exactly one of <see cref="PlayerId"/> and <see cref="StaffMemberId"/> is set.</summary>
    public StaffMemberId? StaffMemberId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    /// <summary>Gross annual salary in whole euros.</summary>
    public long AnnualSalary { get; private set; }

    /// <summary>Changes dates and salary. Club and person never change: a new deal is a new contract.</summary>
    public void UpdateTerms(DateOnly startDate, DateOnly endDate, long annualSalary)
    {
        if (annualSalary is < 0 or > MaxAnnualSalary)
        {
            throw new DomainValidationException($"Annual salary must be between 0 and {MaxAnnualSalary:N0} euros.");
        }

        EndDate = Guard.After(endDate, startDate, "Contract end date");
        StartDate = startDate;
        AnnualSalary = annualSalary;
    }

    public static Contract ForPlayer(ContractId id, ClubId clubId, PlayerId playerId, DateOnly startDate, DateOnly endDate, long annualSalary) =>
        new(id, clubId, Guard.Required(playerId, nameof(playerId)), null, startDate, endDate, annualSalary);

    public static Contract ForStaff(ContractId id, ClubId clubId, StaffMemberId staffMemberId, DateOnly startDate, DateOnly endDate, long annualSalary) =>
        new(id, clubId, null, Guard.Required(staffMemberId, nameof(staffMemberId)), startDate, endDate, annualSalary);
}
