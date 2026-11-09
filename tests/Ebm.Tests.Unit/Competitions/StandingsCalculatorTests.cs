using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Competitions.Rules;

namespace Ebm.Tests.Unit.Competitions;

public sealed class StandingsCalculatorTests
{
    private static readonly CompetitionRules Defaults = CompetitionRules.Parse("{}");

    private readonly ClubId _a = ClubId.New();
    private readonly ClubId _b = ClubId.New();
    private readonly ClubId _c = ClubId.New();
    private readonly ClubId _d = ClubId.New();

    [Fact]
    public void Totals_and_points_follow_the_points_system()
    {
        GameResult[] results = [new(_a, _b, 80, 70), new(_b, _a, 90, 85), new(_a, _c, 77, 60)];

        var table = StandingsCalculator.Calculate([_a, _b, _c], results, CompetitionRules.Parse("""{"pointsForWin":3,"pointsForLoss":1}"""));

        var a = table.Single(r => r.ClubId == _a);
        Assert.Equal((1, 3, 2, 1, 242, 220, 7), (a.Position, a.Played, a.Won, a.Lost, a.PointsFor, a.PointsAgainst, a.Points));
        Assert.Equal(22, a.PointDifference);
        Assert.Equal([_a, _b, _c], table.Select(r => r.ClubId));
        Assert.Equal(0, table.Single(r => r.ClubId == _c).Won);
    }

    [Fact]
    public void Clubs_without_games_are_listed()
    {
        var table = StandingsCalculator.Calculate([_a, _b], [], Defaults);

        Assert.All(table, r => Assert.Equal(0, r.Played));
        Assert.Equal([1, 2], table.Select(r => r.Position));
    }

    [Fact]
    public void Head_to_head_beats_overall_point_difference()
    {
        // A and B both 2-1. B won the meeting; A has the better overall difference.
        GameResult[] results = [new(_b, _a, 71, 70), new(_a, _c, 100, 50), new(_b, _c, 61, 60), new(_a, _d, 90, 60), new(_d, _b, 80, 70)];

        var table = StandingsCalculator.Calculate([_a, _b, _c, _d], results, Defaults);
        Assert.Equal(_b, table[0].ClubId);

        var byDifference = CompetitionRules.Parse("""{"tieBreakers":["PointDifference","HeadToHeadWins"]}""");
        Assert.Equal(_a, StandingsCalculator.Calculate([_a, _b, _c, _d], results, byDifference)[0].ClubId);
    }

    [Fact]
    public void Three_way_ties_use_the_mini_league_then_restart_for_the_rest()
    {
        // A, B and C each beat one another once (circular), so head-to-head wins are level;
        // head-to-head difference: A +8, B -2, C -6.
        GameResult[] results = [new(_a, _b, 80, 70), new(_b, _c, 80, 72), new(_c, _a, 78, 76)];

        var table = StandingsCalculator.Calculate([_a, _b, _c], results, Defaults);

        Assert.Equal([_a, _b, _c], table.Select(r => r.ClubId));
    }

    [Fact]
    public void Clubs_level_on_everything_are_ordered_by_id()
    {
        var table = StandingsCalculator.Calculate([_a, _b], [new(_a, _b, 80, 70), new(_b, _a, 80, 70)], Defaults);

        Assert.True(table[0].ClubId.Value.CompareTo(table[1].ClubId.Value) < 0);
    }

    [Fact]
    public void Results_of_unknown_clubs_or_draws_are_refused()
    {
        Assert.Throws<DomainValidationException>(() => StandingsCalculator.Calculate([_a], [new(_a, _b, 80, 70)], Defaults));
        Assert.Throws<DomainValidationException>(() => StandingsCalculator.Calculate([_a, _b], [new(_a, _b, 70, 70)], Defaults));
    }
}
