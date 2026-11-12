using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;
using Ebm.Domain.Tactics;

namespace Ebm.Tests.Unit.Matches;

public sealed class MatchSimulationTests
{
    private static MatchSetup Setup(int seed, int homeQuality = 60, int awayQuality = 60) =>
        new(SimulationTeams.Team("Home", homeQuality, seed), SimulationTeams.Team("Away", awayQuality, seed + 7), seed);

    private static void AssertValid(MatchSimulation sim, MatchResult result)
    {
        Assert.True(sim.IsFinished);
        Assert.NotEqual(result.HomeScore, result.AwayScore);
        Assert.Equal(result.HomeScore, result.HomeLines.Sum(l => l.Points));
        Assert.Equal(result.AwayScore, result.AwayLines.Sum(l => l.Points));
        var seconds = (2400 + (300 * result.Overtimes)) * 5;
        foreach (var side in TeamSides.Both)
        {
            Assert.Equal(seconds, result.Lines(side).Sum(l => l.SecondsPlayed), 6);
            Assert.All(result.Lines(side), l =>
            {
                l.ToBoxScore().Validate();
                Assert.InRange(l.PersonalFouls, 0, 5);
                Assert.Equal(l.PersonalFouls == 5, l.FouledOut);
            });
        }

        var events = sim.Events;
        Assert.Equal(MatchEventKind.GameEnd, events[^1].Kind);
        Assert.Equal((result.HomeScore, result.AwayScore), (events[^1].HomeScore, events[^1].AwayScore));
        Assert.Equal(4 + result.Overtimes, events.Count(e => e.Kind == MatchEventKind.PeriodStart));
        foreach (var pair in events.Zip(events.Skip(1)))
        {
            Assert.True(pair.Second.Period > pair.First.Period || pair.Second.Clock <= pair.First.Clock, "The clock never runs backwards.");
            Assert.True(pair.Second.HomeScore >= pair.First.HomeScore && pair.Second.AwayScore >= pair.First.AwayScore);
        }
    }

    [Fact]
    public void Complete_matches_follow_the_rules_and_never_reach_an_invalid_state()
    {
        for (var seed = 0; seed < 60; seed++)
        {
            var sim = new MatchSimulation(Setup(seed, 55 + (seed % 10), 60));
            var result = sim.RunToEnd();
            AssertValid(sim, result);
            Assert.False(result.EndedByDefault);
        }
    }

    [Fact]
    public void A_short_handed_team_plays_on_with_fewer_players_and_loses_by_default_below_two()
    {
        var hacking = TeamTactics.Default with
        {
            DefensiveSystem = DefensiveSystem.FullCourtPress,
            Defense = new DefensiveSettings { Intensity = 5, BallPressure = 5 },
        };
        for (var seed = 0; seed < 150; seed++)
        {
            var sim = new MatchSimulation(new MatchSetup(SimulationTeams.Team("Five", 60, seed, hacking, players: 5), SimulationTeams.Team("Full", 60, seed + 1), seed));
            var result = sim.RunToEnd();
            Assert.NotEqual(result.HomeScore, result.AwayScore);
            Assert.All(result.HomeLines, l => Assert.InRange(l.PersonalFouls, 0, 5));
            Assert.Equal(MatchEventKind.GameEnd, sim.Events[^1].Kind);
            if (result.EndedByDefault)
            {
                Assert.True(result.AwayScore > result.HomeScore);
            }
            else
            {
                Assert.Equal(result.HomeScore, result.HomeLines.Sum(l => l.Points));
            }

            // Nobody who fouled out plays again.
            foreach (var line in result.HomeLines.Where(l => l.FouledOut))
            {
                var foulOut = sim.Events.First(e => e.Kind == MatchEventKind.FouledOut && e.Player == line.Player.Id);
                Assert.DoesNotContain(sim.Events.SkipWhile(e => e != foulOut).Skip(1), e => e.Player == line.Player.Id && e.Kind is MatchEventKind.FieldGoalMade or MatchEventKind.FieldGoalMissed or MatchEventKind.Foul);
            }
        }
    }

    [Fact]
    public void Overtime_is_played_until_the_tie_is_broken()
    {
        var (sim, result) = Enumerable.Range(0, 500)
            .Select(seed => new MatchSimulation(Setup(seed)))
            .Select(s => (s, s.RunToEnd()))
            .First(m => m.Item2.Overtimes > 0);

        AssertValid(sim, result);
        var endOfRegulation = sim.Events.Last(e => e.Kind == MatchEventKind.PeriodEnd && e.Period == 4);
        Assert.Equal(endOfRegulation.HomeScore, endOfRegulation.AwayScore);
        Assert.Contains(sim.Events, e => e.Kind == MatchEventKind.PeriodStart && e.Period == 5 && e.Text.Contains("overtime", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_setup_gives_the_same_match()
    {
        var a = new MatchSimulation(Setup(42)).RunToEnd();
        var b = new MatchSimulation(Setup(42)).RunToEnd();
        Assert.Equal((a.HomeScore, a.AwayScore, a.Possessions), (b.HomeScore, b.AwayScore, b.Possessions));
    }

    [Fact]
    public void Fatigue_drains_energy_and_tired_teams_perform_worse()
    {
        var sim = new MatchSimulation(Setup(3));
        sim.RunToEndOfPeriod();
        Assert.Equal(1, sim.Events[^1].Period);
        Assert.Equal(MatchEventKind.PeriodEnd, sim.Events[^1].Kind);
        Assert.All(sim.Lines(TeamSide.Home).Where(l => l.SecondsPlayed > 300), l => Assert.True(l.Energy < 100));

        static TeamSheet Tired(TeamSheet sheet)
        {
            foreach (var p in sheet.Players)
            {
                p.Player.UpdateCondition(45, 50, InjuryStatus.Healthy, null);
            }

            return sheet;
        }

        var fresh = Enumerable.Range(0, 150).Select(i => new MatchSimulation(new MatchSetup(SimulationTeams.Team("A", 60, i), SimulationTeams.Team("B", 60, i + 500), i)).RunToEnd()).ToList();
        var tired = Enumerable.Range(0, 150).Select(i => new MatchSimulation(new MatchSetup(Tired(SimulationTeams.Team("A", 60, i)), SimulationTeams.Team("B", 60, i + 500), i)).RunToEnd()).ToList();
        Assert.True(
            tired.Average(r => r.HomeScore - r.AwayScore) < fresh.Average(r => r.HomeScore - r.AwayScore) - 3,
            "Low fitness must cost points.");
    }

    [Fact]
    public void Live_mode_allows_substitutions_timeouts_and_tactic_changes()
    {
        var sim = new MatchSimulation(Setup(5), SimulationMode.Live);
        sim.SetAutomatic(TeamSide.Home, false);
        sim.Step(); // tip-off
        var onCourt = sim.OnCourt(TeamSide.Home);
        var bench = sim.Lines(TeamSide.Home).First(l => !l.IsOnCourt);
        sim.Substitute(TeamSide.Home, onCourt[0].Player.Id, bench.Player.Id);
        Assert.Contains(sim.OnCourt(TeamSide.Home), l => l.Player.Id == bench.Player.Id);
        Assert.Throws<DomainValidationException>(() => sim.Substitute(TeamSide.Home, bench.Player.Id, bench.Player.Id));

        sim.CallTimeout(TeamSide.Home);
        sim.CallTimeout(TeamSide.Home);
        Assert.Equal(0, sim.TimeoutsLeft(TeamSide.Home));
        Assert.Throws<DomainValidationException>(() => sim.CallTimeout(TeamSide.Home)); // two in the first half

        var press = TeamTactics.Default with { DefensiveSystem = DefensiveSystem.FullCourtPress };
        sim.ChangeTactics(TeamSide.Home, press);
        Assert.Equal(DefensiveSystem.FullCourtPress, sim.Tactics(TeamSide.Home).DefensiveSystem);
        Assert.Equal(MatchEventKind.TacticsChanged, sim.Events[^1].Kind);

        // The managed side keeps its five until the user changes them (no automatic rest substitutions).
        var five = sim.OnCourt(TeamSide.Home).Select(l => l.Player.Id).ToList();
        for (var i = 0; i < 20; i++)
        {
            sim.Step();
        }

        Assert.True(five.SequenceEqual(sim.OnCourt(TeamSide.Home).Select(l => l.Player.Id)) || sim.Lines(TeamSide.Home).Any(l => l.FouledOut));
        AssertValid(sim, sim.RunToEnd());
    }

    [Fact]
    public void Interventions_need_live_mode()
    {
        var sim = new MatchSimulation(Setup(1), SimulationMode.Commentary);
        sim.Step();
        Assert.Throws<DomainValidationException>(() => sim.CallTimeout(TeamSide.Home));
        Assert.Throws<DomainValidationException>(() => sim.ChangeTactics(TeamSide.Away, TeamTactics.Default));
        Assert.NotEmpty(sim.Step());
        Assert.Throws<DomainException>(() => sim.Result());
    }

    [Fact]
    public void Invalid_team_sheets_are_refused()
    {
        var home = SimulationTeams.Team("Home", 60, 1);
        Assert.Throws<DomainValidationException>(() => new MatchSimulation(new MatchSetup(home, SimulationTeams.Team("Short", 60, 2, players: 4), 1)));
        Assert.Throws<DomainValidationException>(() => new MatchSimulation(new MatchSetup(home, home with { Name = "Copy" }, 1)));
        var noStarters = home with { Players = [.. home.Players.Select(p => p with { StartingPosition = null })] };
        Assert.Throws<DomainValidationException>(() => new MatchSimulation(new MatchSetup(noStarters, SimulationTeams.Team("Away", 60, 3), 1)));
    }

    [Fact]
    public void The_matchday_squad_is_picked_from_the_roster_and_completed_when_needed()
    {
        var random = new Random(9);
        var players = Enumerable.Range(0, 14).Select(i => SimulationTeams.CreatePlayer((Position)(i % 5), 60 + i, random, $"P{i}")).ToList();
        players[13].UpdateCondition(100, 50, InjuryStatus.Injured, new DateOnly(2027, 1, 1));
        var roster = players.Select((p, i) => (p, i switch
        {
            0 => RosterRole.Starter,
            < 4 => RosterRole.Rotation,
            12 => RosterRole.Youth,
            _ => RosterRole.Bench,
        }, i == 0 ? (Position?)Position.PointGuard : null, i is > 0 and < 4 ? (int?)i : null));

        var sheet = TeamSheet.Pick(ClubId.New(), "Club", roster, TeamTactics.Default, PlayerRatingCalculator.Default);

        sheet.Validate(MatchRules.Fiba);
        Assert.Equal(12, sheet.Players.Count);
        Assert.DoesNotContain(sheet.Players, p => p.Player == players[13]); // injured
        Assert.DoesNotContain(sheet.Players, p => p.Player == players[12]); // youth
        Assert.Equal(Position.PointGuard, sheet.Players.Single(p => p.Player == players[0]).StartingPosition);
        Assert.Equal(5, sheet.Players.Count(p => p.StartingPosition is not null));
    }
}
