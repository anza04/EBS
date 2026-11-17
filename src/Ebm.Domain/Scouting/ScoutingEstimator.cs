using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;

namespace Ebm.Domain.Scouting;

/// <summary>What a scout believes: estimated attributes and ratings with their error margins.</summary>
public sealed record ScoutEstimate(int Accuracy, int Margin, int PotentialMargin, PlayerAttributes Attributes, int Overall, int Potential);

/// <summary>
/// Turns a player's true ratings into a scout's estimates (docs/SCOUTING.md §5). Every error is drawn from a
/// triangular distribution on [−margin, margin], so estimates are usually close and never further than the margin.
/// Deterministic for a seed.
/// </summary>
public sealed class ScoutingEstimator(PlayerRatingCalculator calculator)
{
    public static ScoutingEstimator Default { get; } = new(PlayerRatingCalculator.Default);

    /// <summary>A full report: every attribute estimated, the overall rating computed from the estimates as for a real player.</summary>
    public ScoutEstimate Estimate(Player player, int accuracy, int seed)
    {
        ArgumentNullException.ThrowIfNull(player);
        accuracy = Math.Clamp(accuracy, ScoutingRules.MinAccuracy, ScoutingRules.MaxAccuracy);
        var margin = ScoutingRules.Margin(accuracy);
        var potentialMargin = ScoutingRules.PotentialMargin(accuracy);
        var random = new Random(seed);
        var attributes = new PlayerAttributes(PlayerCatalog.Attributes.ToDictionary(a => a, a => Clamp(player.Attributes[a] + Error(random, margin))));
        var overall = Math.Max(
            Player.MinRating,
            calculator.PositionRating(attributes, player.HeightCm, player.WingspanCm, player.Position) - calculator.Model.FamiliarityPenalty[PositionFamiliarity.Primary]);
        var truth = calculator.Evaluate(player);
        var potential = Math.Max(overall, Clamp(truth.PotentialRating + Error(random, potentialMargin)));
        return new ScoutEstimate(accuracy, margin, potentialMargin, attributes, overall, potential);
    }

    /// <summary>A quick estimate of the overall and potential ratings only (AI recruitment, many players at once).</summary>
    public static (int Overall, int Potential) EstimateRatings(PlayerEvaluation truth, int accuracy, int seed)
    {
        ArgumentNullException.ThrowIfNull(truth);
        var random = new Random(seed);
        var overall = Clamp(truth.Overall + Error(random, ScoutingRules.Margin(accuracy)));
        return (overall, Math.Max(overall, Clamp(truth.PotentialRating + Error(random, ScoutingRules.PotentialMargin(accuracy)))));
    }

    private static int Error(Random random, int margin) =>
        (int)Math.Round((random.Next(-margin, margin + 1) + random.Next(-margin, margin + 1)) / 2.0, MidpointRounding.AwayFromZero);

    private static int Clamp(int value) => Math.Clamp(value, Player.MinRating, Player.MaxRating);
}
