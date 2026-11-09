using Ebm.Domain.Common;
using Ebm.Domain.Countries;

namespace Ebm.Domain.Players;

/// <summary>
/// A basketball player. The ID is permanent: the player keeps it, and with it all history and
/// statistics, across clubs, seasons and save games.
/// </summary>
public sealed class Player : Entity<PlayerId>
{
    public const int NameMaxLength = 60;
    public const int MinHeightCm = 150;
    public const int MaxHeightCm = 250;
    public const int MinWeightKg = 50;
    public const int MaxWeightKg = 200;

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
    }

    private Player()
    {
    }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string FullName => $"{FirstName} {LastName}";

    public DateOnly DateOfBirth { get; private set; }

    public CountryId NationalityId { get; private set; }

    public int HeightCm { get; private set; }

    public int WeightKg { get; private set; }

    public Position Position { get; private set; }

    public PlayerAttributes Attributes { get; private set; } = null!;

    /// <summary>Changes the player's personal details. The ID, attributes and history are unaffected.</summary>
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
    }

    public void UpdateAttributes(PlayerAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        Attributes = attributes;
    }
}
