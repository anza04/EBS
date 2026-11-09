using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Ebm.Domain.Standings;

namespace Ebm.Tests.Unit.Domain;

/// <summary>Rules of box scores and league standings.</summary>
public sealed class StatisticsRulesTests
{
    [Fact]
    public void Box_score_derives_points_and_rebounds()
    {
        var box = new BoxScore
        {
            Minutes = 30, TwoPointMade = 5, TwoPointAttempted = 9, ThreePointMade = 2, ThreePointAttempted = 6,
            FreeThrowMade = 3, FreeThrowAttempted = 4, OffensiveRebounds = 2, DefensiveRebounds = 5,
        };

        box.Validate();

        Assert.Equal(19, box.Points);
        Assert.Equal(7, box.Rebounds);
    }

    [Theory]
    [InlineData(3, 2, 0, 0)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(-1, 0, 0, 0)]
    public void Box_score_rejects_more_makes_than_attempts_or_negatives(int twoMade, int twoAttempted, int ftMade, int ftAttempted)
    {
        var box = new BoxScore { TwoPointMade = twoMade, TwoPointAttempted = twoAttempted, FreeThrowMade = ftMade, FreeThrowAttempted = ftAttempted };

        Assert.Throws<DomainValidationException>(() =>
            new PlayerMatchStatistics(PlayerMatchStatisticsId.New(), MatchId.New(), PlayerId.New(), ClubId.New(), box));
    }

    [Fact]
    public void Standing_accumulates_results()
    {
        var standing = new LeagueStanding(LeagueStandingId.New(), CompetitionId.New(), SeasonId.New(), ClubId.New());

        standing.RecordResult(90, 80, standingPointsAwarded: 2);
        standing.RecordResult(70, 75, standingPointsAwarded: 0);

        Assert.Equal(2, standing.Played);
        Assert.Equal(1, standing.Won);
        Assert.Equal(1, standing.Lost);
        Assert.Equal(160, standing.PointsFor);
        Assert.Equal(155, standing.PointsAgainst);
        Assert.Equal(5, standing.PointDifference);
        Assert.Equal(2, standing.StandingPoints);
    }

    [Fact]
    public void Standing_rejects_a_draw()
    {
        var standing = new LeagueStanding(LeagueStandingId.New(), CompetitionId.New(), SeasonId.New(), ClubId.New());

        Assert.Throws<DomainValidationException>(() => standing.RecordResult(80, 80, 1));
        Assert.Equal(0, standing.Played);
    }
}
