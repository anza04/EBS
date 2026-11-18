using Ebm.Domain.Chemistry;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Management;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;
using Ebm.Domain.Tactics;
using Ebm.Tests.Unit.Matches;
using Xunit.Abstractions;

namespace Ebm.Tests.Unit.Chemistry;

/// <summary>Step 26: squad hierarchy, satisfaction, morale, relationships and chemistry (docs/CHEMISTRY.md).</summary>
public sealed class ChemistryModelTests(ITestOutputHelper output)
{
    private static readonly CountryId Country = CountryId.New();

    private static Player Make(int leadership = 50, int experience = 50, int morale = 50, Position position = Position.SmallForward)
    {
        var player = new Player(PlayerId.New(), "Chem", "Test", new DateOnly(1998, 1, 1), Country, 200, 95, position, PlayerAttributes.Uniform(60).With(PlayerAttribute.Leadership, leadership));
        player.UpdateProfile(player.WingspanCm, player.SecondaryPositions, experience, 50, 60);
        player.UpdateCondition(player.Fitness, morale, InjuryStatus.Healthy, null);
        return player;
    }

    [Fact]
    public void Hierarchy_ranks_by_overall_with_expected_roles_minutes_captain_and_leaders()
    {
        var veteran = Make(leadership: 90, experience: 90);
        var squad = Enumerable.Range(0, 10).Select(i => new HierarchyInput(i == 9 ? veteran : Make(), 80 - (i * 2), i == 7 ? 20 : 28)).ToList();
        squad[8] = squad[8] with { Promises = new ContractPromises(MinutesPerGame: 20) };

        var places = ChemistryModel.Hierarchy(squad);

        Assert.Equal(Enumerable.Range(1, 10), places.Select(p => p.Rank));
        Assert.Equal(
            [SquadRole.Star, SquadRole.Star, SquadRole.Starter, SquadRole.Starter, SquadRole.Starter, SquadRole.Rotation, SquadRole.Rotation, SquadRole.Prospect, SquadRole.Reserve, SquadRole.Reserve],
            places.Select(p => p.Role));
        Assert.Equal(30, places[0].ExpectedMinutes);
        Assert.Equal(20, places[8].ExpectedMinutes); // a promise above the reserve's 8 minutes
        var captain = Assert.Single(places, p => p.IsCaptain);
        Assert.Same(veteran, captain.Player);
        Assert.Equal(90, captain.LeaderScore);
        Assert.True(captain.IsLeader);
        Assert.Single(places, p => p.IsLeader);
    }

    [Theory]
    [InlineData(25, null, RoleSatisfaction.NotJudged)]
    [InlineData(25, 25.0, RoleSatisfaction.Happy)]
    [InlineData(25, 15.0, RoleSatisfaction.Content)]
    [InlineData(25, 14.9, RoleSatisfaction.Unhappy)]
    public void Satisfaction_compares_minutes_with_the_expectation(int expected, double? minutes, RoleSatisfaction satisfaction) =>
        Assert.Equal(satisfaction, ChemistryModel.Satisfaction(expected, minutes));

    [Fact]
    public void Morale_follows_results_playing_time_and_leaders_and_stays_in_range()
    {
        Assert.Equal(-5, ChemistryModel.MoraleChange(50, SquadRole.Star, RoleSatisfaction.Unhappy, 0, 3, hasLeader: false));
        Assert.Equal(3, ChemistryModel.MoraleChange(50, SquadRole.Starter, RoleSatisfaction.Happy, 2, 0, hasLeader: false));
        Assert.Equal(0, ChemistryModel.MoraleChange(50, SquadRole.Reserve, RoleSatisfaction.Unhappy, 1, 1, hasLeader: true)); // reserves do not complain
        Assert.Equal(-1, ChemistryModel.MoraleChange(50, SquadRole.Prospect, RoleSatisfaction.Unhappy, 0, 0, hasLeader: false));
        Assert.Equal(-1, ChemistryModel.MoraleChange(70, SquadRole.Rotation, RoleSatisfaction.NotJudged, 0, 0, hasLeader: false)); // drift down
        Assert.Equal(1, ChemistryModel.MoraleChange(35, SquadRole.Rotation, RoleSatisfaction.NotJudged, 0, 0, hasLeader: true)); // a leader lifts him
        Assert.Equal(0, ChemistryModel.MoraleChange(35, SquadRole.Rotation, RoleSatisfaction.NotJudged, 0, 0, hasLeader: false));
        Assert.Equal(-1, ChemistryModel.MoraleChange(2, SquadRole.Star, RoleSatisfaction.Unhappy, 0, 3, hasLeader: false)); // floor 1
        Assert.True(ChemistryModel.InConflict(30, RoleSatisfaction.Unhappy));
        Assert.False(ChemistryModel.InConflict(31, RoleSatisfaction.Unhappy));
        Assert.False(ChemistryModel.InConflict(10, RoleSatisfaction.Content));
    }

    [Fact]
    public void Relationships_grow_with_court_time_and_suffer_from_conflicts()
    {
        Assert.Equal(0, ChemistryModel.RelationshipChange(0, true, false));
        Assert.Equal(2, ChemistryModel.RelationshipChange(1, false, false));
        Assert.Equal(4, ChemistryModel.RelationshipChange(3, true, false));
        Assert.Equal(-6, ChemistryModel.RelationshipChange(0, false, true));

        var a = PlayerId.New();
        var b = PlayerId.New();
        var r = new PlayerRelationship(PlayerRelationshipId.New(), b, a, new DateOnly(2026, 1, 1));
        Assert.Equal(PlayerRelationship.Order(a, b), (r.PlayerAId, r.PlayerBId));
        Assert.Equal(a, r.Other(b));
        r.Adjust(150, new DateOnly(2026, 1, 8));
        Assert.Equal((PlayerRelationship.Max, new DateOnly(2026, 1, 8)), (r.Score, r.UpdatedOn));
        Assert.Throws<Ebm.Domain.Common.DomainValidationException>(() => new PlayerRelationship(PlayerRelationshipId.New(), a, a, default));
    }

    [Fact]
    public void Team_chemistry_combines_morale_relationships_leadership_fit_and_conflicts()
    {
        static (Player, TacticalRole) InRole(Player p) => (p, TacticalRoles.Suggest(p));
        var neutral = Enumerable.Range(0, 8).Select(_ => InRole(Make(leadership: 60, experience: 60))).ToList();
        Assert.Equal(TeamChemistry.Neutral, ChemistryModel.Team(neutral, (_, _) => 0));

        var happy = Enumerable.Range(0, 8).Select(i => InRole(Make(leadership: i == 0 ? 90 : 60, experience: i == 0 ? 90 : 50, morale: 90))).ToList();
        var good = ChemistryModel.Team(happy, (_, _) => 50);
        Assert.Equal((1.0, 1.0, 1.0), (good.Morale, good.Relationships, good.Leadership));
        Assert.Equal(0.75, good.Score, 3); // fit cannot add, only cost
        Assert.Equal(88, good.Rating);

        var unhappy = Enumerable.Range(0, 8).Select(i => InRole(Make(morale: i < 3 ? 20 : 50))).ToList();
        var bad = ChemistryModel.Team(unhappy, (_, _) => -100);
        Assert.Equal(3, bad.Conflicts);
        Assert.Equal(-1, bad.Relationships); // clamped
        Assert.True(bad.Score < -0.6);

        // A guard used as a rim protector costs tactical fit.
        var guard = SimulationTeams.CreatePlayer(Position.PointGuard, 60, new Random(1), "Guard");
        var misused = ChemistryModel.Team([(guard, TacticalRole.RimProtector)], (_, _) => 0);
        Assert.True(misused.Tactical < 0);
        Assert.Equal(0, ChemistryModel.Team([], (_, _) => 0).Score);
    }

    [Fact]
    public void Morale_shifts_a_players_form_by_at_most_one_point()
    {
        Assert.Equal(0, ChemistryModel.FormEdge(50));
        Assert.Equal(0.01, ChemistryModel.FormEdge(100), 6);
        Assert.Equal(-0.0098, ChemistryModel.FormEdge(1), 6);
    }

    [Fact]
    public void Chemistry_out_of_range_is_rejected()
    {
        var sheet = SimulationTeams.Team("A", 60, 1) with { Chemistry = 1.5 };
        Assert.Throws<Ebm.Domain.Common.DomainValidationException>(() => sheet.Validate(MatchRules.Fiba));
        Assert.Equal(5 + ChemistryModel.CoreRotation, SimulationTeams.Team("A", 60, 1).Core.Count()); // 5 starters + 3
    }

    [Fact]
    public void Chemistry_changes_results_in_a_controlled_way()
    {
        const int games = 300;
        double Margin(double chemistry) => Enumerable.Range(0, games)
            .Select(i => new MatchSimulation(new MatchSetup(SimulationTeams.Team("A", 60, i) with { Chemistry = chemistry }, SimulationTeams.Team("B", 60, i + 5000), i) { NeutralVenue = true }).RunToEnd())
            .Average(r => r.HomeScore - r.AwayScore);
        double Wins(double chemistry) => Enumerable.Range(0, games)
            .Select(i => new MatchSimulation(new MatchSetup(SimulationTeams.Team("A", 60, i) with { Chemistry = chemistry }, SimulationTeams.Team("B", 60, i + 5000), i) { NeutralVenue = true }).RunToEnd())
            .Count(r => r.HomeScore > r.AwayScore) / (double)games;
        var good = Margin(1);
        var neutral = Margin(0);
        var bad = Margin(-1);
        var quality = Enumerable.Range(0, games)
            .Select(i => new MatchSimulation(new MatchSetup(SimulationTeams.Team("A", 66, i), SimulationTeams.Team("B", 60, i + 5000), i) { NeutralVenue = true }).RunToEnd())
            .Average(r => r.HomeScore - r.AwayScore) - neutral;
        output.WriteLine($"Margin: chemistry +1 {good:F1}, 0 {neutral:F1}, -1 {bad:F1}; +6 overall {quality:F1}");

        Assert.True(good > neutral && neutral > bad);
        Assert.InRange(good - bad, 2, 10); // measurable ...
        Assert.True(good - neutral < quality); // ... but perfect chemistry is worth less than six points of overall
        Assert.InRange(Wins(-1), 0.25, 0.5); // a bad dressing room still wins games
    }

    [Fact]
    public void Ai_coach_gives_an_unhappy_player_minutes_over_a_slightly_better_teammate()
    {
        var calculator = PlayerRatingCalculator.Default;
        var squad = Enumerable.Range(0, 12).Select(i => new Player(PlayerId.New(), "Squad", $"P{i}", new DateOnly(1998, 1, 1), Country, 200, 95, Enum.GetValues<Position>()[i % 5], PlayerAttributes.Uniform(70 - i))).ToList();
        LineupCandidate Candidate(Player p, int balance = 0) => new(p.Id, RosterRole.Bench, PlayerAvailability.Available, calculator.Evaluate(p), balance);
        var plain = LineupPlanner.Plan([.. squad.Select(p => Candidate(p))]);
        var bench = plain.First(p => p.Role == RosterRole.Bench).PlayerId;
        var lastRotation = plain.Last(p => p.Role == RosterRole.Rotation).PlayerId;
        var gap = calculator.Evaluate(squad.First(p => p.Id == lastRotation)).Overall - calculator.Evaluate(squad.First(p => p.Id == bench)).Overall;
        Assert.InRange(gap, 0, ChemistryModel.BalanceBonus - 1);

        var balanced = LineupPlanner.Plan([.. squad.Select(p => Candidate(p, p.Id == bench ? ChemistryModel.BalanceBonus : 0))]);
        Assert.Equal(RosterRole.Rotation, balanced.Single(p => p.PlayerId == bench).Role);
    }
}
