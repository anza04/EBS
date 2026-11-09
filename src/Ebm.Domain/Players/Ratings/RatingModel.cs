using Ebm.Domain.Common;

namespace Ebm.Domain.Players.Ratings;

/// <summary>How familiar a player is with a position.</summary>
public enum PositionFamiliarity
{
    Primary,
    Secondary,
    Unfamiliar,
}

/// <summary>How well a player suits a position, relative to his best position.</summary>
public enum PositionSuitability
{
    Ideal,
    Good,
    Adequate,
    Poor,
}

/// <summary>Ideal reach range for a position, in cm. Reach = height + half the wingspan surplus.</summary>
public sealed record ReachRange(int MinCm, int MaxCm);

/// <summary>
/// The tunable numbers of the rating system: position weights, physical ranges, familiarity penalties and
/// suitability thresholds. <see cref="Default"/> is the game's model; formulas are in docs/PLAYER_MODEL.md.
/// </summary>
public sealed record RatingModel(
    IReadOnlyDictionary<Position, IReadOnlyDictionary<PlayerAttribute, int>> PositionWeights,
    IReadOnlyDictionary<Position, ReachRange> IdealReach,
    double ShortPenaltyPerCm,
    double TallPenaltyPerCm,
    int MaxReachPenalty,
    IReadOnlyDictionary<PositionFamiliarity, int> FamiliarityPenalty,
    int IdealGap,
    int GoodGap,
    int AdequateGap)
{
    // Weight of each attribute (rows) at PG, SG, SF, PF, C (columns), 0–5.
    private static readonly (PlayerAttribute Attribute, int[] Weights)[] DefaultWeights =
    [
        (PlayerAttribute.CloseShot, [2, 2, 2, 3, 3]),
        (PlayerAttribute.MidRange, [3, 4, 3, 2, 1]),
        (PlayerAttribute.ThreePoint, [4, 5, 4, 2, 1]),
        (PlayerAttribute.FreeThrow, [2, 2, 2, 1, 1]),
        (PlayerAttribute.Layup, [3, 3, 3, 2, 2]),
        (PlayerAttribute.Dunk, [0, 1, 2, 2, 3]),
        (PlayerAttribute.PostMoves, [0, 0, 1, 4, 5]),
        (PlayerAttribute.Passing, [5, 3, 2, 1, 1]),
        (PlayerAttribute.CourtVision, [5, 2, 2, 1, 1]),
        (PlayerAttribute.BallHandling, [5, 4, 2, 1, 0]),
        (PlayerAttribute.DecisionMaking, [4, 3, 3, 2, 2]),
        (PlayerAttribute.PerimeterDefense, [4, 4, 4, 2, 1]),
        (PlayerAttribute.InteriorDefense, [0, 1, 2, 4, 5]),
        (PlayerAttribute.Steal, [3, 3, 2, 1, 1]),
        (PlayerAttribute.Block, [0, 1, 2, 3, 5]),
        (PlayerAttribute.DefensivePositioning, [3, 3, 3, 3, 3]),
        (PlayerAttribute.OffensiveRebound, [0, 1, 2, 3, 4]),
        (PlayerAttribute.DefensiveRebound, [1, 1, 2, 4, 5]),
        (PlayerAttribute.Speed, [4, 3, 3, 2, 1]),
        (PlayerAttribute.Strength, [1, 1, 2, 4, 5]),
        (PlayerAttribute.Vertical, [1, 2, 3, 3, 3]),
        (PlayerAttribute.Agility, [4, 4, 3, 2, 1]),
        (PlayerAttribute.Stamina, [2, 2, 2, 2, 2]),
        (PlayerAttribute.Concentration, [2, 2, 2, 2, 2]),
        (PlayerAttribute.Leadership, [1, 1, 1, 1, 1]),
        (PlayerAttribute.Consistency, [2, 2, 2, 2, 2]),
        (PlayerAttribute.WorkRate, [1, 1, 1, 1, 1]),
    ];

    public static RatingModel Default { get; } = new RatingModel(
        Enum.GetValues<Position>().ToDictionary(
            p => p,
            p => (IReadOnlyDictionary<PlayerAttribute, int>)DefaultWeights.ToDictionary(w => w.Attribute, w => w.Weights[(int)p])),
        new Dictionary<Position, ReachRange>
        {
            [Position.PointGuard] = new(183, 196),
            [Position.ShootingGuard] = new(191, 201),
            [Position.SmallForward] = new(198, 207),
            [Position.PowerForward] = new(203, 212),
            [Position.Center] = new(208, 222),
        },
        ShortPenaltyPerCm: 0.6,
        TallPenaltyPerCm: 0.3,
        MaxReachPenalty: 15,
        new Dictionary<PositionFamiliarity, int>
        {
            [PositionFamiliarity.Primary] = 0,
            [PositionFamiliarity.Secondary] = 2,
            [PositionFamiliarity.Unfamiliar] = 6,
        },
        IdealGap: 2,
        GoodGap: 5,
        AdequateGap: 10).Validate();

    /// <summary>Checks the model is complete and consistent; returns it for chaining.</summary>
    public RatingModel Validate()
    {
        var errors = new List<string>();
        foreach (var position in Enum.GetValues<Position>())
        {
            if (!PositionWeights.TryGetValue(position, out var weights))
            {
                errors.Add($"No attribute weights for {position}.");
            }
            else if (PlayerCatalog.Attributes.Any(a => !weights.ContainsKey(a)) || weights.Values.Any(w => w < 0) || weights.Values.Sum() <= 0)
            {
                errors.Add($"The weights of {position} must cover every attribute, be zero or more and not all be zero.");
            }

            if (!IdealReach.TryGetValue(position, out var reach) || reach.MinCm > reach.MaxCm)
            {
                errors.Add($"The ideal reach of {position} is missing or its minimum is above its maximum.");
            }
        }

        if (ShortPenaltyPerCm < 0 || TallPenaltyPerCm < 0 || MaxReachPenalty < 0)
        {
            errors.Add("Reach penalties cannot be negative.");
        }

        if (Enum.GetValues<PositionFamiliarity>().Any(f => !FamiliarityPenalty.TryGetValue(f, out var p) || p < 0))
        {
            errors.Add("Every familiarity needs a penalty of zero or more.");
        }

        if (IdealGap < 0 || GoodGap < IdealGap || AdequateGap < GoodGap)
        {
            errors.Add("Suitability gaps must satisfy 0 <= ideal <= good <= adequate.");
        }

        return errors.Count == 0 ? this : throw new DomainValidationException(string.Join(" ", errors));
    }
}
