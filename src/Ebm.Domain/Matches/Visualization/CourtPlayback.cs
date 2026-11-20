namespace Ebm.Domain.Matches.Visualization;

/// <summary>A player drawn at an instant of the playback.</summary>
public readonly record struct DrawnPlayer(CourtPlayer Player, CourtPoint Position, bool Entering);

/// <summary>
/// Plays court frames over time (Step 30): each frame tweens from the previous layout in a duration set by its
/// action and the speed. It only consumes frames, so pausing, resuming or skipping never affects the simulation.
/// </summary>
public sealed class CourtPlayback
{
    private readonly Queue<CourtFrame> _queue = new();
    private CourtFrame? _from;
    private double _elapsed;
    private double _speed = 1;

    public CourtFrame? Current { get; private set; }

    /// <summary>0–1 through the current frame's tween.</summary>
    public double Progress => Current is null ? 1 : Math.Clamp(_elapsed / Duration(Current), 0, 1);

    public bool IsPaused { get; private set; }

    /// <summary>Changes whenever the shown frame, pause state or queue changes (a redraw is due).</summary>
    public int Version { get; private set; }

    /// <summary>Frames waiting to be shown.</summary>
    public int Pending => _queue.Count;

    /// <summary>True while a tween is running or frames are waiting (and not paused).</summary>
    public bool IsPlaying => !IsPaused && (Pending > 0 || Progress < 1);

    /// <summary>Playback speed multiplier, 0.25–8.</summary>
    public double Speed
    {
        get => _speed;
        set => _speed = Math.Clamp(value, 0.25, 8);
    }

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
            _elapsed = Duration(Current);
        }

        Version++;
    }

    /// <summary>Drops every frame (a new match).</summary>
    public void Clear()
    {
        _queue.Clear();
        Current = _from = null;
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
        _elapsed += seconds;
        while (_elapsed >= Duration(Current) && _queue.Count > 0)
        {
            _elapsed -= Duration(Current);
            Next();
        }

        return before != (Current, Progress);
    }

    /// <summary>The players at this instant, eased between the previous frame and the current one.</summary>
    public IReadOnlyList<DrawnPlayer> Players()
    {
        if (Current is null)
        {
            return [];
        }

        var t = Ease(Progress);
        return [.. Current.Players.Select(p =>
        {
            var start = _from?.Find(p.Player)?.Position;
            return new DrawnPlayer(p, start is { } s ? s.Lerp(p.Position, t) : p.Position, start is null && _from is not null);
        })];
    }

    /// <summary>The ball at this instant and its height in metres (an arc for shots and free throws).</summary>
    public (CourtPoint Position, double Height) Ball()
    {
        if (Current is null)
        {
            return (new CourtPoint(CourtTracker.Length / 2, CourtTracker.Width / 2), 0);
        }

        var t = Ease(Progress);
        var position = Current.BallFrom.Lerp(Current.Ball, t);
        var height = Current.BallInAir ? 1.5 + (Math.Min(4, 1 + (Current.BallFrom.DistanceTo(Current.Ball) / 3)) * 4 * t * (1 - t)) : 0;
        return (position, height);
    }

    /// <summary>Seconds a frame takes at speed 1.</summary>
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

    private double Duration(CourtFrame frame) => BaseDuration(frame.Action) / _speed;

    private void Next()
    {
        _from = Current;
        Current = _queue.Dequeue();
        _elapsed = 0;
        Version++;
    }

    private static double Ease(double t) => t * t * (3 - (2 * t));
}
