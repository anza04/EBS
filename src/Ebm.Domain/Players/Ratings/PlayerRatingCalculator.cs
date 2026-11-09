namespace Ebm.Domain.Players.Ratings;

/// <summary>A player's rating at one position.</summary>
/// <param name="Rating">Skill and physical fit for the position, before familiarity (1–100).</param>
/// <param name="EffectiveRating">Rating minus the familiarity penalty: what the player gives at the position today.</param>
public sealed record PositionEvaluation(
    Position Position,
    int Rating,
    int EffectiveRating,
    PositionFamiliarity Familiarity,
    PositionSuitability Suitability);

/// <summary>
/// Everything derived from a player's attributes. Decisions should use the part that matters to them
/// (a category, a position, a tendency), not only <see cref="Overall"/>.
/// </summary>
/// <param name="Overall">Effective rating at the primary position.</param>
/// <param name="PotentialRating">The overall rating the player can reach: never below <see cref="Overall"/>.</param>
/// <param name="Categories">Position-independent ratings: the mean of each attribute category.</param>
/// <param name="Positions">One evaluation per position, in position order.</param>
/// <param name="BestPosition">Highest effective rating; ties go to the primary position, then position order.</param>
public sealed record PlayerEvaluation(
    int Overall,
    int PotentialRating,
    IReadOnlyDictionary<AttributeCategory, int> Categories,
    IReadOnlyList<PositionEvaluation> Positions,
    Position BestPosition)
{
    public PositionEvaluation At(Position position) => Positions[(int)position];
}

/// <summary>Pure, deterministic rating formulas over a <see cref="RatingModel"/> (docs/PLAYER_MODEL.md §4).</summary>
public sealed class PlayerRatingCalculator
{
    public PlayerRatingCalculator(RatingModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Model = model.Validate();
    }

    public static PlayerRatingCalculator Default { get; } = new(RatingModel.Default);

    public RatingModel Model { get; }

    public static int CategoryRating(PlayerAttributes attributes, AttributeCategory category)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return Round(PlayerCatalog.AttributesOf(category).Average(a => attributes[a]));
    }

    /// <summary>Reach in cm: height plus half the difference between wingspan and height.</summary>
    public static double Reach(int heightCm, int wingspanCm) => heightCm + ((wingspanCm - heightCm) / 2.0);

    /// <summary>Skill and physical fit at a position, ignoring familiarity.</summary>
    public int PositionRating(PlayerAttributes attributes, int heightCm, int wingspanCm, Position position)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var weights = Model.PositionWeights[position];
        var skill = (double)weights.Sum(w => attributes[w.Key] * w.Value) / weights.Values.Sum();
        return Math.Clamp(Round(skill - ReachPenalty(Reach(heightCm, wingspanCm), position)), RatingSet<PlayerAttribute>.MinRating, RatingSet<PlayerAttribute>.MaxRating);
    }

    public PlayerEvaluation Evaluate(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var effective = Enum.GetValues<Position>()
            .Select(p =>
            {
                var familiarity = p == player.Position ? PositionFamiliarity.Primary
                    : player.SecondaryPositions.Contains(p) ? PositionFamiliarity.Secondary
                    : PositionFamiliarity.Unfamiliar;
                var rating = PositionRating(player.Attributes, player.HeightCm, player.WingspanCm, p);
                return (Position: p, Rating: rating, Effective: Math.Max(RatingSet<PlayerAttribute>.MinRating, rating - Model.FamiliarityPenalty[familiarity]), Familiarity: familiarity);
            })
            .ToList();

        var best = effective.Max(e => e.Effective);
        var bestPosition = effective.Where(e => e.Effective == best)
            .OrderBy(e => e.Familiarity != PositionFamiliarity.Primary)
            .ThenBy(e => e.Position)
            .First().Position;
        var overall = effective[(int)player.Position].Effective;

        return new PlayerEvaluation(
            overall,
            Math.Max(overall, player.Potential),
            PlayerCatalog.Categories.ToDictionary(c => c, c => CategoryRating(player.Attributes, c)),
            [.. effective.Select(e => new PositionEvaluation(e.Position, e.Rating, e.Effective, e.Familiarity, Suitability(best - e.Effective)))],
            bestPosition);
    }

    private PositionSuitability Suitability(int gap) =>
        gap <= Model.IdealGap ? PositionSuitability.Ideal
        : gap <= Model.GoodGap ? PositionSuitability.Good
        : gap <= Model.AdequateGap ? PositionSuitability.Adequate
        : PositionSuitability.Poor;

    private double ReachPenalty(double reach, Position position)
    {
        var range = Model.IdealReach[position];
        var penalty = reach < range.MinCm ? (range.MinCm - reach) * Model.ShortPenaltyPerCm
            : reach > range.MaxCm ? (reach - range.MaxCm) * Model.TallPenaltyPerCm
            : 0;
        return Math.Min(penalty, Model.MaxReachPenalty);
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
