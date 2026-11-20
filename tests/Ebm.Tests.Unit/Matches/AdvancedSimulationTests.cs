using Ebm.Domain.Matches;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Xunit.Abstractions;

namespace Ebm.Tests.Unit.Matches;

/// <summary>Step 29: shot detail, contest, decisions, clutch, transition and advanced statistics (docs/MATCH_ENGINE.md §9–10).</summary>
public sealed class AdvancedSimulationTests(ITestOutputHelper output)
{
    private const int Games = 150;

    private static readonly Lazy<List<MatchSimulation>> Baseline = new(() => Play(200, i => Pair(i)));

    private static void Set(SheetPlayer player, params (PlayerAttribute Attribute, int Value)[] values)
    {
        var attributes = player.Player.Attributes;
        foreach (var (attribute, value) in values)
        {
            attributes = attributes.With(attribute, value);
        }

        player.Player.UpdateAttributes(attributes);
    }

    private static void Set(SheetPlayer player, params (PlayerTendency Tendency, int Value)[] values)
    {
        var tendencies = player.Player.Tendencies;
        foreach (var (tendency, value) in values)
        {
            tendencies = tendencies.With(tendency, value);
        }

        player.Player.UpdateTendencies(tendencies);
    }

    private static List<MatchSimulation> Play(int games, Func<int, (TeamSheet Home, TeamSheet Away)> teams) =>
    [
        .. Enumerable.Range(0, games).Select(i =>
        {
            var (home, away) = teams(i);
            var sim = new MatchSimulation(new MatchSetup(home, away, i));
            sim.RunToEnd();
            return sim;
        }),
    ];

    private static (TeamSheet Home, TeamSheet Away) Pair(int i) =>
        (SimulationTeams.Team("Home", 60, i * 2), SimulationTeams.Team("Away", 60, (i * 2) + 1));

    private static IEnumerable<ShotRecord> Shots(IEnumerable<MatchSimulation> games, TeamSide? side = null) =>
        games.SelectMany(g => (side is { } s ? g.Lines(s) : g.Lines(TeamSide.Home).Concat(g.Lines(TeamSide.Away))).SelectMany(l => l.Shots));

    private static double Pct(IEnumerable<ShotRecord> shots)
    {
        var list = shots.ToList();
        return list.Count == 0 ? 0 : list.Count(s => s.Made) / (double)list.Count;
    }

    [Fact]
    public void Shot_charts_match_the_recorded_attempts()
    {
        foreach (var game in Baseline.Value.Take(60))
        {
            foreach (var line in game.Lines(TeamSide.Home).Concat(game.Lines(TeamSide.Away)))
            {
                var box = line.ToBoxScore();
                box.Validate(); // throws when the chart and the box score disagree
                var shots = box.Shots;
                Assert.Equal(line.Shots, shots);
                Assert.Equal(line.TwoPointAttempted + line.ThreePointAttempted, shots.Count);
                Assert.Equal(line.ThreePointMade, shots.Count(s => s.IsThree && s.Made));
                Assert.Equal((2 * line.TwoPointMade) + (3 * line.ThreePointMade), shots.Where(s => s.Made).Sum(s => s.Points));
                Assert.All(shots, s =>
                {
                    Assert.True(ShotZones.IsOnCourt(s.X, s.Y));
                    Assert.Equal(ShotZones.IsThree(s.Zone), s.IsThree);
                    Assert.InRange(s.Period, 1, game.Period);
                    Assert.False(s.Clutch && (s.Period < 4 || s.Clock > 300));
                    Assert.False(s.Blocked && s.Made);
                });
            }
        }
    }

    [Fact]
    public void Shot_chart_encoding_round_trips_and_rejects_mismatches()
    {
        ShotRecord[] shots =
        [
            new(1, 600, 0, 12, true, ShotContest.Open, true, false, false, false),
            new(4, 35, -70, 10, false, ShotContest.Tight, false, false, true, true),
            new(5, 300, 10, 120, true, ShotContest.Contested, false, true, true, false),
        ];
        Assert.Equal(shots, ShotChart.Decode(ShotChart.Encode(shots)));
        Assert.Empty(ShotChart.Decode(null));
        Assert.Equal(ShotZone.RestrictedArea, shots[0].Zone);
        Assert.Equal(ShotZone.CornerThreeLeft, shots[1].Zone);
        Assert.Equal(ShotZone.TopThree, shots[2].Zone);
        Assert.Equal(ShotZone.Paint, ShotZones.Classify(20, 50));
        Assert.Equal(ShotZone.MidRangeRight, ShotZones.Classify(50, 20));
        Assert.Equal(ShotZone.WingThreeRight, ShotZones.Classify(55, 70));

        var box = new BoxScore { TwoPointMade = 1, TwoPointAttempted = 1, ThreePointMade = 1, ThreePointAttempted = 2, ShotData = ShotChart.Encode(shots) };
        box.Validate();
        var wrong = new BoxScore { TwoPointMade = 1, TwoPointAttempted = 2, ThreePointMade = 1, ThreePointAttempted = 2, ShotData = ShotChart.Encode(shots) };
        Assert.Throws<Ebm.Domain.Common.DomainValidationException>(wrong.Validate);
        Assert.Throws<Ebm.Domain.Common.DomainValidationException>(() => ShotChart.Decode("not base64!"));
        var summary = ShotChart.Summarize(shots);
        Assert.Equal(10, summary.Count);
        Assert.Equal(3, summary.Sum(z => z.Attempted));
        Assert.Equal(5, summary.Sum(z => z.Points));
    }

    [Fact]
    public void Advanced_statistics_are_mathematically_consistent()
    {
        foreach (var game in Baseline.Value.Take(80))
        {
            var result = game.Result();
            var home = result.HomeLines.Select(l => l.ToBoxScore()).ToList();
            var away = result.AwayLines.Select(l => l.ToBoxScore()).ToList();

            // On-court accounting: five players on court for every point and possession.
            Assert.Equal(5 * result.HomeScore, home.Sum(b => b.PointsFor));
            Assert.Equal(5 * result.AwayScore, home.Sum(b => b.PointsAgainst));
            Assert.Equal(5 * (result.HomeScore - result.AwayScore), home.Sum(b => b.PlusMinus));
            Assert.Equal(-home.Sum(b => b.PlusMinus), away.Sum(b => b.PlusMinus));
            Assert.Equal(5 * result.Possessions, home.Sum(b => b.OffensivePossessions) + away.Sum(b => b.OffensivePossessions));
            Assert.Equal(home.Sum(b => b.OffensivePossessions), away.Sum(b => b.DefensivePossessions));
            Assert.Equal(TeamStatistics.From(away).FieldGoalsAttempted, home.Sum(b => b.DefendedAttempted));
            Assert.Equal(TeamStatistics.From(away).FieldGoalsMade, home.Sum(b => b.DefendedMade));

            // Team ratings mirror each other; usage, rebound and assist shares add up.
            var h = AdvancedStatistics.Team(home, away);
            var a = AdvancedStatistics.Team(away, home);
            Assert.Equal(h.OffensiveRating, a.DefensiveRating, 9);
            Assert.Equal(h.NetRating, -a.NetRating, 9);
            var lines = AdvancedStatistics.Players(home, away);
            var share = home.Sum(b => b.Minutes) / 5.0;
            var played = home.Zip(lines).Where(x => x.First.Minutes > 0).ToList();
            static double Plays(BoxScore b) => b.TwoPointAttempted + b.ThreePointAttempted + (0.44 * b.FreeThrowAttempted) + b.Turnovers;
            Assert.Equal(played.Sum(x => Plays(x.First)) / home.Sum(Plays), played.Sum(x => x.First.Minutes * x.Second.Usage) / share, 6);
            Assert.Equal(played.Sum(x => x.First.Rebounds) / (double)(home.Sum(b => b.Rebounds) + away.Sum(b => b.Rebounds)), played.Sum(x => x.First.Minutes * x.Second.Rebound) / share, 6);
            Assert.All(home.Zip(lines), x => Assert.Equal(x.First.PlusMinus, x.Second.PlusMinus));
            Assert.All(lines, l => Assert.InRange(l.TrueShooting, 0, 1.5));

            // Turnover causes add up to the box score (shot-clock violations are team turnovers).
            var causes = result.HomeAnalytics.Turnovers;
            Assert.Equal(home.Sum(b => b.Turnovers), causes.Where(c => c.Key != TurnoverCause.ShotClockViolation).Sum(c => c.Value));
            Assert.Equal(home.Sum(b => b.Steals), result.HomeAnalytics.Steals);
            Assert.InRange(result.HomeAnalytics.FastBreakPoints + result.HomeAnalytics.SecondChancePoints, 0, result.HomeScore);
        }

        // Known values: 20 points on 12 FGA (2 threes made of 5) and 6 FTA.
        var box = new BoxScore { Minutes = 30, TwoPointMade = 5, TwoPointAttempted = 7, ThreePointMade = 2, ThreePointAttempted = 5, FreeThrowMade = 4, FreeThrowAttempted = 6, Turnovers = 3 };
        Assert.Equal(20 / (2 * (12 + (0.44 * 6))), AdvancedStatistics.TrueShooting(box.Points, 12, 6), 9);
        Assert.Equal((7 + 1) / 12.0, AdvancedStatistics.EffectiveFieldGoal(7, 2, 12), 9);
        Assert.Equal(3 / (12 + (0.44 * 6) + 3), AdvancedStatistics.TurnoverRate(3, 12, 6), 9);
    }

    [Fact]
    public void Large_scale_shot_profile_is_plausible()
    {
        var games = Baseline.Value;
        var shots = Shots(games).ToList();
        double Share(Func<ShotRecord, bool> f) => shots.Count(f) / (double)shots.Count;
        var twos = shots.Where(s => !s.IsThree).ToList();
        var restricted = twos.Count(s => s.Zone == ShotZone.RestrictedArea) / (double)twos.Count;
        var corner = shots.Where(s => s.Zone is ShotZone.CornerThreeLeft or ShotZone.CornerThreeRight).ToList();
        var above = shots.Where(s => s.IsThree && s.Zone is not (ShotZone.CornerThreeLeft or ShotZone.CornerThreeRight)).ToList();
        var analytics = games.SelectMany(g => new[] { g.Analytics(TeamSide.Home), g.Analytics(TeamSide.Away) }).ToList();
        double PerTeam(Func<TeamAnalytics, double> f) => analytics.Average(f);
        output.WriteLine($"Open {Share(s => s.Contest == ShotContest.Open):P1} ({Pct(shots.Where(s => s.Contest == ShotContest.Open)):P1}), contested {Share(s => s.Contest == ShotContest.Contested):P1} ({Pct(shots.Where(s => s.Contest == ShotContest.Contested)):P1}), tight {Share(s => s.Contest == ShotContest.Tight):P1} ({Pct(shots.Where(s => s.Contest == ShotContest.Tight)):P1})");
        output.WriteLine($"Restricted area {restricted:P1} of twos ({Pct(twos.Where(s => s.Zone == ShotZone.RestrictedArea)):P1}), paint {Pct(twos.Where(s => s.Zone == ShotZone.Paint)):P1}, mid-range {Pct(twos.Where(s => !ShotZones.IsInside(s.Zone))):P1}");
        output.WriteLine($"Corner threes {corner.Count / (double)(corner.Count + above.Count):P1} of threes ({Pct(corner):P1}), above the break {Pct(above):P1}");
        output.WriteLine($"Assisted makes {shots.Count(s => s.Assisted) / (double)shots.Count(s => s.Made):P1}, blocked {Share(s => s.Blocked):P1}, clutch shots {Share(s => s.Clutch):P1}");
        output.WriteLine($"Per team: fast breaks {PerTeam(a => a.FastBreakPossessions):F1} ({PerTeam(a => a.FastBreakPoints):F1} pts), secondary breaks {PerTeam(a => a.SecondaryBreakPossessions):F1}, second-chance points {PerTeam(a => a.SecondChancePoints):F1}, passed-up shots {PerTeam(a => a.PassedUpShots):F1}");
        output.WriteLine("Turnovers: " + string.Join(", ", Enum.GetValues<TurnoverCause>().Select(c => $"{c} {PerTeam(a => a.Turnovers[c]):F2}")));

        Assert.InRange(Share(s => s.Contest == ShotContest.Open), 0.2, 0.45);
        Assert.InRange(Share(s => s.Contest == ShotContest.Tight), 0.15, 0.4);
        Assert.True(Pct(shots.Where(s => s.Contest == ShotContest.Open)) > Pct(shots.Where(s => s.Contest == ShotContest.Contested)) + 0.02);
        Assert.True(Pct(shots.Where(s => s.Contest == ShotContest.Contested)) > Pct(shots.Where(s => s.Contest == ShotContest.Tight)) + 0.02);
        Assert.InRange(restricted, 0.35, 0.7);
        Assert.True(Pct(twos.Where(s => s.Zone == ShotZone.RestrictedArea)) > Pct(twos.Where(s => s.Zone == ShotZone.Paint)) + 0.05);
        Assert.True(Pct(corner) > Pct(above) + 0.01);
        Assert.InRange(corner.Count / (double)(corner.Count + above.Count), 0.15, 0.35);
        Assert.InRange(PerTeam(a => a.FastBreakPoints), 5, 18);
        Assert.InRange(PerTeam(a => a.SecondChancePoints), 6, 16);
        Assert.InRange(PerTeam(a => a.Turnovers[TurnoverCause.BadPass]), 3, 8);
        Assert.InRange(PerTeam(a => a.Turnovers[TurnoverCause.LostBall]), 2, 6);
        Assert.InRange(PerTeam(a => a.Turnovers[TurnoverCause.OffensiveFoul]), 0.5, 2.5);
    }

    [Fact]
    public void Elite_defenders_contest_more_shots_tightly()
    {
        double TightShare(int defense) => Shots(Play(Games, i =>
        {
            var (home, away) = Pair(i);
            foreach (var p in home.Players)
            {
                Set(p, (PlayerAttribute.PerimeterDefense, defense), (PlayerAttribute.InteriorDefense, defense), (PlayerAttribute.DefensivePositioning, defense));
            }

            return (home, away);
        }), TeamSide.Away).Average(s => s.Contest == ShotContest.Tight ? 1.0 : 0);
        var weak = TightShare(30);
        var elite = TightShare(85);
        output.WriteLine($"Tight contests: weak defenders {weak:P1}, elite {elite:P1}");
        Assert.True(elite > weak + 0.12);
    }

    [Fact]
    public void Player_tendencies_and_decisions_shape_shots_and_fouls()
    {
        // Away shooting guard: isolation vs pass-first tendencies (same ratings), then poor vs good decisions.
        List<MatchSimulation> Run(int isolation, int decisions, int drive, int strength) => Play(Games, i =>
        {
            var (home, away) = Pair(i);
            Set(away.Players[1], (PlayerTendency.Isolation, isolation), (PlayerTendency.PassFirst, 100 - isolation), (PlayerTendency.DriveToBasket, drive));
            Set(away.Players[1], (PlayerAttribute.DecisionMaking, decisions), (PlayerAttribute.Strength, strength));
            return (home, away);
        });
        var iso = Run(95, 60, 50, 50);
        var team = Run(5, 60, 50, 50);
        var poor = Run(50, 15, 50, 50);
        var good = Run(50, 95, 50, 50);
        var slasher = Run(50, 60, 95, 90);
        var jumper = Run(50, 60, 5, 30);
        double Of(List<MatchSimulation> g, Func<PlayerLine, double> f) => g.Average(s => f(s.Lines(TeamSide.Away)[1]));
        double Share(List<MatchSimulation> g) => g.Average(s => (s.Lines(TeamSide.Away)[1].TwoPointAttempted + s.Lines(TeamSide.Away)[1].ThreePointAttempted) / (double)s.Lines(TeamSide.Away).Sum(l => l.TwoPointAttempted + l.ThreePointAttempted));
        double Tight(List<MatchSimulation> g) => g.SelectMany(s => s.Lines(TeamSide.Away)[1].Shots).Average(s => s.Contest == ShotContest.Tight ? 1.0 : 0);
        double Assisted(List<MatchSimulation> g) => g.SelectMany(s => s.Lines(TeamSide.Away)[1].Shots.Where(x => x.Made)).Average(s => s.Assisted ? 1.0 : 0);
        double PassedUp(List<MatchSimulation> g) => g.Average(s => s.Analytics(TeamSide.Away).PassedUpShots);
        output.WriteLine($"Isolation scorer: shot share {Share(iso):P1}, assisted {Assisted(iso):P1}; pass-first {Share(team):P1}, {Assisted(team):P1}");
        output.WriteLine($"Decisions poor/good: tight {Tight(poor):P1} / {Tight(good):P1}, passed-up shots {PassedUp(poor):F1} / {PassedUp(good):F1}");
        output.WriteLine($"Free throws: slasher {Of(slasher, l => l.FreeThrowAttempted):F1}, jump shooter {Of(jumper, l => l.FreeThrowAttempted):F1}");
        Assert.True(Share(iso) > Share(team) + 0.02);
        Assert.True(Assisted(iso) < Assisted(team) - 0.05);
        Assert.True(Tight(poor) > Tight(good) + 0.05);
        Assert.True(PassedUp(good) > PassedUp(poor));
        Assert.True(Of(slasher, l => l.FreeThrowAttempted) > Of(jumper, l => l.FreeThrowAttempted) * 1.3);
    }

    [Fact]
    public void Clutch_players_shoot_better_in_clutch_time()
    {
        List<MatchSimulation> Run(int calm) => Play(300, i =>
        {
            var (home, away) = Pair(i);
            foreach (var p in away.Players)
            {
                Set(p, (PlayerAttribute.Concentration, calm), (PlayerAttribute.Consistency, calm), (PlayerAttribute.Leadership, calm));
            }

            return (home, away);
        });
        var coolAll = Shots(Run(95), TeamSide.Away).ToList();
        var nervousAll = Shots(Run(15), TeamSide.Away).ToList();
        var cool = coolAll.Where(s => s.Clutch).ToList();
        var nervous = nervousAll.Where(s => s.Clutch).ToList();
        output.WriteLine($"Other FG%: calm {Pct(coolAll.Where(s => !s.Clutch)):P1}, nervous {Pct(nervousAll.Where(s => !s.Clutch)):P1}");
        output.WriteLine($"Clutch FG%: calm {Pct(cool):P1} ({cool.Count}), nervous {Pct(nervous):P1} ({nervous.Count})");
        Assert.True(cool.Count > 300 && nervous.Count > 300);

        // Clutch gain over the same team's other shots (team quality and game state are the same on both sides).
        Assert.True(Pct(cool) - Pct(coolAll.Where(s => !s.Clutch)) > Pct(nervous) - Pct(nervousAll.Where(s => !s.Clutch)) + 0.04);
    }

    [Fact]
    public void Fast_teams_run_more_and_long_rebounds_go_to_guards()
    {
        List<MatchSimulation> Run(int speed) => Play(Games, i =>
        {
            var (home, away) = Pair(i);
            foreach (var p in away.Players)
            {
                Set(p, (PlayerAttribute.Speed, speed));
            }

            return (home, away);
        });
        double FastBreaks(List<MatchSimulation> g) => g.Average(s => s.Analytics(TeamSide.Away).FastBreakPossessions);
        var fast = FastBreaks(Run(90));
        var slow = FastBreaks(Run(30));
        output.WriteLine($"Fast breaks: fast team {fast:F1}, slow team {slow:F1}");
        Assert.True(fast > slow + 1.5);

        // Guards (slots PG/SG start the game) collect a larger share of rebounds than of a team's height would suggest
        // when the team shoots more threes: compare guards' defensive rebounds against five-out and post offenses.
        double GuardRebounds(Ebm.Domain.Tactics.OffensiveSystem system) => Play(Games, i =>
        {
            var (home, away) = Pair(i);
            return (home, away with { Tactics = away.Tactics with { OffensiveSystem = system } });
        }).Average(s => s.Lines(TeamSide.Home).Where(l => l.StartingPosition is Position.PointGuard or Position.ShootingGuard).Sum(l => l.DefensiveRebounds)
            / (double)Math.Max(1, s.Lines(TeamSide.Home).Sum(l => l.DefensiveRebounds)));
        var vsShooters = GuardRebounds(Ebm.Domain.Tactics.OffensiveSystem.FiveOut);
        var vsPost = GuardRebounds(Ebm.Domain.Tactics.OffensiveSystem.PostOriented);
        output.WriteLine($"Guards' share of defensive rebounds: against five-out {vsShooters:P1}, against post {vsPost:P1}");
        Assert.True(vsShooters > vsPost + 0.01);
    }
}
