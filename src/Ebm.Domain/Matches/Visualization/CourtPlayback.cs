using Ebm.Domain.Players;

namespace Ebm.Domain.Matches.Visualization;

/// <summary>A player drawn at an instant of the playback.</summary>
public readonly record struct DrawnPlayer(CourtPlayer Player, CourtPoint Position, bool Entering);

/// <summary>A pass drawn as a fading trail.</summary>
public readonly record struct PassTrail(CourtPoint From, CourtPoint To, double Opacity);

/// <summary>
/// Plays court frames over time (Step 30): each frame is its possession's keys (<see cref="CourtFrame.Lead"/>) and then
/// the event's own layout. Players run along smooth paths through the keys, the ball stays with its holder, passes
/// and shots fly. It only consumes frames, so pausing, resuming or skipping never affects the simulation.
/// </summary>
public sealed class CourtPlayback
{
    private const double TrailSeconds = 1.2;

    private readonly Queue<CourtFrame> _queue = new();
    private CourtFrame? _from;
    private CourtKey[] _keys = [];
    private double[] _ends = [];
    private Dictionary<PlayerId, CourtPoint[]> _tracks = [];
    private CourtPoint _startBall;

    /// <summary>Playback seconds at 1× into the current frame.</summary>
    private double _elapsed;
    private double _speed = 1;

    public CourtFrame? Current { get; private set; }

    /// <summary>0–1 through the current frame (its lead-up and the event).</summary>
    public double Progress => Current is null ? 1 : Math.Clamp(_elapsed / Total, 0, 1);

    public bool IsPaused { get; private set; }

    /// <summary>Changes whenever the shown frame, pause state or queue changes (a redraw is due).</summary>
    public int Version { get; private set; }

    /// <summary>Frames waiting to be shown.</summary>
    public int Pending => _queue.Count;

    /// <summary>True while a frame is running or frames are waiting (and not paused).</summary>
    public bool IsPlaying => !IsPaused && (Pending > 0 || Progress < 1);

    /// <summary>Playback speed multiplier, 0.25–8.</summary>
    public double Speed
    {
        get => _speed;
        set => _speed = Math.Clamp(value, 0.25, 8);
    }

    /// <summary>Who has the ball now (null while it flies).</summary>
    public PlayerId? Holder => Segment() is var (k, _) && k >= 0 && _keys[k].Flight == BallFlight.Held ? _keys[k].BallHolder : null;

    /// <summary>The player setting a screen now, if any.</summary>
    public PlayerId? Screener => Segment() is var (k, _) && k >= 0 ? _keys[k].Screener : null;

    private double Total => _ends.Length == 0 ? 1 : _ends[^1];

    /// <summary>Seconds a frame's event takes at speed 1 (without its lead-up).</summary>
    public static double BaseDuration(CourtAction action) => action switch
    {
        CourtAction.Shot => 1.4,
        CourtAction.FreeThrow => 1.0,
        CourtAction.TipOff => 1.0,
        CourtAction.Turnover => 0.9,
        CourtAction.Rebound => 0.8,
        CourtAction.Foul => 0.8,
        CourtAction.Substitution => 0.6,
        _ => 0.5,
    };

    /// <summary>Seconds a whole frame takes at speed 1: its lead-up and its event.</summary>
    public static double Duration(CourtFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.Lead.Sum(k => k.Duration) + EventSeconds(frame);
    }

    private static double EventSeconds(CourtFrame frame) => frame.EventDuration > 0 ? frame.EventDuration : BaseDuration(frame.Action);

    public void Enqueue(IEnumerable<CourtFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        foreach (var frame in frames)
        {
            _queue.Enqueue(frame);
        }

        if (Current is null)
        {
            Next();
        }
    }

    public void Pause()
    {
        IsPaused = true;
        Version++;
    }

    public void Resume()
    {
        IsPaused = false;
        Version++;
    }

    /// <summary>Jumps to the last queued frame, finished.</summary>
    public void SkipToEnd()
    {
        while (_queue.Count > 0)
        {
            Next();
        }

        if (Current is not null)
        {
            _elapsed = Total;
        }

        Version++;
    }

    /// <summary>Drops every frame (a new match).</summary>
    public void Clear()
    {
        _queue.Clear();
        Current = _from = null;
        _keys = [];
        _ends = [];
        _tracks = [];
        _elapsed = 0;
        Version++;
    }

    /// <summary>Advances the clock by real seconds; returns true when the drawing changed.</summary>
    public bool Tick(double seconds)
    {
        if (IsPaused || Current is null || seconds <= 0)
        {
            return false;
        }

        var before = (Current, Progress);
        _elapsed += seconds * _speed;
        while (_elapsed >= Total && _queue.Count > 0)
        {
            _elapsed -= Total;
            Next();
        }

        return before != (Current, Progress);
    }

    /// <summary>The players at this instant, running along their paths through the keys.</summary>
    public IReadOnlyList<DrawnPlayer> Players()
    {
        if (Current is null || Segment() is not var (k, u) || k < 0)
        {
            return [];
        }

        return [.. _keys[k].Players.Select(p => new DrawnPlayer(p, Position(p.Player, k, u), _from is not null && _from.Find(p.Player) is null))];
    }

    /// <summary>The ball at this instant and its height in metres (dribbled, passed, shot or loose).</summary>
    public (CourtPoint Position, double Height) Ball()
    {
        if (Current is null || Segment() is not var (k, u) || k < 0)
        {
            return (new CourtPoint(CourtTracker.Length / 2, CourtTracker.Width / 2), 0);
        }

        var key = _keys[k];
        var from = BallFrom(k);
        switch (key.Flight)
        {
            case BallFlight.Held when key.BallHolder is { } holder && _tracks.ContainsKey(holder):
                // Dribbling: on the holder's hand side, bouncing about twice a second.
                var at = Position(holder, k, u);
                var bounce = Math.Abs(Math.Sin(_elapsed * Math.PI * 2));
                return (new CourtPoint(at.X, at.Y + 0.3), 0.15 + (0.75 * bounce));
            case BallFlight.Held:
                return (from.Lerp(key.Ball, u), 0);
            case BallFlight.Pass:
                return (from.Lerp(key.Ball, u), 1.2 + (0.6 * 4 * u * (1 - u)));
            case BallFlight.Shot:
                return (from.Lerp(key.Ball, u), 1.5 + (Math.Min(4, 1 + (from.DistanceTo(key.Ball) / 3)) * 4 * u * (1 - u)));
            default:
                return (from.Lerp(key.Ball, u), (3.05 * (1 - u)) + (0.9 * u) + (1.2 * 4 * u * (1 - u)));
        }
    }

    /// <summary>Passes of the current possession made in the last moments, fading out (the one in flight is solid).</summary>
    public IReadOnlyList<PassTrail> Trails()
    {
        if (Current is null || Segment() is not var (k, _) || k < 0)
        {
            return [];
        }

        var now = Warped();
        var trails = new List<PassTrail>();
        for (var i = 0; i <= k; i++)
        {
            if (_keys[i].Flight != BallFlight.Pass)
            {
                continue;
            }

            if (i == k)
            {
                trails.Add(new PassTrail(BallFrom(i), Ball().Position, 0.9));
            }
            else if (now - _ends[i] < TrailSeconds)
            {
                trails.Add(new PassTrail(BallFrom(i), _keys[i].Ball, 0.9 * (1 - ((now - _ends[i]) / TrailSeconds))));
            }
        }

        return trails;
    }

    private void Next()
    {
        _from = Current;
        Current = _queue.Dequeue();
        _elapsed = 0;
        Version++;

        var frame = Current;
        _keys = [.. frame.Lead, new CourtKey(EventSeconds(frame), frame.Players, frame.Ball, frame.BallHolder, frame.Flight)];
        _ends = new double[_keys.Length];
        var t = 0.0;
        for (var i = 0; i < _keys.Length; i++)
        {
            t += Math.Max(0.05, _keys[i].Duration);
            _ends[i] = t;
        }

        // Each player's position at the start and at the end of every key (carried over while he is off court).
        _tracks = [];
        foreach (var id in _keys.SelectMany(k => k.Players).Select(p => p.Player).Distinct())
        {
            var track = new CourtPoint[_keys.Length + 1];
            var first = _keys.Select(k => k.Find(id)).First(p => p is not null)!.Position;
            track[0] = _from?.Find(id)?.Position ?? first;
            for (var i = 0; i < _keys.Length; i++)
            {
                track[i + 1] = _keys[i].Find(id)?.Position ?? track[i];
            }

            _tracks[id] = track;
        }

        _startBall = frame.Lead.Count == 0 ? frame.BallFrom : _from?.Ball ?? frame.BallFrom;
    }

    private CourtPoint BallFrom(int k) => k == _keys.Length - 1 && Current is { } f && f.Lead.Count > 0
        ? f.BallFrom
        : k == 0 ? _startBall : _keys[k - 1].Ball;

    /// <summary>The key playing now and 0–1 through it; a lone event eases in and out, a possession runs at an even pace.</summary>
    private (int Key, double U)? Segment()
    {
        if (_keys.Length == 0)
        {
            return null;
        }

        var now = Warped();
        var k = 0;
        while (k < _keys.Length - 1 && now > _ends[k])
        {
            k++;
        }

        var start = k == 0 ? 0 : _ends[k - 1];
        return (k, Math.Clamp((now - start) / (_ends[k] - start), 0, 1));
    }

    /// <summary>
    /// Playback time warped so motion starts and stops gently: smoothstep for a lone event, a short speed-up and
    /// slow-down around an even pace for a possession.
    /// </summary>
    private double Warped()
    {
        var x = Math.Clamp(_elapsed / Total, 0, 1);
        if (_keys.Length == 1)
        {
            return Total * x * x * (3 - (2 * x));
        }

        const double r = 0.12;
        const double v = 1 / (1 - r);
        var w = x < r ? v * x * x / (2 * r) : x > 1 - r ? 1 - (v * (1 - x) * (1 - x) / (2 * r)) : v * (x - (r / 2));
        return Total * w;
    }

    /// <summary>
    /// A cubic through the player's key positions with time-aware tangents (capped at a sprint), so he runs through
    /// keys without stopping dead and short keys do not make him jump.
    /// </summary>
    private CourtPoint Position(PlayerId id, int k, double u)
    {
        var t = _tracks[id];
        var (p1, p2) = (t[k], t[k + 1]);
        if (u >= 1 || u <= 0)
        {
            return u >= 1 ? p2 : p1;
        }

        var dt = _ends[k] - Time(k);
        var (m1, m2) = (Tangent(t, k), Tangent(t, k + 1));
        var (h00, h10, h01, h11) = ((2 * u * u * u) - (3 * u * u) + 1, (u * u * u) - (2 * u * u) + u, (-2 * u * u * u) + (3 * u * u), (u * u * u) - (u * u));
        var p = new CourtPoint(
            (h00 * p1.X) + (h10 * dt * m1.X) + (h01 * p2.X) + (h11 * dt * m2.X),
            (h00 * p1.Y) + (h10 * dt * m1.Y) + (h01 * p2.Y) + (h11 * dt * m2.Y));
        return new CourtPoint(Math.Clamp(p.X, 0, CourtTracker.Length), Math.Clamp(p.Y, 0, CourtTracker.Width));
    }

    /// <summary>Start time of key <paramref name="i"/> (the time of track point i).</summary>
    private double Time(int i) => i == 0 ? 0 : _ends[i - 1];

    /// <summary>Velocity (m/s) through track point i: zero at the ends of the frame, capped in between.</summary>
    private CourtPoint Tangent(CourtPoint[] track, int i)
    {
        if (i <= 0 || i >= track.Length - 1)
        {
            return default;
        }

        var span = Math.Max(1e-3, Time(i + 1) - Time(i - 1));
        var (vx, vy) = ((track[i + 1].X - track[i - 1].X) / span, (track[i + 1].Y - track[i - 1].Y) / span);
        var speed = Math.Sqrt((vx * vx) + (vy * vy));
        var cap = CourtTracker.MaxRunSpeed;
        return speed > cap ? new CourtPoint(vx * cap / speed, vy * cap / speed) : new CourtPoint(vx, vy);
    }
}
