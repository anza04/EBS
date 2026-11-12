using Ebm.Domain.Countries;
using Ebm.Domain.Players;

namespace Ebm.Domain.Management;

/// <summary>First and last names to combine for one nationality, taken from the players already in the database.</summary>
public sealed record NamePool(CountryId Nationality, IReadOnlyList<string> FirstNames, IReadOnlyList<string> LastNames);

/// <summary>
/// Creates fictional young players to keep the free-agent pool supplied (docs/AI_MANAGEMENT.md §7): the stand-in for
/// youth intake until the youth step. Names recombine existing players' names of the nationality (data, not code);
/// attributes follow the position's profile around a quality level. Deterministic for a seed.
/// </summary>
public sealed class PlayerGenerator(int seed)
{
    public const int MinAge = 18;
    public const int MaxAge = 23;
    public const int MinQuality = 38;
    public const int MaxQuality = 60;

    private static readonly IReadOnlyDictionary<Position, (int Height, int Weight, IReadOnlyDictionary<AttributeCategory, int> Profile)> Profiles =
        new Dictionary<Position, (int, int, IReadOnlyDictionary<AttributeCategory, int>)>
        {
            [Position.PointGuard] = (186, 84, new Dictionary<AttributeCategory, int> { [AttributeCategory.Playmaking] = 10, [AttributeCategory.Rebounding] = -10, [AttributeCategory.Finishing] = -4 }),
            [Position.ShootingGuard] = (194, 90, new Dictionary<AttributeCategory, int> { [AttributeCategory.Shooting] = 8, [AttributeCategory.Rebounding] = -6 }),
            [Position.SmallForward] = (200, 96, new Dictionary<AttributeCategory, int>()),
            [Position.PowerForward] = (205, 104, new Dictionary<AttributeCategory, int> { [AttributeCategory.Rebounding] = 8, [AttributeCategory.Finishing] = 4, [AttributeCategory.Playmaking] = -6 }),
            [Position.Center] = (211, 112, new Dictionary<AttributeCategory, int> { [AttributeCategory.Rebounding] = 10, [AttributeCategory.Finishing] = 6, [AttributeCategory.Shooting] = -10, [AttributeCategory.Playmaking] = -10 }),
        };

    private readonly Random _random = new(seed);

    /// <param name="isTaken">Whether a player with this name and birth date already exists (duplicate rule).</param>
    public Player Create(NamePool names, Position position, DateOnly today, Func<string, string, DateOnly, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(isTaken);
        if (names.FirstNames.Count == 0 || names.LastNames.Count == 0)
        {
            throw new ArgumentException("The name pool is empty.", nameof(names));
        }

        string first, last;
        DateOnly birth;
        do
        {
            first = names.FirstNames[_random.Next(names.FirstNames.Count)];
            last = names.LastNames[_random.Next(names.LastNames.Count)];
            birth = today.AddYears(-_random.Next(MinAge, MaxAge + 1)).AddDays(-_random.Next(0, 365));
        }
        while (isTaken(first, last, birth));

        var (height, weight, profile) = Profiles[position];
        var quality = _random.Next(MinQuality, MaxQuality + 1);
        var attributes = new PlayerAttributes(PlayerCatalog.Attributes.ToDictionary(
            a => a,
            a => Math.Clamp(quality + profile.GetValueOrDefault(PlayerCatalog.CategoryOf(a)) + _random.Next(-6, 7), 20, 90)));
        var player = new Player(PlayerId.New(), first, last, birth, names.Nationality, height + _random.Next(-4, 5), weight + _random.Next(-5, 6), position, attributes);
        var age = today.Year - birth.Year;
        player.UpdateProfile(player.HeightCm + _random.Next(0, 8), PositionSet.Empty, 10 + (age - MinAge) * 4, Math.Max(5, quality - 25), Math.Min(95, quality + 10 + _random.Next(0, 15)));
        return player;
    }

    public Position NextPosition() => (Position)_random.Next(0, 5);
}
