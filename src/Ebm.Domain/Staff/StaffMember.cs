using Ebm.Domain.Common;
using Ebm.Domain.Countries;

namespace Ebm.Domain.Staff;

/// <summary>A non-playing club employee (coach, scout, physio, …). Employment is recorded as a contract.</summary>
public sealed class StaffMember : Entity<StaffMemberId>
{
    public const int NameMaxLength = 60;
    public const int MinAbility = 1;
    public const int MaxAbility = 100;
    public const int DefaultAbility = 50;

    public StaffMember(StaffMemberId id, string firstName, string lastName, DateOnly dateOfBirth, CountryId nationalityId, StaffRole role, int ability = DefaultAbility)
        : base(id)
    {
        UpdateAbility(ability);
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

    /// <summary>How good he is at his job (1–100); for a scout, how accurate his reports are (docs/SCOUTING.md §5).</summary>
    public int Ability { get; private set; } = DefaultAbility;

    public string FullName => $"{FirstName} {LastName}";

    public void UpdateAbility(int ability) => Ability = Guard.Range(ability, MinAbility, MaxAbility, "Ability");
}
