using Ebm.Domain.Common;
using Ebm.Domain.Countries;

namespace Ebm.Domain.Players;

/// <summary>Whether a player can play. Detailed injuries and recovery are Step 22.</summary>
public enum InjuryStatus
{
    Healthy,

    /// <summary>Minor problem; the player may miss single games. A return date is optional.</summary>
    DayToDay,

    /// <summary>Out until the expected return date.</summary>
    Injured,
}

/// <summary>
/// A basketball player. The ID is permanent: the player keeps it, and with it all history and
/// statistics, across clubs, seasons and save games. Current club and contract are not stored here:
/// they come from <see cref="PlayerClubHistoryEntry"/> and <see cref="Contracts.Contract"/>.
/// </summary>
/// <remarks>Field definitions and ranges: docs/PLAYER_MODEL.md.</remarks>
public sealed class Player : Entity<PlayerId>
{
    public const int NameMaxLength = 60;
    public const int MinHeightCm = 150;
    public const int MaxHeightCm = 250;
    public const int MinWeightKg = 50;
    public const int MaxWeightKg = 200;

    /// <summary>Allowed difference between wingspan and height, in either direction.</summary>
    public const int MaxWingspanDifferenceCm = 30;
    public const int MinRating = 1;
    public const int MaxRating = 100;
    public const int MinFitness = 0;
    public const int MaxFitness = 100;

    /// <summary>Default values of new players (and of players stored before Step 05).</summary>
    public const int DefaultExperience = 1;
    public const int DefaultReputation = 1;
    public const int DefaultPotential = 50;
    public const int DefaultFitness = 100;
    public const int DefaultMorale = 50;

    public Player(
        PlayerId id,
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        CountryId nationalityId,
        int heightCm,
        int weightKg,
        Position position,
        PlayerAttributes attributes)
        : base(id)
    {
        UpdateDetails(firstName, lastName, dateOfBirth, nationalityId, heightCm, weightKg, position);
        UpdateAttributes(attributes);
        UpdateProfile(heightCm, PositionSet.Empty, DefaultExperience, DefaultReputation, DefaultPotential);
        UpdateCondition(DefaultFitness, DefaultMorale, InjuryStatus.Healthy, null);
        UpdateTendencies(PlayerTendencies.Suggest(attributes));
        UpdateDataQuality(DataStatus.Generated, DataStatus.Generated, null);
    }

    private Player()
    {
    }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string FullName => $"{FirstName} {LastName}";

    public DateOnly DateOfBirth { get; private set; }

    /// <summary>Sporting nationality: decides domestic and foreign-player counts.</summary>
    public CountryId NationalityId { get; private set; }

    /// <summary>A further citizenship (passport), if any; counts for EU/EEA status only. Never equal to <see cref="NationalityId"/>.</summary>
    public CountryId? SecondNationalityId { get; private set; }

    /// <summary>Every nationality the player holds, sporting nationality first.</summary>
    public IEnumerable<CountryId> Nationalities => SecondNationalityId is { } second ? [NationalityId, second] : [NationalityId];

    /// <summary>Trust level of the personal details (name, birth date, nationalities, body, positions).</summary>
    public DataStatus ProfileStatus { get; private set; }

    /// <summary>Trust level of the attributes and tendencies. Game ratings of real players are at best estimates.</summary>
    public DataStatus AttributesStatus { get; private set; }

    /// <summary>Where the player's information comes from (publication, website, database); required when verified.</summary>
    public string? Source { get; private set; }

    public int HeightCm { get; private set; }

    public int WeightKg { get; private set; }

    /// <summary>Arm span in cm; with height it gives the player's reach.</summary>
    public int WingspanCm { get; private set; }

    /// <summary>Primary position.</summary>
    public Position Position { get; private set; }

    /// <summary>Other positions the player is used to; never contains <see cref="Position"/>.</summary>
    public PositionSet SecondaryPositions { get; private set; }

    /// <summary>Professional experience, 1–100. Grows with games played (Step 12).</summary>
    public int Experience { get; private set; }

    /// <summary>How well known and respected the player is, 1–100.</summary>
    public int Reputation { get; private set; }

    /// <summary>The highest overall rating the player can reach, 1–100.</summary>
    public int Potential { get; private set; }

    /// <summary>Match fitness, 0–100 (100 = fully fresh).</summary>
    public int Fitness { get; private set; }

    /// <summary>Morale, 1–100 (50 = neutral).</summary>
    public int Morale { get; private set; }

    public InjuryStatus InjuryStatus { get; private set; }

    /// <summary>Expected return date; required while <see cref="InjuryStatus.Injured"/>, optional while day-to-day.</summary>
    public DateOnly? InjuryReturnDate { get; private set; }

    public PlayerAttributes Attributes { get; private set; } = null!;

    public PlayerTendencies Tendencies { get; private set; } = null!;

    /// <summary>Age in whole years on <paramref name="date"/>.</summary>
    public int AgeOn(DateOnly date)
    {
        var age = date.Year - DateOfBirth.Year;
        return DateOfBirth.AddYears(age) > date ? age - 1 : age;
    }

    /// <summary>
    /// Changes the player's personal details. The ID, attributes and history are unaffected. A new primary
    /// position is removed from the secondary positions.
    /// </summary>
    public void UpdateDetails(
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        CountryId nationalityId,
        int heightCm,
        int weightKg,
        Position position)
    {
        FirstName = Guard.Text(firstName, "First name", NameMaxLength);
        LastName = Guard.Text(lastName, "Last name", NameMaxLength);
        DateOfBirth = dateOfBirth;
        NationalityId = Guard.Required(nationalityId, "Nationality");
        HeightCm = Guard.Range(heightCm, MinHeightCm, MaxHeightCm, "Height (cm)");
        WeightKg = Guard.Range(weightKg, MinWeightKg, MaxWeightKg, "Weight (kg)");
        Position = Guard.Defined(position, "Position");
        SecondaryPositions = SecondaryPositions.Without(position);
        if (SecondNationalityId == nationalityId)
        {
            SecondNationalityId = null;
        }
    }

    /// <summary>Sets or clears the second citizenship.</summary>
    public void UpdateSecondNationality(CountryId? secondNationalityId)
    {
        if (secondNationalityId == NationalityId)
        {
            throw new DomainValidationException("The second nationality must differ from the nationality.");
        }

        SecondNationalityId = Guard.Optional(secondNationalityId, "Second nationality");
    }

    /// <summary>Sets the trust level of the profile and of the attributes, and the source (required when either is verified).</summary>
    public void UpdateDataQuality(DataStatus profileStatus, DataStatus attributesStatus, string? source)
    {
        (ProfileStatus, Source) = DataQuality.Check(profileStatus, source, "The player profile");
        (AttributesStatus, _) = DataQuality.Check(attributesStatus, source, "The player attributes");
    }

    /// <summary>Whether any of the player's nationalities satisfies <paramref name="predicate"/> (e.g. EU/EEA membership).</summary>
    public bool HasNationality(Func<CountryId, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Nationalities.Any(predicate);
    }

    /// <summary>Changes the basketball profile: wingspan, secondary positions, experience, reputation and potential.</summary>
    public void UpdateProfile(int wingspanCm, PositionSet secondaryPositions, int experience, int reputation, int potential)
    {
        if (Math.Abs(wingspanCm - HeightCm) > MaxWingspanDifferenceCm)
        {
            throw new DomainValidationException(
                $"Wingspan (cm) must be within {MaxWingspanDifferenceCm} cm of the height ({HeightCm} cm); got {wingspanCm}.");
        }

        if (secondaryPositions.Contains(Position))
        {
            throw new DomainValidationException($"{Position} is the primary position; it cannot also be a secondary position.");
        }

        WingspanCm = wingspanCm;
        SecondaryPositions = secondaryPositions;
        Experience = Guard.Range(experience, MinRating, MaxRating, "Experience");
        Reputation = Guard.Range(reputation, MinRating, MaxRating, "Reputation");
        Potential = Guard.Range(potential, MinRating, MaxRating, "Potential");
    }

    /// <summary>Changes the player's current condition.</summary>
    public void UpdateCondition(int fitness, int morale, InjuryStatus injuryStatus, DateOnly? injuryReturnDate)
    {
        Guard.Defined(injuryStatus, "Injury status");
        if (injuryStatus == InjuryStatus.Healthy && injuryReturnDate is not null)
        {
            throw new DomainValidationException("A healthy player has no injury return date.");
        }

        if (injuryStatus == InjuryStatus.Injured && injuryReturnDate is null)
        {
            throw new DomainValidationException("An injured player needs an expected return date.");
        }

        Fitness = Guard.Range(fitness, MinFitness, MaxFitness, "Fitness");
        Morale = Guard.Range(morale, MinRating, MaxRating, "Morale");
        InjuryStatus = injuryStatus;
        InjuryReturnDate = injuryReturnDate;
    }

    public void UpdateAttributes(PlayerAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        Attributes = attributes;
    }

    public void UpdateTendencies(PlayerTendencies tendencies)
    {
        ArgumentNullException.ThrowIfNull(tendencies);
        Tendencies = tendencies;
    }
}
