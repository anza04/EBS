using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;

namespace Ebm.Domain.Matches.Visualization;

/// <summary>A point on the full court in metres: <see cref="X"/> 0–28 from the left baseline, <see cref="Y"/> 0–15 across.</summary>
public readonly record struct CourtPoint(double X, double Y)
{
    public CourtPoint Lerp(CourtPoint to, double t) => new(X + ((to.X - X) * t), Y + ((to.Y - Y) * t));

    public double DistanceTo(CourtPoint other) => Math.Sqrt(((other.X - X) * (other.X - X)) + ((other.Y - Y) * (other.Y - Y)));
}

/// <summary>What a court frame shows (Step 30).</summary>
public enum CourtAction
{
    /// <summary>Tip-off at the centre circle.</summary>
    TipOff,
    Shot,
    FreeThrow,
    Rebound,
    Turnover,
    Foul,
    Substitution,

    /// <summary>Dead ball: timeout, tactics, situation notes, end of a period or the match.</summary>
    DeadBall,
}

/// <summary>A player on the court in a frame.</summary>
public sealed record CourtPlayer(PlayerId Player, TeamSide Side, int Slot, CourtPoint Position);

/// <summary>
/// One play-by-play event placed on the court: the ten players, and the ball moving from <see cref="BallFrom"/> to
/// <see cref="Ball"/> (in the air for shots and free throws). <see cref="Index"/> is the event's index in the match.
/// </summary>
public sealed record CourtFrame(
    int Index,
    MatchEvent Event,
    CourtAction Action,
    TeamSide? Offense,
    IReadOnlyList<CourtPlayer> Players,
    CourtPoint BallFrom,
    CourtPoint Ball,
    PlayerId? BallHolder,
    bool BallInAir)
{
    public CourtPlayer? Find(PlayerId player) => Players.FirstOrDefault(p => p.Player == player);
}

/// <summary>
/// Places the match engine's play-by-play on a 2D FIBA court (docs/MATCH_VISUALIZATION.md). The engine is the only
/// source of truth: the tracker reads <see cref="MatchEvent"/>s and never touches the simulation or its random
/// stream, so a match plays identically with or without it. Positions are a deterministic function of the events
/// (formations, matchups, the recorded shot spot), so the same match always draws the same way.
/// </summary>
public sealed class CourtTracker
{
    public const double Length = 28;
    public const double Width = 15;
    public const double RimDistance = 1.575;

    /// <summary>Half-court spots (across, depth from the baseline) of the five slots: PG, SG, SF, PF, C.</summary>
    private static readonly (double A, double D)[] Formation = [(0.0, 8.2), (-5.4, 6.0), (6.6, 0.9), (3.2, 4.6), (-2.4, 2.2)];

    /// <summary>Free-throw lane spots: defenders nearest the basket, then attackers, then the second defender pair.</summary>
    private static readonly (double A, double D)[] DefenceLane = [(-2.65, 1.75), (2.65, 1.75), (-2.65, 3.95), (0.0, 9.5), (2.65, 3.95)];
    private static readonly (double A, double D)[] OffenceLane = [(-2.65, 2.85), (2.65, 2.85), (3.5, 9.0), (-3.5, 9.0)];

    private readonly PlayerId?[] _home = new PlayerId?[5];
    private readonly PlayerId?[] _away = new PlayerId?[5];
    private readonly List<CourtFrame> _frames = [];
    private TeamSide? _offense;

    /// <param name="homeStarters">Home starters in slot order (PG, SG, SF, PF, C); a null leaves the slot empty.</param>
    public CourtTracker(IReadOnlyList<PlayerId?> homeStarters, IReadOnlyList<PlayerId?> awayStarters)
    {
        ArgumentNullException.ThrowIfNull(homeStarters);
        ArgumentNullException.ThrowIfNull(awayStarters);
        for (var i = 0; i < 5; i++)
        {
            _home[i] = i < homeStarters.Count ? homeStarters[i] : null;
            _away[i] = i < awayStarters.Count ? awayStarters[i] : null;
        }
    }

    /// <summary>A tracker for a simulation: its starters by starting position, as the engine puts them on court.</summary>
    public static CourtTracker For(MatchSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        return new CourtTracker(Starters(simulation.Lines(TeamSide.Home)), Starters(simulation.Lines(TeamSide.Away)));
    }

    /// <summary>The frames placed so far, one per event.</summary>
    public IReadOnlyList<CourtFrame> Frames => _frames;

    /// <summary>Players on court now (after the last event placed).</summary>
    public IReadOnlyList<PlayerId> OnCourt(TeamSide side) => [.. Slots(side).Where(p => p is not null).Select(p => p!.Value)];

    /// <summary>Places the events not placed yet (the list is the match's play-by-play from the start) and returns them.</summary>
    public IReadOnlyList<CourtFrame> Update(IReadOnlyList<MatchEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var first = _frames.Count;
        for (var i = first; i < events.Count; i++)
        {
            _frames.Add(Place(i, events[i]));
        }

        return _frames[first..];
    }

    /// <summary>Which basket a side attacks: home attacks the right basket in the first half, the left one after.</summary>
    public static bool AttacksRight(TeamSide side, int period) => (side == TeamSide.Home) == (period <= 2);

    /// <summary>The rim a side attacks in a period.</summary>
    public static CourtPoint Rim(TeamSide side, int period) =>
        AttacksRight(side, period) ? new CourtPoint(Length - RimDistance, Width / 2) : new CourtPoint(RimDistance, Width / 2);

    /// <summary>Half-court metres (across, depth from the attacked baseline) to the full court.</summary>
    public static CourtPoint ToCourt(TeamSide offense, int period, double across, double depth) =>
        AttacksRight(offense, period) ? new CourtPoint(Length - depth, (Width / 2) + across) : new CourtPoint(depth, (Width / 2) - across);

    /// <summary>A recorded shot's spot on the full court.</summary>
    public static CourtPoint ShotSpot(TeamSide offense, ShotRecord shot) => ToCourt(offense, shot.Period, shot.X / 10.0, shot.Y / 10.0);

    private static PlayerId?[] Starters(IReadOnlyList<PlayerLine> lines)
    {
        var slots = new PlayerId?[5];
        foreach (var line in lines.Where(l => l.StartingPosition is not null))
        {
            slots[(int)line.StartingPosition!.Value] = line.Player.Id;
        }

        return slots;
    }

    private static TeamSide Other(TeamSide side) => side == TeamSide.Home ? TeamSide.Away : TeamSide.Home;

    private PlayerId?[] Slots(TeamSide side) => side == TeamSide.Home ? _home : _away;

    private CourtFrame Place(int index, MatchEvent e)
    {
        var previous = _frames.Count > 0 ? _frames[^1] : null;
        var lastBall = previous?.Ball ?? new CourtPoint(Length / 2, Width / 2);
        switch (e.Kind)
        {
            case MatchEventKind.PeriodStart:
            case MatchEventKind.JumpBall:
                return TipOff(index, e, lastBall);
            case MatchEventKind.FieldGoalMade:
            case MatchEventKind.FieldGoalMissed:
                return Shot(index, e, e.Side!.Value);
            case MatchEventKind.FreeThrowMade:
            case MatchEventKind.FreeThrowMissed:
                return FreeThrow(index, e, e.Side!.Value);
            case MatchEventKind.OffensiveRebound:
                return Rebound(index, e, e.Side!.Value, lastBall);
            case MatchEventKind.DefensiveRebound:
                return Rebound(index, e, Other(e.Side!.Value), lastBall);
            case MatchEventKind.Turnover:
            case MatchEventKind.ShotClockViolation:
                return Turnover(index, e, e.Side!.Value);
            case MatchEventKind.Foul:
                return Foul(index, e, Other(e.Side!.Value));
            case MatchEventKind.Substitution:
                return Substitution(index, e, previous, lastBall);
            case MatchEventKind.FouledOut:
                var slot = Array.IndexOf(Slots(e.Side!.Value), e.Player);
                if (slot >= 0)
                {
                    Slots(e.Side.Value)[slot] = null;
                }

                return Still(index, e, CourtAction.DeadBall, previous, lastBall);
            default:
                return Still(index, e, CourtAction.DeadBall, previous, lastBall);
        }
    }

    private CourtFrame TipOff(int index, MatchEvent e, CourtPoint lastBall)
    {
        var centre = new CourtPoint(Length / 2, Width / 2);
        var players = new List<CourtPlayer>();
        foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
        {
            // Each team stands in the half it defends; the centres jump in the circle.
            var back = AttacksRight(side, e.Period) ? -1 : 1;
            (double Dx, double Dy)[] spots = [(4.5, 0), (2.5, -4.5), (2.5, 4.5), (1.2, -2.2), (0.5, 0)];
            for (var s = 0; s < 5; s++)
            {
                if (Slots(side)[s] is { } id)
                {
                    players.Add(new CourtPlayer(id, side, s, new CourtPoint(centre.X + (back * spots[s].Dx), centre.Y + spots[s].Dy)));
                }
            }
        }

        _offense = e.Kind == MatchEventKind.JumpBall ? e.Side : null;
        var holder = e.Kind == MatchEventKind.JumpBall && e.Side is { } winner ? Slots(winner)[0] ?? e.Player : null;
        var ball = holder is { } h ? players.First(p => p.Player == h).Position : centre;
        return new CourtFrame(index, e, CourtAction.TipOff, _offense, players, e.Kind == MatchEventKind.JumpBall ? centre : lastBall, ball, holder, false);
    }

    /// <summary>The offense in its formation and each defender between his man and the basket.</summary>
    private List<CourtPlayer> HalfCourt(int index, MatchEvent e, TeamSide offense, Func<int, (double A, double D)?>? spot = null)
    {
        var players = new List<CourtPlayer>(10);
        var attack = Slots(offense);
        var rim = Rim(offense, e.Period);
        for (var s = 0; s < 5; s++)
        {
            if (attack[s] is not { } id)
            {
                continue;
            }

            var (a, d) = spot?.Invoke(s) ?? (Formation[s].A + Jitter(index, s, 0), Formation[s].D + Jitter(index, s, 1));
            players.Add(new CourtPlayer(id, offense, s, Clamp(ToCourt(offense, e.Period, a, d))));
        }

        var defence = Slots(Other(offense));
        for (var s = 0; s < 5; s++)
        {
            if (defence[s] is not { } id)
            {
                continue;
            }

            var man = players.FirstOrDefault(p => p.Slot == s) ?? players.FirstOrDefault();
            var position = man is null ? rim : Between(man.Position, rim, 1.3);
            players.Add(new CourtPlayer(id, Other(offense), s, Clamp(position)));
        }

        return players;
    }

    private CourtFrame Shot(int index, MatchEvent e, TeamSide offense)
    {
        _offense = offense;
        var players = HalfCourt(index, e, offense);
        var rim = Rim(offense, e.Period);
        var shooter = Move(players, e.Player, e.Shot is { } shot ? ShotSpot(offense, shot) : Between(rim, new CourtPoint(Length / 2, Width / 2), -1.2));
        if (shooter is not null)
        {
            // The shooter's closest defender: the blocker, or his matchup at the contest distance.
            var gap = e.Shot?.Contest switch { ShotContest.Open => 2.8, ShotContest.Tight => 0.6, _ => 1.2 };
            var defender = e.Kind == MatchEventKind.FieldGoalMissed && e.OtherPlayer is { } blocker
                ? players.FirstOrDefault(p => p.Player == blocker && p.Side != offense)
                : players.FirstOrDefault(p => p.Side != offense && p.Slot == shooter.Slot);
            if (defender is not null)
            {
                Move(players, defender.Player, Between(shooter.Position, rim, e.Shot?.Blocked == true ? 0.4 : gap));
            }
        }

        return new CourtFrame(index, e, CourtAction.Shot, offense, players, shooter?.Position ?? rim, rim, null, true);
    }

    private CourtFrame FreeThrow(int index, MatchEvent e, TeamSide offense)
    {
        _offense = offense;
        var shooterSlot = Array.IndexOf(Slots(offense), e.Player);
        var lane = 0;
        var players = HalfCourt(index, e, offense, s => s == shooterSlot ? (0.0, 5.8) : OffenceLane[lane++ % OffenceLane.Length]);
        var rim = Rim(offense, e.Period);
        var defenders = players.Where(p => p.Side != offense).ToList();
        for (var i = 0; i < defenders.Count; i++)
        {
            Move(players, defenders[i].Player, ToCourt(offense, e.Period, DefenceLane[i].A, DefenceLane[i].D));
        }

        var shooter = e.Player is { } id ? players.FirstOrDefault(p => p.Player == id) : null;
        return new CourtFrame(index, e, CourtAction.FreeThrow, offense, players, shooter?.Position ?? ToCourt(offense, e.Period, 0, 5.8), rim, null, true);
    }

    /// <summary>A rebound at the basket <paramref name="attacked"/> was attacking; the ball goes to the rebounder.</summary>
    private CourtFrame Rebound(int index, MatchEvent e, TeamSide attacked, CourtPoint lastBall)
    {
        var players = HalfCourt(index, e, attacked);
        var spot = ToCourt(attacked, e.Period, Jitter(index, 7, 0) * 2.5, 2.4 + Math.Abs(Jitter(index, 7, 1) * 2));
        var rebounder = Move(players, e.Player, spot);
        _offense = e.Side;
        return new CourtFrame(index, e, CourtAction.Rebound, e.Side, players, lastBall, rebounder?.Position ?? spot, e.Player, false);
    }

    private CourtFrame Turnover(int index, MatchEvent e, TeamSide offense)
    {
        var players = HalfCourt(index, e, offense);
        var loser = e.Player is { } id ? players.FirstOrDefault(p => p.Player == id) : players.FirstOrDefault(p => p.Side == offense);
        var from = loser?.Position ?? Rim(offense, e.Period);
        PlayerId? holder = loser?.Player;
        var to = from;
        if (e.OtherPlayer is { } thief && loser is not null)
        {
            // The steal: the thief jumps the passing lane towards the ball handler.
            var stealer = Move(players, thief, Between(from, Rim(offense, e.Period), 0.9));
            holder = thief;
            to = stealer?.Position ?? from;
        }

        _offense = Other(offense);
        return new CourtFrame(index, e, CourtAction.Turnover, offense, players, from, to, holder, false);
    }

    private CourtFrame Foul(int index, MatchEvent e, TeamSide offense)
    {
        var players = HalfCourt(index, e, offense);
        var fouled = e.OtherPlayer is { } id ? players.FirstOrDefault(p => p.Player == id) : null;
        if (fouled is not null && e.Player is { } fouler)
        {
            Move(players, fouler, Between(fouled.Position, Rim(offense, e.Period), 0.6));
        }

        var ball = fouled?.Position ?? players.FirstOrDefault(p => p.Side == offense)?.Position ?? Rim(offense, e.Period);
        return new CourtFrame(index, e, CourtAction.Foul, offense, players, ball, ball, fouled?.Player, false);
    }

    private CourtFrame Substitution(int index, MatchEvent e, CourtFrame? previous, CourtPoint lastBall)
    {
        var slots = Slots(e.Side!.Value);
        var slot = e.OtherPlayer is { } leaving ? Array.IndexOf(slots, leaving) : -1;
        if (slot < 0)
        {
            slot = Array.IndexOf(slots, null);
        }

        if (slot >= 0)
        {
            slots[slot] = e.Player;
        }

        return Still(index, e, CourtAction.Substitution, previous, lastBall);
    }

    /// <summary>The previous layout with the current line-ups: an entering player takes the spot of the one leaving.</summary>
    private CourtFrame Still(int index, MatchEvent e, CourtAction action, CourtFrame? previous, CourtPoint lastBall)
    {
        var players = new List<CourtPlayer>(10);
        foreach (var side in new[] { TeamSide.Home, TeamSide.Away })
        {
            var slots = Slots(side);
            for (var s = 0; s < 5; s++)
            {
                if (slots[s] is not { } id)
                {
                    continue;
                }

                var position = previous?.Players.FirstOrDefault(p => p.Side == side && p.Slot == s)?.Position
                    ?? (_offense is { } o ? HalfCourtSpot(index, e.Period, o, side, s) : new CourtPoint(Length / 2, Width / 2));
                players.Add(new CourtPlayer(id, side, s, position));
            }
        }

        var holder = previous?.BallHolder is { } h && players.Any(p => p.Player == h) ? h : (PlayerId?)null;
        return new CourtFrame(index, e, action, _offense, players, lastBall, lastBall, holder, false);
    }

    private static CourtPoint HalfCourtSpot(int index, int period, TeamSide offense, TeamSide side, int slot)
    {
        var spot = ToCourt(offense, period, Formation[slot].A + Jitter(index, slot, 0), Formation[slot].D + Jitter(index, slot, 1));
        return side == offense ? spot : Between(spot, Rim(offense, period), 1.3);
    }

    /// <summary>Moves a player of the list and returns his new entry.</summary>
    private static CourtPlayer? Move(List<CourtPlayer> players, PlayerId? player, CourtPoint to)
    {
        var i = player is { } id ? players.FindIndex(p => p.Player == id) : -1;
        if (i < 0)
        {
            return null;
        }

        players[i] = players[i] with { Position = Clamp(to) };
        return players[i];
    }

    /// <summary><paramref name="distance"/> metres from <paramref name="from"/> towards <paramref name="towards"/>.</summary>
    private static CourtPoint Between(CourtPoint from, CourtPoint towards, double distance)
    {
        var length = from.DistanceTo(towards);
        return length < 1e-6 ? from : from.Lerp(towards, Math.Min(distance / length, 1));
    }

    private static CourtPoint Clamp(CourtPoint p) => new(Math.Clamp(p.X, 0.1, Length - 0.1), Math.Clamp(p.Y, 0.1, Width - 0.1));

    /// <summary>A small deterministic offset (±0.5 m) so the formation breathes from play to play.</summary>
    private static double Jitter(int index, int slot, int axis)
    {
        unchecked
        {
            var h = (uint)((index * 73856093) ^ (slot * 19349663) ^ (axis * 83492791));
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return ((h % 1001) / 1000.0) - 0.5;
        }
    }
}
