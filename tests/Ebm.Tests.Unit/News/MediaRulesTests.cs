using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Matches;
using Ebm.Domain.Medical;
using Ebm.Domain.News;
using Ebm.Domain.Seasons;
using Ebm.Domain.Transfers;

namespace Ebm.Tests.Unit.News;

/// <summary>Step 31: media rules and story writing (docs/NEWS.md).</summary>
public sealed class MediaRulesTests
{
    [Fact]
    public void Upsets_need_a_reputation_gap_and_reputation_points_follow_the_event()
    {
        Assert.True(MediaRules.IsUpset(40, 40 + MediaRules.UpsetGap));
        Assert.False(MediaRules.IsUpset(40, 40 + MediaRules.UpsetGap - 1));
        Assert.False(MediaRules.IsUpset(80, 40));
        Assert.All(Enum.GetValues<ReputationCause>().Where(c => c is not (ReputationCause.CompetitionReward or ReputationCause.LongTermDrift)), c => Assert.NotEqual(0, MediaRules.Points(c)));
        Assert.True(MediaRules.Points(ReputationCause.LeagueTitle) > MediaRules.Points(ReputationCause.ObjectiveMet));
        Assert.True(MediaRules.Points(ReputationCause.Relegation) < MediaRules.Points(ReputationCause.UpsetDefeat));
    }

    [Fact]
    public void Fans_expect_more_from_bigger_clubs_and_judge_position_and_form()
    {
        Assert.Equal(FanExpectation.Title, MediaRules.Expect(1, 12));
        Assert.Equal(FanExpectation.Playoffs, MediaRules.Expect(4, 12));
        Assert.Equal(FanExpectation.MidTable, MediaRules.Expect(8, 12));
        Assert.Equal(FanExpectation.Survival, MediaRules.Expect(9, 12));
        Assert.Equal(FanExpectation.Playoffs, MediaRules.Expect(1, 1));

        Assert.Equal(FanMood.Delighted, MediaRules.Mood(FanExpectation.MidTable, 1, 12, 5, 5));
        Assert.Equal(FanMood.Content, MediaRules.Mood(FanExpectation.Title, 1, 12, 3, 5));
        Assert.Equal(FanMood.Restless, MediaRules.Mood(FanExpectation.Title, 2, 12, 2, 5));
        Assert.Equal(FanMood.Angry, MediaRules.Mood(FanExpectation.Title, 10, 12, 0, 5));

        // The same position pleases the fans of a small club and angers those of a big one.
        Assert.True(MediaRules.Mood(FanExpectation.Survival, 6, 12, 2, 5) > MediaRules.Mood(FanExpectation.Title, 6, 12, 2, 5));
    }

    [Fact]
    public void Streaks_count_the_current_run()
    {
        Assert.Equal(0, MediaRules.Streak([]));
        Assert.Equal(3, MediaRules.Streak([true, true, true, false, true]));
        Assert.Equal(-2, MediaRules.Streak([false, false, true]));
    }

    [Fact]
    public void Stories_use_the_facts_and_read_the_same_every_time()
    {
        var match = new MatchStory("Aurora", "Virtus", true, 88, 70, 1, "Serie A", new StarLine("Ann Lee", 25, 8, 4), null, 5);
        var review = NewsWriter.Review("review:1", match);
        Assert.Equal(review, NewsWriter.Review("review:1", match));
        Assert.Contains("88–70", review.Headline, StringComparison.Ordinal);
        Assert.Contains("(OT)", review.Body, StringComparison.Ordinal);
        Assert.Contains("Ann Lee (25 pts, 8 reb, 4 ast)", review.Body, StringComparison.Ordinal);
        Assert.Contains("5 wins in a row", review.Body, StringComparison.Ordinal);
        Assert.Equal(2, review.Sentiment);
        Assert.True(NewsWriter.Review("review:2", match with { ClubScore = 60 }).Sentiment < 0);

        var preview = NewsWriter.Preview("p", new("Aurora", 3, "WWL", ["Ann Lee"]), new("Virtus", null, string.Empty, []), false, "Serie A", new DateOnly(2026, 10, 4));
        Assert.Equal("Preview: Virtus v Aurora", preview.Headline);
        Assert.Contains("Away: Aurora (3rd), form WWL; unavailable: Ann Lee", preview.Body, StringComparison.Ordinal);
        Assert.Contains("Home: Virtus; full squad available", preview.Body, StringComparison.Ordinal);

        Assert.Contains("as a free agent", NewsWriter.Transfer(TransferKind.FreeAgentSigning, "Ann Lee", null, "Aurora", 0, null, false).Body, StringComparison.Ordinal);
        var rumoured = NewsWriter.Transfer(TransferKind.Transfer, "Ann Lee", "Virtus", "Aurora", 1_500_000, null, true);
        Assert.Contains("€1.5m", rumoured.Body, StringComparison.Ordinal);
        Assert.Contains("confirms the reports", rumoured.Body, StringComparison.Ordinal);

        var injury = NewsWriter.Injury("Ann Lee", "Aurora", "Ankle sprain", InjurySeverity.Moderate, InjurySource.Match, new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 1));
        Assert.Equal("Aurora: Ann Lee out with an ankle sprain", injury.Headline);
        Assert.Contains("about 2 weeks", injury.Body, StringComparison.Ordinal);
        Assert.Contains("from 50 to 53 (+3)", NewsWriter.Reputation("Aurora", ReputationCause.LeagueTitle, 50, 53, "Champions.").Body, StringComparison.Ordinal);
        Assert.Equal(["1st", "2nd", "3rd", "4th", "11th", "12th", "21st"], new[] { 1, 2, 3, 4, 11, 12, 21 }.Select(NewsWriter.Ordinal));
    }

    [Fact]
    public void News_items_and_reputation_changes_validate_their_data()
    {
        Assert.Throws<DomainValidationException>(() => new NewsItem(NewsItemId.New(), "k", new DateOnly(2026, 1, 1), NewsCategory.Rumor, "h", "b", 3));
        Assert.Throws<DomainValidationException>(() => new NewsItem(NewsItemId.New(), string.Empty, new DateOnly(2026, 1, 1), NewsCategory.Rumor, "h", "b"));
        var club = ClubId.New();
        var item = new NewsItem(NewsItemId.New(), "k", new DateOnly(2026, 1, 1), NewsCategory.MatchReview, "h", "b") { ClubId = club, OtherClubId = ClubId.New() };
        Assert.True(item.Involves(club));
        var change = new ReputationChange(ReputationChangeId.New(), "r", club, new DateOnly(2026, 1, 1), ReputationCause.UpsetWin, 50, 51, SeasonId.New(), MatchId.New());
        Assert.Equal(1, change.Points);
        Assert.Throws<DomainValidationException>(() => new ReputationChange(ReputationChangeId.New(), "r", club, new DateOnly(2026, 1, 1), ReputationCause.UpsetWin, 50, 101, SeasonId.New(), null));
    }
}
