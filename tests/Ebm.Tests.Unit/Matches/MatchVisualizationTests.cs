using System.Text.Json;
using Ebm.Domain.Matches;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Matches.Visualization;

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
        playback.Speed = 2;
        playback.Tick((CourtPlayback.BaseDuration(frames[1].Action) / 2) + 0.001);
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

        var shot = tracker.Frames.First(f => f.Action == CourtAction.Shot);
        var playback = new CourtPlayback();
        playback.Enqueue([shot]);
        Assert.Equal(shot.BallFrom, playback.Ball().Position);
        playback.Tick(CourtPlayback.BaseDuration(CourtAction.Shot) / 2);
        Assert.True(playback.Ball().Height > 2);
        playback.SkipToEnd();
        Assert.InRange(shot.Ball.DistanceTo(playback.Ball().Position), 0, 1e-9);
    }
}
