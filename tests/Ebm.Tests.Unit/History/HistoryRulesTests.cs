using Ebm.Domain.Clubs;
using Ebm.Domain.Countries;
using Ebm.Domain.History;
using Ebm.Domain.Management;
using Ebm.Domain.Players;
using Ebm.Domain.Staff;

namespace Ebm.Tests.Unit.History;

/// <summary>Step 32: the pure rules of the long-term world (docs/HISTORY.md).</summary>
public sealed class HistoryRulesTests
{
    private static readonly ClubId Club = ClubId.New();

    [Fact]
    public void Awards_go_to_the_best_eligible_players()
    {
        var scorer = Line(games: 30, points: 750, age: 25);           // 25.0 ppg
        var rebounder = Line(games: 30, points: 300, rebounds: 360);  // 12 rpg
        var passer = Line(games: 30, points: 300, assists: 270, age: 21);
        var defender = Line(games: 30, points: 200, steals: 90, blocks: 90);
        var cameo = Line(games: 10, points: 400);                    // 40 ppg but under half the games
        var picks = HistoryRules.Awards([scorer, rebounder, passer, defender, cameo, Line(games: 30, points: 100)]);

        PlayerSeasonLine Winner(AwardKind kind) => Assert.Single(picks, p => p.Kind == kind).Line;
        Assert.Equal(scorer, Winner(AwardKind.TopScorer));
        Assert.Equal(250, Assert.Single(picks, p => p.Kind == AwardKind.TopScorer).Value);
        Assert.Equal(rebounder, Winner(AwardKind.TopRebounder));
        Assert.Equal(passer, Winner(AwardKind.TopAssists));
        Assert.Equal(defender, Winner(AwardKind.BestDefender));
        Assert.Equal(passer, Winner(AwardKind.YoungPlayer));
        Assert.DoesNotContain(picks, p => p.Line == cameo);
        var team = picks.Where(p => p.Kind == AwardKind.AllLeagueTeam).ToList();
        Assert.Equal([1, 2, 3, 4, 5], team.Select(p => p.Rank));
        Assert.Equal(Winner(AwardKind.MostValuablePlayer), team[0].Line);
        Assert.Empty(HistoryRules.Awards([]));
    }

    [Fact]
    public void Winning_teams_make_the_mvp()
    {
        var loser = Line(games: 30, points: 700, winShare: 0.1);
        var winner = Line(games: 30, points: 600, winShare: 0.9);
        Assert.Equal(winner, HistoryRules.Awards([loser, winner]).Single(p => p.Kind == AwardKind.MostValuablePlayer).Line);
    }

    [Fact]
    public void Coach_of_the_season_beats_expectations_most()
    {
        var (a, b, c) = (ClubId.New(), ClubId.New(), ClubId.New());
        var best = HistoryRules.CoachOfTheSeason([new(a, 1, 1), new(b, 8, 3), new(c, 6, 4)]);
        Assert.Equal(b, best!.ClubId);
        Assert.Null(HistoryRules.CoachOfTheSeason([]));
    }

    [Fact]
    public void Hall_of_fame_rewards_long_careers_titles_and_awards()
    {
        var journeyman = new CareerSummary(10, 300, 3000, 1500, 600, 0, 0, 0, 0, 55);
        Assert.Equal(30 + 15 + 7, HistoryRules.HallOfFameScore(journeyman));
        Assert.True(HistoryRules.HallOfFameScore(journeyman) >= HistoryRules.HallOfFameThreshold);
        var star = new CareerSummary(1, 30, 600, 150, 150, 1, 1, 1, 1, 90);
        Assert.Equal(3 + 3 + 1 + 8 + 15 + 5 + 3 + 30, HistoryRules.HallOfFameScore(star));
        Assert.True(HistoryRules.HallOfFameScore(new CareerSummary(3, 60, 500, 100, 100, 0, 0, 0, 0, 50)) < HistoryRules.HallOfFameThreshold);
    }

    [Fact]
    public void Smart_leaders_become_coaches()
    {
        Assert.Null(HistoryRules.CoachingRole(PlayerAttributes.Uniform(50), hallOfFame: false));
        Assert.Equal(StaffRole.HeadCoach, HistoryRules.CoachingRole(PlayerAttributes.Uniform(50), hallOfFame: true));
        Assert.Equal(StaffRole.AssistantCoach, HistoryRules.CoachingRole(PlayerAttributes.Uniform(65), hallOfFame: false));
        Assert.Equal(StaffRole.HeadCoach, HistoryRules.CoachingRole(PlayerAttributes.Uniform(80), hallOfFame: false));

        var player = new Player(PlayerId.New(), "Ann", "Coach", new DateOnly(1990, 1, 1), CountryId.New(), 190, 85, Position.PointGuard,
            PlayerAttributes.Uniform(70).With(PlayerAttribute.Leadership, 95));
        var profile = HistoryRules.CoachProfile(player);
        Assert.Equal(CoachingStyle.Motivator, profile.Style);
        Assert.Equal((65, 60, 95, 55, 20, 0), (profile.TacticalKnowledge, profile.Development, profile.Motivation, profile.Scouting, profile.MedicalExpertise, profile.Experience));
    }

    [Fact]
    public void Academies_produce_prospects_by_reputation()
    {
        Assert.Equal((1, 2), (HistoryRules.IntakeSize(40), HistoryRules.IntakeSize(80)));
        var random = new Random(1);
        for (var i = 0; i < 200; i++)
        {
            var (quality, potential) = HistoryRules.Prospect(i % 100, random);
            Assert.InRange(quality, 20, 60);
            Assert.InRange(potential, quality + 20, 95);
        }

        var names = new NamePool(CountryId.New(), ["Luca", "Marco"], ["Rossi", "Bianchi"]);
        var youth = new PlayerGenerator(7).CreateYouth(names, Position.Center, new DateOnly(2027, 6, 30), (_, _, _) => false, Club, HistoryRules.YouthAge, 40, 80);
        Assert.Equal((Club, 17, 80, 0), (youth.YouthClubId!.Value, youth.AgeOn(new DateOnly(2027, 6, 30)), youth.Potential, youth.Experience - 1));
    }

    [Fact]
    public void Only_ready_youth_players_are_promoted_while_there_is_room()
    {
        var (ready, young, weak) = (PlayerId.New(), PlayerId.New(), PlayerId.New());
        var youth = new[] { (ready, 19, 55), (young, 18, 70), (weak, 20, 40) };
        Assert.Equal([ready], HistoryRules.Promotions(youth, [50, 60, 70], 12));
        Assert.Empty(HistoryRules.Promotions(youth, [.. Enumerable.Repeat(45, 12)], 12));
    }

    [Theory]
    [InlineData(90, -2)]
    [InlineData(70, -1)]
    [InlineData(60, 0)]
    [InlineData(50, 0)]
    [InlineData(20, 1)]
    public void Reputations_drift_back_to_the_middle(int reputation, int drift) => Assert.Equal(drift, HistoryRules.Drift(reputation));

    private static PlayerSeasonLine Line(int games, int points, int rebounds = 0, int assists = 0, int steals = 0, int blocks = 0, int age = 28, double winShare = 0.5) =>
        new(PlayerId.New(), Club, age, games, games * 30, points, rebounds, rebounds / 2, assists, steals, blocks, 0, winShare);
}
