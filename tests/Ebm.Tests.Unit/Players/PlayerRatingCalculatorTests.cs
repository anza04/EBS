using Ebm.Domain.Common;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;

namespace Ebm.Tests.Unit.Players;

public sealed class PlayerRatingCalculatorTests
{
    private static readonly PlayerRatingCalculator Calculator = PlayerRatingCalculator.Default;

    private static Player Make(PlayerAttributes attributes, Position position, int height = 200, int? wingspan = null, PositionSet secondary = default, int potential = 50)
    {
        var player = new Player(PlayerId.New(), "Test", "Player", new DateOnly(2000, 1, 1), CountryId.New(), height, 95, position, attributes);
        player.UpdateProfile(wingspan ?? height, secondary, 1, 1, potential);
        return player;
    }

    [Fact]
    public void Uniform_player_rates_by_physical_fit_and_familiarity()
    {
        var evaluation = Calculator.Evaluate(Make(PlayerAttributes.Uniform(60), Position.SmallForward));

        // Reach 200 cm: PG 1.2 too tall, SG/SF in range, PF 1.8 and C 4.8 too short.
        Assert.Equal([59, 60, 60, 58, 55], evaluation.Positions.Select(p => p.Rating));
        Assert.Equal([53, 54, 60, 52, 49], evaluation.Positions.Select(p => p.EffectiveRating));
        Assert.Equal(
            [PositionSuitability.Adequate, PositionSuitability.Adequate, PositionSuitability.Ideal, PositionSuitability.Adequate, PositionSuitability.Poor],
            evaluation.Positions.Select(p => p.Suitability));
        Assert.Equal(60, evaluation.Overall);
        Assert.Equal(Position.SmallForward, evaluation.BestPosition);
        Assert.All(evaluation.Categories.Values, v => Assert.Equal(60, v));
    }

    [Fact]
    public void Secondary_position_has_a_small_familiarity_penalty()
    {
        var evaluation = Calculator.Evaluate(Make(PlayerAttributes.Uniform(60), Position.SmallForward, secondary: PositionSet.Of(Position.ShootingGuard)));

        var guard = evaluation.At(Position.ShootingGuard);
        Assert.Equal((PositionFamiliarity.Secondary, 58, PositionSuitability.Ideal), (guard.Familiarity, guard.EffectiveRating, guard.Suitability));
        Assert.Equal(PositionFamiliarity.Unfamiliar, evaluation.At(Position.PointGuard).Familiarity);
    }

    [Fact]
    public void Wingspan_extends_reach()
    {
        var shortArms = Calculator.PositionRating(PlayerAttributes.Uniform(60), 190, 190, Position.SmallForward);
        var longArms = Calculator.PositionRating(PlayerAttributes.Uniform(60), 190, 210, Position.SmallForward);

        Assert.Equal(55, shortArms); // reach 190: 8 cm short × 0.6
        Assert.Equal(60, longArms); // reach 200: in range
        Assert.Equal(200, PlayerRatingCalculator.Reach(190, 210));
    }

    [Fact]
    public void Reach_penalty_is_capped()
    {
        Assert.Equal(45, Calculator.PositionRating(PlayerAttributes.Uniform(60), 150, 150, Position.Center));
    }

    [Fact]
    public void Position_suitability_follows_the_skill_profile()
    {
        var big = PlayerAttributes.Uniform(40)
            .With(PlayerAttribute.PostMoves, 90).With(PlayerAttribute.InteriorDefense, 90).With(PlayerAttribute.Block, 90)
            .With(PlayerAttribute.OffensiveRebound, 85).With(PlayerAttribute.DefensiveRebound, 90).With(PlayerAttribute.Strength, 85)
            .With(PlayerAttribute.CloseShot, 75);
        var guard = PlayerAttributes.Uniform(40)
            .With(PlayerAttribute.Passing, 90).With(PlayerAttribute.CourtVision, 90).With(PlayerAttribute.BallHandling, 90)
            .With(PlayerAttribute.ThreePoint, 85).With(PlayerAttribute.Speed, 85).With(PlayerAttribute.Agility, 85);

        var center = Calculator.Evaluate(Make(big, Position.PowerForward, height: 211, secondary: PositionSet.Of(Position.Center)));
        var pointGuard = Calculator.Evaluate(Make(guard, Position.PointGuard, height: 188));

        Assert.Equal(Position.Center, center.BestPosition);
        Assert.Equal(PositionSuitability.Poor, center.At(Position.PointGuard).Suitability);
        Assert.True(center.At(Position.Center).Rating > center.At(Position.SmallForward).Rating);
        Assert.Equal(Position.PointGuard, pointGuard.BestPosition);
        Assert.Equal(PositionSuitability.Poor, pointGuard.At(Position.Center).Suitability);
        Assert.True(pointGuard.Categories[AttributeCategory.Playmaking] > pointGuard.Categories[AttributeCategory.Rebounding]);
    }

    [Fact]
    public void Overall_is_the_primary_position_even_when_another_suits_better()
    {
        // A 195 cm center: 7.8 reach penalty at C; PG and SG fit physically but are unfamiliar (−6).
        var player = Make(PlayerAttributes.Uniform(60), Position.Center, height: 195);

        var evaluation = Calculator.Evaluate(player);

        Assert.Equal(52, evaluation.Overall);
        Assert.Equal([54, 54, 52, 49, 52], evaluation.Positions.Select(p => p.EffectiveRating));
        Assert.Equal(Position.PointGuard, evaluation.BestPosition); // PG/SG tie; position order breaks it
    }

    [Theory]
    [InlineData(30, 60)]
    [InlineData(80, 80)]
    public void Potential_rating_is_never_below_overall(int potential, int expected)
    {
        Assert.Equal(expected, Calculator.Evaluate(Make(PlayerAttributes.Uniform(60), Position.SmallForward, potential: potential)).PotentialRating);
    }

    [Fact]
    public void Ratings_are_deterministic()
    {
        var player = Make(PlayerAttributes.Uniform(47).With(PlayerAttribute.Dunk, 99), Position.PowerForward, height: 204, wingspan: 214);

        var first = Calculator.Evaluate(player);
        var second = new PlayerRatingCalculator(RatingModel.Default).Evaluate(player);

        Assert.Equal(first.Positions, second.Positions);
        Assert.Equal(first.Categories, second.Categories);
        Assert.Equal(first.Overall, second.Overall);
    }

    [Fact]
    public void Invalid_models_are_refused()
    {
        var model = RatingModel.Default;
        var noCenter = model.PositionWeights.Where(w => w.Key != Position.Center).ToDictionary();

        Assert.Throws<DomainValidationException>(() => (model with { PositionWeights = noCenter }).Validate());
        Assert.Throws<DomainValidationException>(() => (model with { GoodGap = 1, IdealGap = 2 }).Validate());
        Assert.Throws<DomainValidationException>(() => new PlayerRatingCalculator(model with { TallPenaltyPerCm = -1 }));
    }
}
