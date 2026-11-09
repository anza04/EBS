using Ebm.Domain.Common;
using Ebm.Domain.Countries;

namespace Ebm.Domain.Staff;

/// <summary>A non-playing club employee (coach, scout, physio, …). Employment is recorded as a contract.</summary>
public sealed class StaffMember : Entity<StaffMemberId>
{
    public const int NameMaxLength = 60;

    public StaffMember(StaffMemberId id, string firstName, string lastName, DateOnly dateOfBirth, CountryId nationalityId, StaffRole role)
        : base(id)
    {
        FirstName = Guard.Text(firstName, "First name", NameMaxLength);
        LastName = Guard.Text(lastName, "Last name", NameMaxLength);
        DateOfBirth = dateOfBirth;
        NationalityId = Guard.Required(nationalityId, nameof(nationalityId));
        Role = Guard.Defined(role, "Staff role");
    }

    private StaffMember()
    {
    }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public DateOnly DateOfBirth { get; private set; }

    public CountryId NationalityId { get; private set; }

    public StaffRole Role { get; private set; }
}
