using Ebm.Domain.Common;
using Ebm.Domain.Countries;
using Ebm.Domain.Tactics;

namespace Ebm.Domain.Staff;

/// <summary>
/// Staff attributes (Step 28, docs/STAFF.md §2), each 1–100 except <paramref name="Experience"/> (years in the job, 0–50).
/// </summary>
public sealed record StaffProfile(
    int TacticalKnowledge,
    int Development,
    int Motivation,
    int Scouting,
    int MedicalExpertise,
    int Reputation,
    int Experience,
    CoachingStyle Style = CoachingStyle.Balanced,
    OffensiveSystem? PreferredOffense = null,
    DefensiveSystem? PreferredDefense = null)
{
    public const int MaxExperience = 50;

    /// <summary>Every attribute and the reputation at <paramref name="value"/>, no experience: <see cref="StaffRules.RoleAbility"/> is then exactly <paramref name="value"/>.</summary>
    public static StaffProfile Uniform(int value) => new(value, value, value, value, value, value, 0);
}

/// <summary>A non-playing club employee (coach, scout, physio, …). Employment is recorded as a contract.</summary>
public sealed class StaffMember : Entity<StaffMemberId>
{
    public const int NameMaxLength = 60;
    public const int MinAbility = 1;
    public const int MaxAbility = 100;
    public const int DefaultAbility = 50;

    /// <summary>A staff member whose every attribute is <paramref name="ability"/> (tests, older callers).</summary>
    public StaffMember(StaffMemberId id, string firstName, string lastName, DateOnly dateOfBirth, CountryId nationalityId, StaffRole role, int ability = DefaultAbility)
        : this(id, firstName, lastName, dateOfBirth, nationalityId, role, StaffProfile.Uniform(Guard.Range(ability, MinAbility, MaxAbility, "Ability")))
    {
    }

    public StaffMember(StaffMemberId id, string firstName, string lastName, DateOnly dateOfBirth, CountryId nationalityId, StaffRole role, StaffProfile profile)
        : base(id)
    {
        FirstName = Guard.Text(firstName, "First name", NameMaxLength);
        LastName = Guard.Text(lastName, "Last name", NameMaxLength);
        DateOfBirth = dateOfBirth;
        NationalityId = Guard.Required(nationalityId, nameof(nationalityId));
        Role = Guard.Defined(role, "Staff role");
        Update(profile);
    }

    private StaffMember()
    {
    }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public DateOnly DateOfBirth { get; private set; }

    public CountryId NationalityId { get; private set; }

    public StaffRole Role { get; private set; }

    public int TacticalKnowledge { get; private set; } = DefaultAbility;

    public int Development { get; private set; } = DefaultAbility;

    public int Motivation { get; private set; } = DefaultAbility;

    public int Scouting { get; private set; } = DefaultAbility;

    public int MedicalExpertise { get; private set; } = DefaultAbility;

    public int Reputation { get; private set; } = DefaultAbility;

    public int Experience { get; private set; }

    public CoachingStyle Style { get; private set; }

    public OffensiveSystem? PreferredOffense { get; private set; }

    public DefensiveSystem? PreferredDefense { get; private set; }

    public StaffProfile Profile => new(TacticalKnowledge, Development, Motivation, Scouting, MedicalExpertise, Reputation, Experience, Style, PreferredOffense, PreferredDefense);

    /// <summary>How good he is at his role's job (1–100, docs/STAFF.md §3); for a scout, how accurate his reports are (docs/SCOUTING.md §5).</summary>
    public int Ability => StaffRules.RoleAbility(Role, Profile);

    public string FullName => $"{FirstName} {LastName}";

    /// <summary>Age in whole years on <paramref name="date"/>.</summary>
    public int AgeOn(DateOnly date)
    {
        var age = date.Year - DateOfBirth.Year;
        return DateOfBirth.AddYears(age) > date ? age - 1 : age;
    }

    public void Update(StaffProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        TacticalKnowledge = Rating(profile.TacticalKnowledge, "Tactical knowledge");
        Development = Rating(profile.Development, "Player development");
        Motivation = Rating(profile.Motivation, "Motivation");
        Scouting = Rating(profile.Scouting, "Scouting");
        MedicalExpertise = Rating(profile.MedicalExpertise, "Medical expertise");
        Reputation = Rating(profile.Reputation, "Reputation");
        Experience = Guard.Range(profile.Experience, 0, StaffProfile.MaxExperience, "Experience");
        Style = Guard.Defined(profile.Style, "Coaching style");
        PreferredOffense = profile.PreferredOffense is { } offense ? Guard.Defined(offense, "Preferred offense") : null;
        PreferredDefense = profile.PreferredDefense is { } defense ? Guard.Defined(defense, "Preferred defense") : null;
    }

    private static int Rating(int value, string name) => Guard.Range(value, MinAbility, MaxAbility, name);
}
