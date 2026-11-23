using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Ebm.Domain.Tactics;

namespace Ebm.Domain.Matches.Visualization;

/// <summary>How the ball travels during a key.</summary>
public enum BallFlight
{
    /// <summary>Dribbled by the holder (or lying still when nobody holds it).</summary>
    Held,
    Pass,
    Shot,

    /// <summary>A loose ball: off the rim, a tip, a deflection.</summary>
    Bounce,
}

/// <summary>The play a possession is drawn with (docs/MATCH_VISUALIZATION.md §3).</summary>
public enum CourtPlay
{
    None,
    FastBreak,
    EarlyOffense,
    PickAndRoll,
    PickAndPop,
    DriveAndKick,
    PostUp,
    HandOff,
    OffScreen,
    BallMovement,
    Isolation,
    BackdoorCut,
    Putback,
    LateClock,
}

/// <summary>
/// One keyframe of a possession: where everyone is when it ends, where the ball is and who has it, and how the ball
/// got there. <see cref="Duration"/> is in playback seconds at 1×; <see cref="Screener"/> marks a player setting a screen.
/// </summary>
public sealed record CourtKey(double Duration, IReadOnlyList<CourtPlayer> Players, CourtPoint Ball, PlayerId? BallHolder, BallFlight Flight)
{
    public PlayerId? Screener { get; init; }

    public CourtPlayer? Find(PlayerId player) => Players.FirstOrDefault(p => p.Player == player);
}

/// <summary>Display texts of the plays.</summary>
public static class CourtPlays
{
    public static string Name(CourtPlay play) => play switch
    {
        CourtPlay.FastBreak => "Fast break",
        CourtPlay.EarlyOffense => "Early offense",
        CourtPlay.PickAndRoll => "Pick-and-roll",
        CourtPlay.PickAndPop => "Pick-and-pop",
        CourtPlay.DriveAndKick => "Drive and kick",
        CourtPlay.PostUp => "Post-up",
        CourtPlay.HandOff => "Hand-off",
        CourtPlay.OffScreen => "Off a screen",
        CourtPlay.BallMovement => "Ball movement",
        CourtPlay.Isolation => "Isolation",
        CourtPlay.BackdoorCut => "Backdoor cut",
        CourtPlay.Putback => "Putback",
        CourtPlay.LateClock => "Late clock",
        _ => string.Empty,
    };

    /// <summary>
    /// "Pick-and-roll → Rossi · corner three"; just "Pick-and-roll" while <paramref name="detailed"/> is false (the
    /// lead-up, so the caption does not tell who ends the play and how); null for frames without a play.
    /// </summary>
    public static string? Describe(CourtFrame frame, Func<PlayerId, string?> name, bool detailed = true)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(name);
        if (frame.Play == CourtPlay.None)
        {
            return null;
        }

        if (!detailed)
        {
            return Name(frame.Play);
        }

        var e = frame.Event;
        var (who, what) = e.Kind switch
        {
            MatchEventKind.FieldGoalMade or MatchEventKind.FieldGoalMissed => (e.Player, e.Shot is { } s ? ShotText(s) : "shot"),
            MatchEventKind.Turnover => (e.Player, "turnover"),
            MatchEventKind.ShotClockViolation => (null, "the shot clock runs out"),
            MatchEventKind.Foul => (e.OtherPlayer, "fouled"),
            _ => ((PlayerId?)null, (string?)null),
        };
        var player = who is { } id ? name(id) : null;
        return Name(frame.Play) + (player is null ? string.Empty : $" → {player}") + (what is null ? string.Empty : $" · {what}");
    }

    public static string ShotText(ShotRecord shot) => shot.Zone switch
    {
        ShotZone.RestrictedArea => shot.Distance < 1.0 ? "at the rim" : "layup",
        ShotZone.Paint => "in the paint",
        ShotZone.MidRangeLeft or ShotZone.MidRangeCenter or ShotZone.MidRangeRight => "mid-range",
        ShotZone.CornerThreeLeft or ShotZone.CornerThreeRight => "corner three",
        ShotZone.WingThreeLeft or ShotZone.WingThreeRight => "wing three",
        _ => "three from the top",
    };
}

/// <summary>Half-court formations (across, depth from the attacked baseline) of the five slots per offensive system.</summary>
internal static class CourtFormations
{
    private static readonly (double A, double D)[] Standard = [(0.0, 8.2), (-5.4, 6.0), (6.6, 0.9), (3.2, 4.6), (-2.4, 2.2)];
    private static readonly (double A, double D)[] Spread = [(0.0, 8.4), (-5.6, 6.2), (6.6, 0.9), (5.4, 6.4), (-6.6, 0.9)];
    private static readonly (double A, double D)[] FourOut = [(0.0, 8.2), (-5.6, 6.2), (6.6, 0.9), (-6.6, 0.9), (1.9, 2.0)];
    private static readonly (double A, double D)[] Post = [(0.0, 8.0), (-5.4, 6.0), (6.4, 1.2), (2.0, 5.6), (-1.9, 1.9)];

    public static (double A, double D)[] For(OffensiveSystem system) => system switch
    {
        OffensiveSystem.FiveOut or OffensiveSystem.Isolation => Spread,
        OffensiveSystem.FourOutOneIn => FourOut,
        OffensiveSystem.PostOriented => Post,
        _ => Standard,
    };
}

/// <summary>What a possession script needs: the event, the line-ups, the layouts before and at the event.</summary>
/// <param name="Target">Who has the ball when the event happens: shooter, player losing it, fouled player.</param>
/// <param name="Passer">The assist on a made shot.</param>
/// <param name="GameSeconds">Game-clock seconds since the last live event: how long the possession took.</param>
internal sealed record PlayContext(
    int Index,
    MatchEvent Event,
    TeamSide Offense,
    IReadOnlyList<PlayerId?> Attack,
    IReadOnlyList<PlayerId?> Defence,
    CourtFrame? Previous,
    CourtFrame? LastLive,
    IReadOnlyList<CourtPlayer> Final,
    PlayerId? Target,
    PlayerId? Passer,
    OffensiveSystem System,
    double GameSeconds,
    (double A, double D)[] Formation);

/// <summary>
/// Scripts the lead-up of a shot, turnover or foul as keyframes (docs/MATCH_VISUALIZATION.md §3): getting the ball
/// up the floor (outlet, inbound, push), setting the formation, a play chosen from the event (shooter, assist, shot
/// spot, fast break, clock used) and the offensive system, and the catch or the last dribble into the event's own
/// layout. Deterministic: choices come from a hash of the event index.
/// </summary>
internal sealed class PossessionScript
{
    private const double Half = CourtTracker.Length / 2;

    private readonly PlayContext _c;
    private readonly Dictionary<PlayerId, CourtPoint> _pos = [];
    private readonly Dictionary<PlayerId, CourtPoint> _start;
    private readonly HashSet<PlayerId> _pinned = [];

    /// <summary>Attackers the play placed in the key being built (the others are spaced around them).</summary>
    private readonly HashSet<PlayerId> _moved = [];
    private readonly List<(double Weight, CourtKey Key)> _keys = [];
    private readonly CourtPoint _rim;
    private readonly int _sign;
    private readonly bool _attacksRight;
    private PlayerId? _holder;
    private CourtPoint _ball;
    private int _salt;

    private PossessionScript(PlayContext c)
    {
        _c = c;
        _rim = CourtTracker.Rim(c.Offense, c.Event.Period);
        _attacksRight = CourtTracker.AttacksRight(c.Offense, c.Event.Period);
        foreach (var p in c.Final)
        {
            _pos[p.Player] = c.Previous?.Find(p.Player)?.Position ?? p.Position;
        }

        _start = new Dictionary<PlayerId, CourtPoint>(_pos);
        _holder = c.Previous?.BallHolder is { } h && IsAttacker(h) && _pos.ContainsKey(h) ? h : null;
        _ball = c.Previous?.Ball ?? new CourtPoint(Half, CourtTracker.Width / 2);
        var target = c.Target is { } t ? c.Final.FirstOrDefault(p => p.Player == t) : null;
        _sign = target is null ? (Hash(3) % 2 == 0 ? 1 : -1) : ToHalf(target.Position).A >= 0 ? 1 : -1;
    }

    /// <summary>The lead-up keys of an event and the play drawn; empty for a putback or when nobody can act.</summary>
    public static (IReadOnlyList<CourtKey> Lead, CourtPlay Play) Build(PlayContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Attack.Any(p => p is not null))
        {
            return ([], CourtPlay.None);
        }

        var script = new PossessionScript(context);
        var play = script.Run();
        if (play == CourtPlay.Putback)
        {
            return ([], play);
        }

        var total = Math.Clamp(0.5 + (context.GameSeconds * 0.11), 0.9, 3.4);
        return (script.Finish(total), play);
    }

    // ---------------------------------------------------------------- choice

    private CourtPlay Run()
    {
        var e = _c.Event;
        var shot = e.Shot;
        var shooterSlot = SlotOf(_c.Target);
        var big = shooterSlot >= 3;
        if (shot is { } s && _c.LastLive is { Action: CourtAction.Rebound } reb && reb.Offense == _c.Offense && reb.BallHolder == _c.Target && _c.GameSeconds <= 3 && s.Distance < 2.5)
        {
            return CourtPlay.Putback;
        }

        if (shot?.FastBreak == true)
        {
            FastBreak();
            End();
            return CourtPlay.FastBreak;
        }

        var backcourt = !InFrontcourt();
        Start(backcourt);
        var play = Choose(shot, shooterSlot, big, backcourt);
        if (play != CourtPlay.EarlyOffense)
        {
            Set();
        }

        Perform(play, big);
        End();
        return play;
    }

    private CourtPlay Choose(ShotRecord? shot, int shooterSlot, bool big, bool backcourt)
    {
        var e = _c.Event;
        var candidates = new List<(CourtPlay Play, double Weight)>();
        void Add(CourtPlay play, double weight) => candidates.Add((play, weight * SystemWeight(play)));

        if (e.Kind == MatchEventKind.ShotClockViolation || (_c.GameSeconds >= 19 && _c.Passer is null && e.Kind != MatchEventKind.Foul))
        {
            return CourtPlay.LateClock;
        }

        if (backcourt && _c.GameSeconds <= 9 && e.Kind is MatchEventKind.FieldGoalMade or MatchEventKind.FieldGoalMissed)
        {
            Add(CourtPlay.EarlyOffense, _c.System == OffensiveSystem.Transition ? 4 : 1.5);
        }

        if (shot is { } s)
        {
            var close = s.Distance < 3.2;
            var passerBig = SlotOf(_c.Passer) >= 3;
            if (_c.Passer is not null)
            {
                if (close)
                {
                    Add(big ? CourtPlay.PickAndRoll : CourtPlay.BackdoorCut, 3);
                    Add(CourtPlay.DriveAndKick, 1);
                    if (big)
                    {
                        Add(CourtPlay.PostUp, 2);
                    }
                    else
                    {
                        Add(CourtPlay.PickAndRoll, 1);
                    }
                }
                else if (s.IsThree)
                {
                    Add(CourtPlay.DriveAndKick, 3);
                    Add(CourtPlay.BallMovement, 2);
                    Add(big ? CourtPlay.PickAndPop : CourtPlay.OffScreen, 2);
                    Add(CourtPlay.HandOff, 1);
                    if (passerBig)
                    {
                        Add(CourtPlay.PostUp, 2);
                    }
                }
                else
                {
                    Add(big ? CourtPlay.PickAndPop : CourtPlay.OffScreen, 3);
                    Add(CourtPlay.HandOff, 2);
                    Add(CourtPlay.BallMovement, 1);
                }
            }
            else if (big)
            {
                Add(close ? CourtPlay.PostUp : CourtPlay.PickAndPop, 4);
                Add(CourtPlay.Isolation, 1);
            }
            else
            {
                Add(CourtPlay.PickAndRoll, 3);
                Add(CourtPlay.Isolation, 2);
                Add(CourtPlay.HandOff, 1.5);
            }
        }
        else
        {
            // Turnovers and fouls: whatever the offense was running when it happened.
            Add(CourtPlay.BallMovement, 2);
            Add(CourtPlay.PickAndRoll, 2);
            Add(CourtPlay.DriveAndKick, 1.5);
            Add(SlotOf(_c.Target) >= 3 ? CourtPlay.PostUp : CourtPlay.Isolation, 1.5);
        }

        var total = candidates.Sum(c => c.Weight);
        var pick = (Hash(11) % 10_000) / 10_000.0 * total;
        foreach (var (play, weight) in candidates)
        {
            pick -= weight;
            if (pick < 0)
            {
                return play;
            }
        }

        return candidates[^1].Play;
    }

    private double SystemWeight(CourtPlay play) => (_c.System, play) switch
    {
        (OffensiveSystem.PickAndRoll, CourtPlay.PickAndRoll or CourtPlay.PickAndPop) => 3,
        (OffensiveSystem.PostOriented, CourtPlay.PostUp) => 3,
        (OffensiveSystem.Isolation, CourtPlay.Isolation) => 3,
        (OffensiveSystem.FiveOut, CourtPlay.DriveAndKick or CourtPlay.HandOff or CourtPlay.BackdoorCut) => 2,
        (OffensiveSystem.MotionOffense, CourtPlay.BallMovement or CourtPlay.OffScreen or CourtPlay.BackdoorCut) => 2,
        (OffensiveSystem.FourOutOneIn, CourtPlay.PostUp or CourtPlay.DriveAndKick) => 2,
        (OffensiveSystem.Transition, CourtPlay.EarlyOffense or CourtPlay.DriveAndKick) => 2,
        _ => 1,
    };

    // ---------------------------------------------------------------- phases

    /// <summary>Getting the ball: outlet after a rebound, inbound after a score or dead ball, then up the floor.</summary>
    private void Start(bool backcourt)
    {
        if (!backcourt)
        {
            if (_holder is null)
            {
                Pass(Handler, 0.5);
            }

            return;
        }

        var last = _c.LastLive;
        var handler = Handler;
        if (last is { Action: CourtAction.Rebound } && _holder is { } rebounder)
        {
            // Outlet: the guard gets open on the wing of his own half.
            if (rebounder != handler)
            {
                Move(handler, _sign * 5.0, 21.5);
                RunAhead(4, except: [rebounder, handler]);
                Key(0.6);
                Pass(handler, 0.6);
            }
        }
        else if (_holder is null || last?.Event.Kind is MatchEventKind.FieldGoalMade or MatchEventKind.FreeThrowMade)
        {
            // Inbound: a big takes it out under the basket the offense defended; a forward from the sideline otherwise.
            var scored = last?.Event.Kind is MatchEventKind.FieldGoalMade or MatchEventKind.FreeThrowMade;
            var inbounder = scored ? Slot(4) ?? Slot(3) : Slot(2) ?? Slot(1);
            if (scored)
            {
                Move(inbounder, -_sign * 1.2, 27.9);
                Move(handler, _sign * 3.0, 23.0);
            }
            else
            {
                Move(inbounder, _sign * 7.4, 17.0);
                Move(handler, _sign * 3.0, 18.5);
            }

            _holder = inbounder;
            Key(0.5, BallFlight.Bounce);
            Pass(handler, 0.6);
        }
        else if (_holder != handler)
        {
            Pass(handler, 0.5);
        }

        // Bring it up: the guard dribbles over half court, the others run to their spots.
        Move(Handler, (_sign * 1.5) + J(0, 0), 12.0 + J(0, 1));
        for (var s = 0; s < 5; s++)
        {
            if (Slot(s) is { } id && id != Handler)
            {
                Move(id, _c.Formation[s].A + J(s, 0), _c.Formation[s].D + 3.5 + J(s, 1));
            }
        }

        Key(1.3);
    }

    /// <summary>The offense in its formation, the ball handler at the top.</summary>
    private void Set()
    {
        for (var s = 0; s < 5; s++)
        {
            Move(Slot(s), _c.Formation[s].A + J(s, 0), _c.Formation[s].D + J(s, 1));
        }

        Key(0.9);
    }

    private void Perform(CourtPlay play, bool big)
    {
        var giver = _c.Passer ?? _c.Target ?? Handler;
        switch (play)
        {
            case CourtPlay.PickAndRoll:
            case CourtPlay.PickAndPop:
                var screener = (big ? _c.Target : null) ?? (SlotOf(_c.Passer) >= 3 ? _c.Passer : null) ?? Slot(4) ?? Slot(3);
                var handler = new[] { _c.Passer, _c.Target, Handler }.FirstOrDefault(p => p is not null && p != screener) ?? Handler;
                PickAndRoll(handler, screener, play == CourtPlay.PickAndRoll);
                break;
            case CourtPlay.DriveAndKick:
            case CourtPlay.EarlyOffense:
                var driver = _c.Passer ?? _c.Target ?? Handler;
                if (driver == _c.Passer && SlotOf(driver) >= 3)
                {
                    driver = Handler != _c.Target ? Handler : driver;
                }

                Drive(driver, help: _c.Target);
                break;
            case CourtPlay.PostUp:
                PostUp(SlotOf(_c.Target) >= 3 ? _c.Target : SlotOf(_c.Passer) >= 3 ? _c.Passer : Slot(4) ?? Slot(3));
                break;
            case CourtPlay.HandOff:
                HandOff(Slot(4) ?? Slot(3), _c.Target ?? Slot(1));
                break;
            case CourtPlay.OffScreen:
                OffScreen(_c.Target, Slot(3) ?? Slot(4));
                break;
            case CourtPlay.BallMovement:
                BallMovement();
                break;
            case CourtPlay.Isolation:
            case CourtPlay.LateClock:
                Isolation(_c.Target ?? Handler, late: play == CourtPlay.LateClock);
                break;
            case CourtPlay.BackdoorCut:
                BackdoorCut(_c.Target);
                break;
        }

        Give(giver, 0.6);
    }

    private void FastBreak()
    {
        // The ball is pushed at once, the lanes fill, the defense sprints back behind it.
        var pusher = _holder ?? _c.Passer ?? _c.Target ?? Handler;
        _holder ??= pusher;
        Move(pusher, J(0, 0) * 2, 15.0);
        var lane = 0;
        foreach (var id in Attackers().Where(id => id != pusher))
        {
            var across = lane++ switch { 0 => 5.5, 1 => -5.5, 2 => 2.0, _ => -2.0 };
            Move(id, across, 12.5 + (lane * 1.5));
        }

        Key(0.9);
        Move(pusher, _sign * 1.0, 8.5);
        lane = 0;
        foreach (var id in Attackers().Where(id => id != pusher))
        {
            var across = lane++ switch { 0 => 4.8, 1 => -4.8, 2 => 1.5, _ => -1.5 };
            Move(id, across, 3.5 + (lane * 1.8));
        }

        Key(0.9);
        Give(_c.Passer ?? _c.Target, 0.5);
    }

    private void PickAndRoll(PlayerId? handler, PlayerId? screener, bool roll)
    {
        Give(handler, 0.5);
        if (handler is null || screener is null)
        {
            return;
        }

        var (ha, hd) = ToHalf(_pos[handler.Value]);
        var side = ha >= 0 ? 1 : -1;
        Move(handler, side * 1.6, Math.Max(hd, 7.6));
        Move(screener, side * 0.7, 7.0);
        Key(0.9, screener: screener);

        // The handler turns the corner, his defender trails, the screener's defender shows.
        Move(handler, -side * 1.2, 5.0);
        Pin(DefenderOf(handler), At(-side * 0.4, 6.6));
        Pin(DefenderOf(screener), At(-side * 1.6, 4.6));
        Move(screener, -side * 0.2, 6.2);
        Key(0.8, screener: screener);

        Move(screener, roll ? side * 0.6 : side * 5.2, roll ? 2.4 : 6.6);
        Move(handler, -side * 2.0, 4.2);
        Key(0.8);
    }

    private void Drive(PlayerId? driver, PlayerId? help)
    {
        Give(driver, 0.6);
        if (driver is null)
        {
            return;
        }

        var (a, _) = ToHalf(_pos[driver.Value]);
        var side = a >= 0 ? 1 : -1;
        Move(driver, side * 1.4, 2.8);
        if (help is { } h && h != driver && DefenderOf(h) is { } helper)
        {
            // The help defender collapses on the drive and leaves his man.
            Pin(helper, CourtTracker.Between(_pos[helper], _pos[driver.Value], 2.2));
        }

        Key(1.0);
    }

    private void PostUp(PlayerId? post)
    {
        if (post is null)
        {
            return;
        }

        var wing = Attackers().Cast<PlayerId?>().FirstOrDefault(id => id != post && SlotOf(id) is 1 or 2) ?? Handler;
        if (wing == post)
        {
            wing = Handler;
        }

        Give(wing, 0.5);
        Move(wing, _sign * 5.4, 5.6);
        Move(post, _sign * 1.9, 2.1);
        Pin(DefenderOf(post), At(_sign * 1.7, 1.4));
        Key(0.9);
        Pass(post, 0.6);

        // Backing down: one bump towards the rim, his defender giving ground.
        Move(post, _sign * 1.2, 1.6);
        Pin(DefenderOf(post), At(_sign * 0.9, 1.0));
        Key(0.9);
    }

    private void HandOff(PlayerId? big, PlayerId? receiver)
    {
        if (big is null || receiver is null || big == receiver)
        {
            BallMovement();
            return;
        }

        Move(big, _sign * 2.2, 7.4);
        Move(receiver, _sign * 5.6, 4.0);
        Key(0.6);
        Pass(big, 0.6);
        Move(receiver, _sign * 2.9, 7.8);
        Key(0.7, screener: big);
        Pass(receiver, 0.3);
        Move(receiver, -_sign * 0.4, 6.6);
        Move(big, _sign * 1.8, 5.0);
        Key(0.7);
    }

    private void OffScreen(PlayerId? shooter, PlayerId? screener)
    {
        if (shooter is null || screener is null || shooter == screener)
        {
            BallMovement();
            return;
        }

        Give(_c.Passer ?? Handler, 0.5);
        Move(shooter, _sign * 2.0, 1.4);
        Move(screener, _sign * 3.6, 3.2);
        Key(0.8);
        Move(shooter, _sign * 4.4, 3.8);
        Pin(DefenderOf(shooter), At(_sign * 3.2, 2.6));
        Key(0.8, screener: screener);
    }

    private void BallMovement()
    {
        // Two or three swings around the perimeter, the offense shifting with the ball.
        var order = new[] { 1, 0, 3, 2, 0 }.Select(Slot).Where(p => p is not null).Distinct().ToList();
        var passes = 2 + (Hash(5) % 2);
        var start = (int)(Hash(7) % (uint)Math.Max(1, order.Count));
        for (var i = 0; i < passes && order.Count > 1; i++)
        {
            var next = order[(start + i) % order.Count];
            if (next == _holder)
            {
                continue;
            }

            _salt++;
            for (var s = 0; s < 5; s++)
            {
                Move(Slot(s), _c.Formation[s].A + J(s, 0), _c.Formation[s].D + J(s, 1));
            }

            Pass(next, 0.6);
        }
    }

    private void Isolation(PlayerId? iso, bool late)
    {
        Give(iso, 0.6);
        if (iso is null)
        {
            return;
        }

        // Everybody else clears to the weak side; the clock runs while he sizes up his man.
        var (a, d) = ToHalf(_pos[iso.Value]);
        var side = a >= 0 ? 1 : -1;
        (double A, double D)[] weak = [(-side * 6.6, 0.9), (-side * 5.4, 6.2), (side * 6.6, 0.9), (-side * 1.6, 1.6)];
        var spot = 0;
        foreach (var id in Attackers().Where(id => id != iso))
        {
            Move(id, weak[spot].A, weak[spot].D);
            spot = (spot + 1) % weak.Length;
        }

        Move(iso, side * 1.0, Math.Max(d, 7.2));
        Key(late ? 1.4 : 1.0);
        Move(iso, side * 2.2, Math.Max(d, 7.2) - 1.2);
        Key(0.6);
    }

    private void BackdoorCut(PlayerId? cutter)
    {
        if (cutter is null || cutter == _c.Passer)
        {
            return;
        }

        Give(_c.Passer ?? Handler, 0.5);
        Move(cutter, _sign * 6.0, 6.6);
        Pin(DefenderOf(cutter), At(_sign * 5.4, 7.4));
        Key(0.8);
    }

    /// <summary>The catch or the last dribble into the event's own layout.</summary>
    private void End()
    {
        var target = _c.Target;
        var shot = _c.Event.Kind is MatchEventKind.FieldGoalMade or MatchEventKind.FieldGoalMissed;
        if (_c.Passer is { } passer && passer != target)
        {
            Give(passer, 0.5);
        }

        foreach (var p in _c.Final)
        {
            var attacker = p.Side == _c.Offense;
            if (attacker || shot)
            {
                _pos[p.Player] = p.Position;
                if (!attacker)
                {
                    _pinned.Add(p.Player);
                }
            }
        }

        if (target is { } t && _pos.ContainsKey(t) && _holder != t)
        {
            _holder = t;
            Key(0.6, BallFlight.Pass, final: true);
        }
        else
        {
            Key(0.6, final: true);
        }
    }

    // ---------------------------------------------------------------- keys

    /// <summary>
    /// Snapshots a key: crowded attackers the play did not place spread to free spots, defenders follow their men
    /// (unless the play put them somewhere), nobody overlaps. The <paramref name="final"/> key is the event's own
    /// layout, already spaced by the tracker, and stays exactly as it is.
    /// </summary>
    private void Key(double weight, BallFlight flight = BallFlight.Held, PlayerId? screener = null, bool final = false)
    {
        HashSet<PlayerId> anchored = [.. _pinned];
        foreach (var id in new[] { _holder, screener })
        {
            if (id is { } p)
            {
                anchored.Add(p);
            }
        }

        if (!final)
        {
            var spread = Snapshot();
            CourtSpacing.Spread(spread, _c.Offense, _c.Event.Period, new HashSet<PlayerId>([.. _moved, .. anchored]));
            Apply(spread);
        }

        for (var s = 0; s < 5; s++)
        {
            if (_c.Defence[s] is not { } d || _pinned.Contains(d) || !_pos.ContainsKey(d) || Slot(s) is not { } man)
            {
                continue;
            }

            _pos[d] = CourtTracker.Clamp(CourtTracker.Between(_pos[man], _rim, man == _holder ? 1.0 : 1.4));
        }

        if (!final)
        {
            var settled = Snapshot();
            CourtSpacing.Separate(settled, anchored);
            Apply(settled);
        }

        _pinned.Clear();
        _moved.Clear();
        if (_holder is { } h)
        {
            _ball = _pos[h];
        }

        _keys.Add((weight, new CourtKey(0, Snapshot(), _ball, _holder, flight) { Screener = screener }));
    }

    private List<CourtPlayer> Snapshot() => [.. _c.Final.Select(p => p with { Position = _pos[p.Player] })];

    private void Apply(List<CourtPlayer> players)
    {
        foreach (var p in players)
        {
            _pos[p.Player] = p.Position;
        }
    }

    private void Pass(PlayerId? to, double weight)
    {
        if (to is not { } id || id == _holder || !_pos.ContainsKey(id))
        {
            return;
        }

        _holder = id;
        Key(weight, BallFlight.Pass);
    }

    /// <summary>Makes sure <paramref name="to"/> has the ball (a pass if somebody else holds it).</summary>
    private void Give(PlayerId? to, double weight) => Pass(to, weight);

    /// <summary>Scales the weights to <paramref name="total"/> seconds, nobody faster than <see cref="CourtTracker.MaxRunSpeed"/>.</summary>
    private List<CourtKey> Finish(double total)
    {
        var sum = _keys.Sum(k => k.Weight);
        var before = _start;
        var ballBefore = _c.Previous?.Ball ?? _ball;
        var keys = new List<CourtKey>(_keys.Count);
        foreach (var (weight, key) in _keys)
        {
            var run = key.Players.Max(p => before.TryGetValue(p.Player, out var b) ? b.DistanceTo(p.Position) : 0);
            var duration = Math.Max(total * weight / sum, Math.Max(run / CourtTracker.MaxRunSpeed, ballBefore.DistanceTo(key.Ball) / (CourtTracker.MaxRunSpeed * 2)));
            keys.Add(key with { Duration = Math.Round(duration, 3) });
            before = key.Players.ToDictionary(p => p.Player, p => p.Position);
            ballBefore = key.Ball;
        }

        return keys;
    }

    // ---------------------------------------------------------------- helpers

    private PlayerId? Handler => Slot(0);

    private bool IsAttacker(PlayerId id) => _c.Attack.Contains(id);

    private IEnumerable<PlayerId> Attackers() => _c.Attack.Where(p => p is not null && _pos.ContainsKey(p.Value)).Select(p => p!.Value);

    /// <summary>The attacker in a slot, or the first one when the slot is empty.</summary>
    private PlayerId? Slot(int s) =>
        s >= 0 && s < 5 && _c.Attack[s] is { } id && _pos.ContainsKey(id) ? id : Attackers().Cast<PlayerId?>().FirstOrDefault();

    private int SlotOf(PlayerId? id)
    {
        for (var s = 0; s < 5 && id is not null; s++)
        {
            if (_c.Attack[s] == id)
            {
                return s;
            }
        }

        return -1;
    }

    private PlayerId? DefenderOf(PlayerId? attacker) => SlotOf(attacker) is var s and >= 0 && _c.Defence[s] is { } d && _pos.ContainsKey(d) ? d : null;

    private bool InFrontcourt() => _holder is not null && ToHalf(_ball).D < Half;

    private void Move(PlayerId? id, double across, double depth)
    {
        if (id is { } p && _pos.ContainsKey(p))
        {
            _pos[p] = At(across, depth);
            _moved.Add(p);
        }
    }

    private void Pin(PlayerId? id, CourtPoint at)
    {
        if (id is { } p && _pos.ContainsKey(p))
        {
            _pos[p] = CourtTracker.Clamp(at);
            _pinned.Add(p);
        }
    }

    /// <summary>Moves the attackers not listed <paramref name="metres"/> towards the attacked basket.</summary>
    private void RunAhead(double metres, IReadOnlyCollection<PlayerId?> except)
    {
        foreach (var id in Attackers().Where(id => !except.Contains(id)))
        {
            var (a, d) = ToHalf(_pos[id]);
            Move(id, a, Math.Max(4, d - metres));
        }
    }

    private CourtPoint At(double across, double depth) => CourtTracker.Clamp(CourtTracker.ToCourt(_c.Offense, _c.Event.Period, across, depth));

    private (double A, double D) ToHalf(CourtPoint p) =>
        _attacksRight ? (p.Y - (CourtTracker.Width / 2), CourtTracker.Length - p.X) : ((CourtTracker.Width / 2) - p.Y, p.X);

    private double J(int slot, int axis) => CourtTracker.Jitter((_c.Index * 31) + _salt, slot, axis);

    private uint Hash(int salt)
    {
        unchecked
        {
            var h = ((uint)_c.Index * 2654435761u) ^ ((uint)salt * 40503u);
            h ^= h >> 16;
            h *= 0x7feb352d;
            h ^= h >> 15;
            return h;
        }
    }
}
