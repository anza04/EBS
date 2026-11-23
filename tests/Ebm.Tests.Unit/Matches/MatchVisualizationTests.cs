using System.Text.Json;
using Ebm.Domain.Matches;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Matches.Visualization;
using Ebm.Domain.Tactics;

namespace Ebm.Tests.Unit.Matches;

/// <summary>Step 30: the 2D court follows the engine's events and never changes the match (docs/MATCH_VISUALIZATION.md).</summary>
public sealed class MatchVisualizationTests
{
    private static MatchSetup Setup(int seed) =>
        new(SimulationTeams.Team("Home", 60, seed * 2), SimulationTeams.Team("Away", 60, (seed * 2) + 1, players: seed % 3 == 0 ? 8 : 12), seed);

    private static (MatchSimulation Sim, CourtTracker Tracker) Watch(int seed, SimulationMode mode = SimulationMode.Commentary, MatchSetup? setup = null)
    {
        var sim = new MatchSimulation(setup ?? Setup(seed), mode);
        var tracker = CourtTracker.For(sim);
        while (!sim.IsFinished)
        {
            sim.Step();
            tracker.Update(sim.Events);
        }

        return (sim, tracker);
    }

    [Fact]
    public void Every_event_is_placed_and_line_ups_stay_in_sync_with_the_engine()
    {
        for (var seed = 0; seed < 30; seed++)
        {
            var sim = new MatchSimulation(Setup(seed));
            var tracker = CourtTracker.For(sim);
            while (!sim.IsFinished)
            {
                sim.Step();
                tracker.Update(sim.Events);
                foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
                {
                    Assert.Equal(
                        sim.OnCourt(side).Select(l => l.Player.Id.Value).Order(),
                        tracker.OnCourt(side).Select(p => p.Value).Order());
                }
            }

            Assert.Equal(sim.Events.Count, tracker.Frames.Count);
            Assert.All(tracker.Frames.Select((f, i) => (f, i)), x =>
            {
                Assert.Equal(x.i, x.f.Index);
                Assert.Same(sim.Events[x.i], x.f.Event);
            });
        }
    }

    [Fact]
    public void Players_and_ball_follow_the_events()
    {
        var (_, tracker) = Watch(4);
        foreach (var frame in tracker.Frames)
        {
            Assert.All(frame.Players, p =>
            {
                Assert.InRange(p.Position.X, 0, CourtTracker.Length);
                Assert.InRange(p.Position.Y, 0, CourtTracker.Width);
            });
            Assert.Equal(frame.Players.Count, frame.Players.Select(p => p.Player).Distinct().Count());
            if (frame.BallHolder is { } holder)
            {
                Assert.Equal(frame.Ball, frame.Find(holder)!.Position);
            }

            var e = frame.Event;
            switch (e.Kind)
            {
                case MatchEventKind.FieldGoalMade or MatchEventKind.FieldGoalMissed:
                    var shot = e.Shot!.Value;
                    var shooter = frame.Find(e.Player!.Value)!;
                    Assert.Equal(e.Side, shooter.Side);
                    Assert.InRange(CourtTracker.ShotSpot(e.Side!.Value, shot).DistanceTo(shooter.Position), 0, 0.11);
                    Assert.Equal(shooter.Position, frame.BallFrom);
                    Assert.Equal(CourtTracker.Rim(e.Side.Value, e.Period), frame.Ball);
                    Assert.True(frame.BallInAir);

                    // The shot distance on the court is the one the engine recorded.
                    Assert.Equal(shot.Distance, shooter.Position.DistanceTo(frame.Ball), 0.15);
                    break;
                case MatchEventKind.OffensiveRebound or MatchEventKind.DefensiveRebound:
                    Assert.Equal(e.Player, frame.BallHolder);
                    Assert.Equal(e.Side, frame.Offense);
                    break;
                case MatchEventKind.Turnover when e.OtherPlayer is { } thief:
                    Assert.Equal(thief, frame.BallHolder);
                    break;
                case MatchEventKind.FreeThrowMade or MatchEventKind.FreeThrowMissed:
                    Assert.Equal(CourtTracker.Rim(e.Side!.Value, e.Period), frame.Ball);
                    break;
                case MatchEventKind.Substitution:
                    Assert.NotNull(frame.Find(e.Player!.Value));
                    if (e.OtherPlayer is { } leaving)
                    {
                        Assert.Null(frame.Find(leaving));
                    }

                    break;
            }
        }

        Assert.Contains(tracker.Frames, f => f.Action == CourtAction.Shot);
        Assert.Contains(tracker.Frames, f => f.Action == CourtAction.Substitution);
    }

    [Fact]
    public void Teams_attack_the_other_basket_after_half_time()
    {
        Assert.True(CourtTracker.AttacksRight(TeamSide.Home, 1));
        Assert.False(CourtTracker.AttacksRight(TeamSide.Away, 2));
        Assert.False(CourtTracker.AttacksRight(TeamSide.Home, 3));
        Assert.True(CourtTracker.AttacksRight(TeamSide.Away, 5));
        var (_, tracker) = Watch(2);
        var homeShots = tracker.Frames.Where(f => f.Action == CourtAction.Shot && f.Offense == TeamSide.Home).ToList();
        Assert.All(homeShots.Where(f => f.Event.Period <= 2), f => Assert.True(f.Ball.X > CourtTracker.Length / 2));
        Assert.All(homeShots.Where(f => f.Event.Period > 2), f => Assert.True(f.Ball.X < CourtTracker.Length / 2));
    }

    [Fact]
    public void Simulation_results_are_identical_with_or_without_the_visualization()
    {
        for (var seed = 0; seed < 10; seed++)
        {
            var setup = Setup(seed);
            var plain = new MatchSimulation(setup).RunToEnd();

            var sim = new MatchSimulation(setup);
            var tracker = CourtTracker.For(sim);
            var playback = new CourtPlayback { Speed = 4 };
            while (!sim.IsFinished)
            {
                sim.Step();
                playback.Enqueue(tracker.Update(sim.Events));
                playback.Tick(0.3);
                if (sim.Events.Count % 7 == 0)
                {
                    playback.Pause();
                    playback.Tick(1);
                    playback.Resume();
                }
            }

            var watched = sim.Result();
            Assert.Equal((plain.HomeScore, plain.AwayScore, plain.Possessions), (watched.HomeScore, watched.AwayScore, watched.Possessions));
            Assert.Equal(
                plain.HomeLines.Concat(plain.AwayLines).Select(l => JsonSerializer.Serialize(l.ToBoxScore())),
                watched.HomeLines.Concat(watched.AwayLines).Select(l => JsonSerializer.Serialize(l.ToBoxScore())));
        }

        // The events (and therefore the frames) are the same in every mode.
        var shared = Setup(7);
        var commentary = Watch(7, SimulationMode.Commentary, shared);
        var live = Watch(7, SimulationMode.Live, shared);
        Assert.Equal(commentary.Sim.Events, live.Sim.Events);
        Assert.Equal(
            commentary.Tracker.Frames.Select(f => (f.Action, f.Ball, f.BallHolder, string.Join(";", f.Players))),
            live.Tracker.Frames.Select(f => (f.Action, f.Ball, f.BallHolder, string.Join(";", f.Players))));
    }

    [Fact]
    public void Playback_can_be_paused_resumed_and_skipped()
    {
        var (_, tracker) = Watch(1);
        var frames = tracker.Frames.Take(20).ToList();
        var playback = new CourtPlayback();
        playback.Enqueue(frames);
        Assert.Same(frames[0], playback.Current);
        Assert.Equal(19, playback.Pending);

        playback.Tick(CourtPlayback.BaseDuration(frames[0].Action) / 2);
        Assert.InRange(playback.Progress, 0.45, 0.55);

        playback.Pause();
        var (current, progress) = (playback.Current, playback.Progress);
        Assert.False(playback.Tick(10));
        Assert.Same(current, playback.Current);
        Assert.Equal(progress, playback.Progress);
        Assert.False(playback.IsPlaying);

        playback.Resume();
        Assert.True(playback.Tick(CourtPlayback.BaseDuration(frames[0].Action)));
        Assert.Same(frames[1], playback.Current);

        // Double speed halves the time a frame takes.
        playback.Tick(CourtPlayback.Duration(frames[1]) - playback.Progress * CourtPlayback.Duration(frames[1]) - 0.0001);
        Assert.Same(frames[1], playback.Current);
        playback.Speed = 2;
        playback.Tick(0.001);
        Assert.Same(frames[2], playback.Current);

        playback.SkipToEnd();
        Assert.Same(frames[^1], playback.Current);
        Assert.Equal(1, playback.Progress);
        Assert.Equal(0, playback.Pending);
        Assert.False(playback.IsPlaying);
        Assert.All(playback.Players(), p => Assert.Equal(p.Player.Position, p.Position));

        playback.Clear();
        Assert.Null(playback.Current);
        Assert.Empty(playback.Players());
    }

    [Fact]
    public void Shots_arc_and_the_drawing_is_deterministic()
    {
        var setup = Setup(3);
        var (sim, tracker) = Watch(3, setup: setup);
        var again = CourtTracker.For(new MatchSimulation(setup));
        again.Update(sim.Events);
        Assert.Equal(
            tracker.Frames.Select(f => string.Join(";", f.Players) + f.Ball),
            again.Frames.Select(f => string.Join(";", f.Players) + f.Ball));

        var shot = tracker.Frames.First(f => f.Action == CourtAction.Shot && f.Lead.Count > 0);
        var playback = new CourtPlayback();
        playback.Enqueue([shot]);
        // The lead-up ends with the shooter holding the ball on his spot; then it flies in an arc.
        var (released, arced) = (false, false);
        while (playback.IsPlaying)
        {
            playback.Tick(1 / 60.0);
            released |= playback.Holder == shot.Event.Player && shot.BallFrom.DistanceTo(playback.Players().First(p => p.Player.Player == shot.Event.Player).Position) < 0.05;
            arced |= released && playback.Holder is null && playback.Ball().Height > 2;
        }

        Assert.True(released && arced);
    }

    [Fact]
    public void Shots_finish_as_made_missed_or_blocked_and_captions_wait_for_the_finish()
    {
        var (_, tracker) = Watch(4);
        var results = new HashSet<ShotResult>();
        foreach (var shot in tracker.Frames.Where(f => f.Action == CourtAction.Shot && f.Lead.Count > 0).Take(60))
        {
            var e = shot.Event;
            var expected = e.Kind == MatchEventKind.FieldGoalMade ? ShotResult.Made : e.Shot!.Value.Blocked ? ShotResult.Blocked : ShotResult.Missed;
            var playback = new CourtPlayback();
            playback.Enqueue([tracker.Frames[shot.Index - 1]]);
            playback.SkipToEnd();
            playback.Enqueue([shot]);
            playback.Tick(0.001);
            Assert.Same(shot, playback.Current);
            Assert.Same(tracker.Frames[shot.Index - 1], playback.Previous);

            ShotFinish? finish = null;
            while (playback.IsPlaying)
            {
                // Nothing tells the outcome before the ball gets there: no finish, no play-by-play, no score.
                if (!playback.Revealed)
                {
                    Assert.Null(playback.Finish);
                    Assert.Equal(!playback.EventStarted, CourtPlays.Describe(shot, _ => "X", playback.EventStarted) == CourtPlays.Name(shot.Play));
                }

                finish ??= playback.Finish;
                playback.Tick(1 / 60.0);
            }

            Assert.Equal(expected, finish!.Value.Result);
            Assert.True(playback.Revealed);
            results.Add(expected);
            var (end, height) = playback.Ball();
            var rim = CourtTracker.Rim(e.Side!.Value, e.Period);
            switch (expected)
            {
                case ShotResult.Made:
                    Assert.Equal(rim, end);
                    Assert.Equal(0, height, 3); // through the net and down
                    Assert.Equal(e.Points, finish.Value.Points);
                    break;
                case ShotResult.Missed:
                    Assert.InRange(end.DistanceTo(rim), 1.0, 1.7); // off the rim
                    break;
                case ShotResult.Blocked:
                    Assert.True(end.DistanceTo(rim) > shot.BallFrom.DistanceTo(rim) - 1.2); // knocked away, never near the rim
                    break;
            }
        }

        Assert.Contains(ShotResult.Made, results);
        Assert.Contains(ShotResult.Missed, results);

        // The rebound picks the ball up where the miss left it.
        var miss = tracker.Frames.First(f => f.Event.Kind == MatchEventKind.FieldGoalMissed && !f.Event.Shot!.Value.Blocked && tracker.Frames[f.Index + 1].Action == CourtAction.Rebound);
        var replay = new CourtPlayback();
        replay.Enqueue([miss]);
        replay.SkipToEnd();
        var left = replay.Ball().Position;
        replay.Enqueue([tracker.Frames[miss.Index + 1]]);
        replay.Tick(0.0001);
        Assert.Same(tracker.Frames[miss.Index + 1], replay.Current);
        Assert.InRange(replay.Ball().Position.DistanceTo(left), 0, 0.01);
    }

    [Fact]
    public void Possessions_are_scripted_into_the_event_layout()
    {
        var scripted = 0;
        for (var seed = 0; seed < 6; seed++)
        {
            var (_, tracker) = Watch(seed);
            foreach (var frame in tracker.Frames.Where(f => f.Lead.Count > 0))
            {
                scripted++;
                var e = frame.Event;
                CourtFrame? before = frame.Index > 0 ? tracker.Frames[frame.Index - 1] : null;
                var from = frame.Players.ToDictionary(p => p.Player, p => before?.Find(p.Player)?.Position ?? frame.Lead[0].Find(p.Player)!.Position);
                foreach (var key in frame.Lead)
                {
                    Assert.True(key.Duration > 0);
                    Assert.Equal(frame.Players.Select(p => p.Player), key.Players.Select(p => p.Player));
                    Assert.All(key.Players, p =>
                    {
                        Assert.InRange(p.Position.X, 0, CourtTracker.Length);
                        Assert.InRange(p.Position.Y, 0, CourtTracker.Width);

                        // Nobody runs faster than the cap (durations are rounded to milliseconds).
                        Assert.True(from[p.Player].DistanceTo(p.Position) <= (CourtTracker.MaxRunSpeed * key.Duration) + 0.01);
                    });
                    if (key.BallHolder is { } holder && key.Flight is BallFlight.Held or BallFlight.Pass)
                    {
                        Assert.Equal(key.Ball, key.Find(holder)!.Position);
                        Assert.Equal(frame.Offense, key.Find(holder)!.Side);
                    }

                    from = key.Players.ToDictionary(p => p.Player, p => p.Position);
                }

                // The last key hands over to the event: the offense in place and the ball with its player.
                var last = frame.Lead[^1];
                Assert.All(frame.Players.Where(p => p.Side == frame.Offense), p => Assert.Equal(p.Position, last.Find(p.Player)!.Position));
                if (frame.Action == CourtAction.Shot)
                {
                    Assert.Equal(e.Player, last.BallHolder);
                    Assert.Equal(frame.BallFrom, last.Ball);
                    if (e.Kind == MatchEventKind.FieldGoalMade && e.OtherPlayer is { } passer)
                    {
                        Assert.Equal(BallFlight.Pass, last.Flight); // the assist is drawn
                        Assert.Equal(passer, frame.Lead[^2].BallHolder);
                    }
                }
            }

            // Every half-court shot has its possession; only putbacks are drawn on their own.
            Assert.All(tracker.Frames.Where(f => f.Action == CourtAction.Shot), f => Assert.True(f.Lead.Count > 0 || f.Play == CourtPlay.Putback));
        }

        Assert.True(scripted > 500);
    }

    [Fact]
    public void Plays_vary_and_follow_the_offensive_system()
    {
        static MatchSetup Systems(int seed, OffensiveSystem home, OffensiveSystem away) => new(
            SimulationTeams.Team("Home", 60, seed * 2, TeamTactics.Default with { OffensiveSystem = home }),
            SimulationTeams.Team("Away", 60, (seed * 2) + 1, TeamTactics.Default with { OffensiveSystem = away }),
            seed);

        var plays = new List<(TeamSide? Side, CourtPlay Play)>();
        for (var seed = 0; seed < 8; seed++)
        {
            var (_, tracker) = Watch(seed, setup: Systems(seed, OffensiveSystem.PickAndRoll, OffensiveSystem.PostOriented));
            plays.AddRange(tracker.Frames.Where(f => f.Action == CourtAction.Shot).Select(f => (f.Offense, f.Play)));
        }

        Assert.True(plays.Select(p => p.Play).Distinct().Count() >= 10);
        double Share(TeamSide side, params CourtPlay[] kinds) =>
            plays.Count(p => p.Side == side && kinds.Contains(p.Play)) / (double)plays.Count(p => p.Side == side);
        Assert.True(Share(TeamSide.Home, CourtPlay.PickAndRoll, CourtPlay.PickAndPop) > Share(TeamSide.Away, CourtPlay.PickAndRoll, CourtPlay.PickAndPop));
        Assert.True(Share(TeamSide.Away, CourtPlay.PostUp) > Share(TeamSide.Home, CourtPlay.PostUp));

        // Long possessions take longer to show than fast breaks.
        var (_, one) = Watch(5);
        double Lead(CourtPlay play) => one.Frames.Where(f => f.Play == play).Select(f => f.Lead.Sum(k => k.Duration)).DefaultIfEmpty(0).Average();
        Assert.True(Lead(CourtPlay.LateClock) > Lead(CourtPlay.FastBreak));

        var shot = one.Frames.First(f => f.Action == CourtAction.Shot && f.Play != CourtPlay.None);
        var caption = CourtPlays.Describe(shot, _ => "Rossi")!;
        Assert.StartsWith(CourtPlays.Name(shot.Play), caption, StringComparison.Ordinal);
        Assert.Contains("→ Rossi · " + CourtPlays.ShotText(shot.Event.Shot!.Value), caption, StringComparison.Ordinal);
    }

    [Fact]
    public void Playback_moves_players_and_ball_smoothly()
    {
        var (_, tracker) = Watch(2);
        var playback = new CourtPlayback();
        playback.Enqueue(tracker.Frames.Take(400));
        var players = new Dictionary<Ebm.Domain.Players.PlayerId, CourtPoint>();
        var (ball, frame) = (playback.Ball().Position, playback.Current);
        var (maxPlayer, maxBall) = (0.0, 0.0);
        while (playback.IsPlaying)
        {
            playback.Tick(1 / 60.0);
            foreach (var p in playback.Players())
            {
                if (!p.Entering && players.TryGetValue(p.Player.Player, out var was))
                {
                    maxPlayer = Math.Max(maxPlayer, was.DistanceTo(p.Position));
                }

                players[p.Player.Player] = p.Position;
            }

            var now = playback.Ball().Position;
            if (playback.Current == frame)
            {
                maxBall = Math.Max(maxBall, ball.DistanceTo(now));
            }

            if (playback.Holder is { } holder)
            {
                Assert.InRange(playback.Players().First(p => p.Player.Player == holder).Position.DistanceTo(now), 0, 0.35); // dribbled, not left behind
            }

            (ball, frame) = (now, playback.Current);
        }

        Assert.InRange(maxPlayer, 0, 0.3); // < 18 m/s at 60 fps: no teleporting
        Assert.InRange(maxBall, 0, 0.8);
    }

    [Fact]
    public void Shots_come_from_in_front_of_the_backboard_and_the_floor_is_spaced()
    {
        var (shots, frames, keys) = (0, 0, 0);
        var closestAttackers = new List<double>();
        for (var seed = 0; seed < 8; seed++)
        {
            var (sim, tracker) = Watch(seed);
            foreach (var e in sim.Events.Where(e => e.Shot is not null))
            {
                var shot = e.Shot!.Value;
                Assert.False(ShotZones.IsBehindBackboard(shot.X, shot.Y), $"{shot.X},{shot.Y} {shot.Zone}");
                shots++;
            }

            foreach (var frame in tracker.Frames.Where(f => f.Action is CourtAction.Shot or CourtAction.Rebound or CourtAction.Turnover or CourtAction.Foul))
            {
                // Nobody on top of anybody: only a defender on his man, or the event's own two players, may be closer.
                HashSet<Ebm.Domain.Players.PlayerId?> involved = [frame.Event.Player, frame.Event.OtherPlayer, frame.BallHolder];
                AssertSpaced(frame.Players, (a, b) => involved.Contains(a.Player) && involved.Contains(b.Player));
                frames++;
                if (frame.Action == CourtAction.Shot)
                {
                    var attackers = frame.Players.Where(p => p.Side == frame.Offense).ToList();
                    closestAttackers.Add(attackers.SelectMany(a => attackers.Where(b => b != a).Select(b => a.Position.DistanceTo(b.Position))).DefaultIfEmpty(9).Min());
                }

                foreach (var key in frame.Lead.SkipLast(1))
                {
                    AssertSpaced(key.Players, (a, b) => a.Player == key.BallHolder || b.Player == key.BallHolder || a.Player == key.Screener || b.Player == key.Screener || a.Side != b.Side);
                    keys++;
                }
            }
        }

        Assert.True(shots > 800 && frames > 1500 && keys > 5000, $"{shots} {frames} {keys}");
        Assert.True(closestAttackers.Average() > 2.6, $"{closestAttackers.Average():0.00}");
        Assert.True(closestAttackers.Count(d => d < 1.5) < closestAttackers.Count / 20);

        static void AssertSpaced(IReadOnlyList<CourtPlayer> players, Func<CourtPlayer, CourtPlayer, bool> exempt)
        {
            for (var i = 0; i < players.Count; i++)
            {
                for (var j = i + 1; j < players.Count; j++)
                {
                    var (a, b) = (players[i], players[j]);
                    if ((a.Side != b.Side && a.Slot == b.Slot) || exempt(a, b))
                    {
                        continue;
                    }

                    Assert.True(a.Position.DistanceTo(b.Position) >= 0.7, $"{a} {b}");
                }
            }
        }
    }
}
