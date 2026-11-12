using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Tactics;
using Xunit.Abstractions;

namespace Ebm.Tests.Unit.Matches;

/// <summary>
/// Large-scale statistical validation of the engine (docs/MATCH_ENGINE.md §7): averages over many matches between
/// generated teams must fall in plausible European ranges.
/// </summary>
public sealed class MatchStatisticsValidationTests(ITestOutputHelper output)
{
    private const int Matches = 400;

    internal sealed record Averages(
        double Points,
        double Possessions,
        double TwoPct,
        double ThreePct,
        double FreeThrowPct,
        double ThreeShare,
        double FreeThrowAttempts,
        double Rebounds,
        double OffensiveRebounds,
        double Assists,
        double Turnovers,
        double Steals,
        double Blocks,
        double Fouls,
        double OvertimeShare,
        double TopScorerShare,
        double StarterMinutes,
        double HomeWins);

    internal static Averages Run(int matches, Func<int, MatchSetup> setup)
    {
        var results = Enumerable.Range(0, matches).Select(i => new MatchSimulation(setup(i)).RunToEnd()).ToList();
        var teams = results.SelectMany(r => new[] { r.Statistics(TeamSide.Home), r.Statistics(TeamSide.Away) }).ToList();
        double Avg(Func<Ebm.Domain.Matches.Simulation.TeamStatistics, double> f) => teams.Average(f);
        var lines = results.SelectMany(r => new[] { r.HomeLines, r.AwayLines }).ToList();
        return new Averages(
            Avg(t => t.Points),
            results.Average(r => r.Possessions / 2.0),
            teams.Sum(t => t.TwoPointMade) / (double)teams.Sum(t => t.TwoPointAttempted),
            teams.Sum(t => t.ThreePointMade) / (double)teams.Sum(t => t.ThreePointAttempted),
            teams.Sum(t => t.FreeThrowMade) / (double)teams.Sum(t => t.FreeThrowAttempted),
            teams.Sum(t => t.ThreePointAttempted) / (double)teams.Sum(t => t.FieldGoalsAttempted),
            Avg(t => t.FreeThrowAttempted),
            Avg(t => t.Rebounds),
            Avg(t => t.OffensiveRebounds),
            Avg(t => t.Assists),
            Avg(t => t.Turnovers),
            Avg(t => t.Steals),
            Avg(t => t.Blocks),
            Avg(t => t.PersonalFouls),
            results.Count(r => r.Overtimes > 0) / (double)results.Count,
            lines.Average(l => l.Max(p => p.Points) / (double)Math.Max(1, l.Sum(p => p.Points))),
            lines.Average(l => l.Where(p => p.IsStarter).Average(p => p.SecondsPlayed / 60)),
            results.Count(r => r.HomeScore > r.AwayScore) / (double)results.Count);
    }

    [Fact]
    public void Average_matches_produce_plausible_basketball_distributions()
    {
        var a = Run(Matches, i => new MatchSetup(SimulationTeams.Team("Home", 60, i * 2), SimulationTeams.Team("Away", 60, (i * 2) + 1), i));
        output.WriteLine(a.ToString());

        Assert.InRange(a.Points, 72, 88);
        Assert.InRange(a.Possessions, 68, 78);
        Assert.InRange(a.TwoPct, 0.48, 0.57);
        Assert.InRange(a.ThreePct, 0.32, 0.39);
        Assert.InRange(a.FreeThrowPct, 0.70, 0.80);
        Assert.InRange(a.ThreeShare, 0.32, 0.45);
        Assert.InRange(a.FreeThrowAttempts, 16, 26);
        Assert.InRange(a.Rebounds, 32, 40);
        Assert.InRange(a.OffensiveRebounds, 8, 13);
        Assert.InRange(a.Assists, 14, 21);
        Assert.InRange(a.Turnovers, 11, 16);
        Assert.InRange(a.Steals, 5, 9);
        Assert.InRange(a.Blocks, 1.5, 4.5);
        Assert.InRange(a.Fouls, 17, 24);
        Assert.InRange(a.OvertimeShare, 0.02, 0.12);
        Assert.InRange(a.TopScorerShare, 0.18, 0.35); // usage is spread, but a leading scorer exists
        Assert.InRange(a.StarterMinutes, 24, 33);
        Assert.InRange(a.HomeWins, 0.5, 0.65);
    }

    [Fact]
    public void Better_teams_win_most_games_without_winning_all()
    {
        var results = Enumerable.Range(0, 200)
            .Select(i => new MatchSimulation(new MatchSetup(SimulationTeams.Team("Strong", 70, i), SimulationTeams.Team("Weak", 55, i + 1000), i)).RunToEnd())
            .ToList();
        var wins = results.Count(r => r.HomeScore > r.AwayScore) / (double)results.Count;
        output.WriteLine($"Strong team wins {wins:P0}, margin {results.Average(r => r.HomeScore - r.AwayScore):F1}");
        Assert.InRange(wins, 0.8, 0.99);
    }

    [Fact]
    public void Tactical_settings_change_the_game()
    {
        TeamTactics With(OffensiveSystem system, int tempo) => TeamTactics.Default with { OffensiveSystem = system, Offense = new OffensiveSettings { Tempo = tempo } };

        var slow = Run(150, i => new MatchSetup(SimulationTeams.Team("A", 60, i, With(OffensiveSystem.PostOriented, 1)), SimulationTeams.Team("B", 60, i + 1, With(OffensiveSystem.PostOriented, 1)), i));
        var fast = Run(150, i => new MatchSetup(SimulationTeams.Team("A", 60, i, With(OffensiveSystem.Transition, 5)), SimulationTeams.Team("B", 60, i + 1, With(OffensiveSystem.Transition, 5)), i));
        output.WriteLine($"slow {slow}\nfast {fast}");
        Assert.True(fast.Possessions > slow.Possessions + 8, "Tempo and transition must add possessions.");
        Assert.True(fast.Points > slow.Points + 8);

        var fiveOut = Run(150, i => new MatchSetup(SimulationTeams.Team("A", 60, i, With(OffensiveSystem.FiveOut, 3)), SimulationTeams.Team("B", 60, i + 1), i));
        var post = Run(150, i => new MatchSetup(SimulationTeams.Team("A", 60, i, With(OffensiveSystem.PostOriented, 3)), SimulationTeams.Team("B", 60, i + 1), i));
        output.WriteLine($"five-out {fiveOut}\npost {post}");
        Assert.True(fiveOut.ThreeShare > post.ThreeShare + 0.08, "Five-out must shoot more threes than a post offense.");

        // A full-court press forces turnovers.
        var press = Run(150, i => new MatchSetup(SimulationTeams.Team("A", 60, i), SimulationTeams.Team("B", 60, i + 1, TeamTactics.Default with { DefensiveSystem = DefensiveSystem.FullCourtPress }), i));
        var man = Run(150, i => new MatchSetup(SimulationTeams.Team("A", 60, i), SimulationTeams.Team("B", 60, i + 1), i));
        Assert.True(press.Turnovers > man.Turnovers + 1, $"press {press.Turnovers:F1} vs man {man.Turnovers:F1}");
    }
}
