namespace Ebm.Domain.Players;

/// <summary>
/// Typical body and attribute profile of each position: shared by the player generator and the attribute estimator,
/// so generated and estimated players look alike.
/// </summary>
public static class PositionProfile
{
    private static readonly IReadOnlyDictionary<Position, (int Height, int Weight, IReadOnlyDictionary<AttributeCategory, int> Profile)> Profiles =
        new Dictionary<Position, (int, int, IReadOnlyDictionary<AttributeCategory, int>)>
        {
            [Position.PointGuard] = (186, 84, new Dictionary<AttributeCategory, int> { [AttributeCategory.Playmaking] = 10, [AttributeCategory.Rebounding] = -10, [AttributeCategory.Finishing] = -4 }),
            [Position.ShootingGuard] = (194, 90, new Dictionary<AttributeCategory, int> { [AttributeCategory.Shooting] = 8, [AttributeCategory.Rebounding] = -6 }),
            [Position.SmallForward] = (200, 96, new Dictionary<AttributeCategory, int>()),
            [Position.PowerForward] = (205, 104, new Dictionary<AttributeCategory, int> { [AttributeCategory.Rebounding] = 8, [AttributeCategory.Finishing] = 4, [AttributeCategory.Playmaking] = -6 }),
            [Position.Center] = (211, 112, new Dictionary<AttributeCategory, int> { [AttributeCategory.Rebounding] = 10, [AttributeCategory.Finishing] = 6, [AttributeCategory.Shooting] = -10, [AttributeCategory.Playmaking] = -10 }),
        };

    /// <summary>Typical height (cm), weight (kg) and attribute offsets by category of <paramref name="position"/>.</summary>
    public static (int Height, int Weight, IReadOnlyDictionary<AttributeCategory, int> Profile) Of(Position position) => Profiles[position];
}

/// <summary>
/// Fills in unknown attributes (docs/PLAYER_DATABASE.md §3): the quality level is the average of the known attributes
/// (at least <see cref="MinKnownForAverage"/>), else derived from the player's reputation; each missing attribute is
/// that level plus the position's offset. Deterministic, so re-importing the same data gives the same estimate.
/// </summary>
public static class AttributeEstimator
{
    public const int MinKnownForAverage = 3;
    public const int MinEstimate = 20;
    public const int MaxEstimate = 90;

    /// <summary>Quality level of a player known only by reputation (1–100): 35 for an unknown, 80 for a star.</summary>
    public static int QualityFromReputation(int reputation) => (int)Math.Round(35 + Math.Clamp(reputation, 1, 100) * 0.45);

    /// <summary>All attributes: the known values kept, the missing ones estimated.</summary>
    public static PlayerAttributes Complete(IReadOnlyDictionary<PlayerAttribute, int> known, Position position, int reputation)
    {
        ArgumentNullException.ThrowIfNull(known);
        var quality = known.Count >= MinKnownForAverage ? (int)Math.Round(known.Values.Average()) : QualityFromReputation(reputation);
        var offsets = PositionProfile.Of(position).Profile;
        return new PlayerAttributes(PlayerCatalog.Attributes.ToDictionary(
            a => a,
            a => known.TryGetValue(a, out var value)
                ? value
                : Math.Clamp(quality + offsets.GetValueOrDefault(PlayerCatalog.CategoryOf(a)), MinEstimate, MaxEstimate)));
    }
}
