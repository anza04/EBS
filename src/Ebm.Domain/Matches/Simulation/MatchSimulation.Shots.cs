using Ebm.Domain.Tactics;

namespace Ebm.Domain.Matches.Simulation;

/// <summary>Shot detail, decisions, clutch, transition and turnover causes (Step 29, docs/MATCH_ENGINE.md §9).</summary>
public sealed partial class MatchSimulation
{
    /// <summary>Everything the engine decided about one field-goal attempt.</summary>
    private readonly record struct ShotContext(
        ShotType Type,
        int X,
        int Y,
        ShotContest Contest,
        bool FastBreak,
        bool Clutch,
        Runner? Defender,
        Runner? KickOutPasser)
    {
        public ShotZone Zone => ShotZones.Classify(X, Y);
    }

    // ---------------------------------------------------------------- clutch

    /// <summary>Clutch time: last five minutes of the fourth quarter or an overtime, margin within five.</summary>
    private bool IsClutch() => Period >= _rules.Periods && _clock <= 300 && Math.Abs(_home.Score - _away.Score) <= 5;

    /// <summary>Clutch rating: 0.45 concentration + 0.35 consistency + 0.2 leadership.</summary>
    private static double ClutchRating(Runner r) =>
        (0.45 * r.A.Concentration) + (0.35 * r.A.Consistency) + (0.2 * r.A.Leadership);

    /// <summary>In clutch time the ball goes to the best and calmest players.</summary>
    private static double ClutchUsage(Team off, Runner r)
    {
        var slot = off.SlotOf(r);
        var rating = slot >= 0 ? r.Rating(Slots[slot]) : r.Rating(r.Player.Position);
        return Math.Pow(rating / 60.0, 1.5) * Math.Clamp(1 + (0.01 * (ClutchRating(r) - 50)), 0.6, 1.5);
    }

    private static double ClutchEdge(Runner r) => 0.0015 * (ClutchRating(r) - 50);

    // ---------------------------------------------------------------- tendencies and decisions

    /// <summary>Isolation players create more of their own shots (neutral at tendency 50).</summary>
    private static double IsolationUsage(Runner r) => 0.85 + (0.3 * r.T.Isolation / 100.0);

    /// <summary>Chance that a shooter facing a tight contest passes up the shot (once per shot).</summary>
    private static double PassUpChance(Team off, Runner shooter) => Math.Clamp(
        0.1
        + (0.006 * (shooter.A.DecisionMaking - 50))
        + (0.002 * (shooter.T.PassFirst - 50))
        - (0.002 * (shooter.T.Isolation - 50))
        - (0.08 * Level(off.Tactics.Offense.ShotSelection))
        + (0.04 * Level(off.Tactics.Offense.BallMovement)),
        0,
        0.45);

    /// <summary>Shooting-foul multiplier from the shooter's ability to draw contact (neutral at 50).</summary>
    private static double FoulDrawing(Runner shooter, ShotType type)
    {
        var draw = type is ShotType.Rim or ShotType.Post
            ? (0.4 * shooter.T.DriveToBasket) + (0.3 * shooter.A.Strength) + (0.3 * shooter.A.BallHandling)
            : (0.5 * shooter.A.BallHandling) + (0.5 * shooter.T.Isolation);
        return Math.Clamp(1 + (0.012 * (draw - 50)), 0.6, 1.5);
    }

    // ---------------------------------------------------------------- contest

    /// <summary>
    /// Contest quality: score <c>s</c> plus N(0, 1); below −0.5 open, above 0.55 tight. About 31% open and 29% tight
    /// for average players (docs/MATCH_ENGINE.md §9.2).
    /// </summary>
    private ShotContest Contest(Team off, Team def, Runner shooter, Runner? defender, ShotType type, bool fastBreak, bool secondary, bool kickOut, bool hurried)
    {
        var inside = type is ShotType.Rim or ShotType.Post;
        var o = off.Tactics.Offense;
        var d = def.Tactics.Defense;
        var a = shooter.A;
        double s;
        if (fastBreak)
        {
            s = -0.6;
        }
        else
        {
            var guard = defender is null ? 20.0
                : (inside
                    ? (0.6 * defender.A.InteriorDefense) + (0.2 * defender.A.DefensivePositioning) + (0.2 * defender.A.Block)
                    : (0.6 * defender.A.PerimeterDefense) + (0.2 * defender.A.DefensivePositioning) + (0.2 * defender.A.Agility))
                  * (0.7 + (0.3 * defender.Energy / 100.0));
            var create = inside
                ? (0.35 * a.Strength) + (0.35 * a.Speed) + (0.3 * a.Vertical)
                : (0.4 * a.BallHandling) + (0.3 * a.Agility) + (0.3 * a.Speed);
            var zone = TacticalModel.IsZone(def.Tactics.DefensiveSystem);
            s = (0.02 * (guard - 50)) - (0.008 * (create - 50))
                + (zone ? (inside ? 0.25 : -0.2) : 0)
                + (inside ? 0.12 : -0.08) * Level(d.HelpDefense)
                + (0.2 * Level(d.Intensity))
                - (0.2 * Level(o.BallMovement))
                + (0.15 * Level(o.ShotSelection))
                + (0.012 * (50 - a.DecisionMaking))
                + (0.003 * (shooter.T.Isolation - 50))
                - (defender is null ? 0 : 0.006 * (defender.T.DefensiveGambling - 50))
                + (inside && defender is not null ? 0.03 * (defender.Player.HeightCm - shooter.Player.HeightCm) : 0)
                - (secondary ? 0.35 : 0)
                - (kickOut ? 0.6 : 0)
                + (hurried ? 0.8 : 0);
        }

        s -= 0.8 * (5 - def.OnCourt.Count());
        var u = s + Normal(_random);
        return u < -0.5 ? ShotContest.Open : u > 0.55 ? ShotContest.Tight : ShotContest.Contested;
    }

    private static double ContestEdge(ShotContest contest) => contest switch
    {
        ShotContest.Open => 0.035,
        ShotContest.Tight => -0.04,
        _ => 0,
    };

    /// <summary>Tight contests draw more fouls, less so from disciplined (well-positioned) defenders.</summary>
    private static double ContestFoulFactor(ShotContest contest, Runner defender) => contest switch
    {
        ShotContest.Open => 0.6,
        ShotContest.Tight => 1.35,
        _ => 1.0,
    } * Math.Clamp(1 - (0.006 * (defender.A.DefensivePositioning - 50)), 0.7, 1.3);

    private static double ContestBlockFactor(ShotContest contest) => contest switch
    {
        ShotContest.Open => 0.5,
        ShotContest.Tight => 1.6,
        _ => 1.0,
    };

    // ---------------------------------------------------------------- location

    /// <summary>Zone of an attempt from its type, the shooter's role and the systems (docs/MATCH_ENGINE.md §9.1).</summary>
    private ShotZone PickZone(Team off, Team def, Runner shooter, ShotType type, bool fastBreak, bool hurried)
    {
        if (hurried)
        {
            return type == ShotType.Three ? ShotZone.TopThree : ShotZone.MidRangeCenter;
        }

        switch (type)
        {
            case ShotType.Rim:
                return _random.NextDouble() < (fastBreak ? 0.95 : 0.85) ? ShotZone.RestrictedArea : ShotZone.Paint;
            case ShotType.Post:
                return _random.NextDouble() < 0.3 ? ShotZone.RestrictedArea : ShotZone.Paint;
            case ShotType.MidRange:
                var m = _random.NextDouble();
                return m < 0.35 ? ShotZone.MidRangeLeft : m < 0.65 ? ShotZone.MidRangeCenter : ShotZone.MidRangeRight;
        }

        var role = shooter.Assignment.Role;
        var corner = 0.11
            * (role is TacticalRole.SpotUpShooter or TacticalRole.StretchBig ? 1.8 : 1)
            * (role is TacticalRole.PrimaryBallHandler or TacticalRole.ShotCreator ? 0.6 : 1)
            * (off.Tactics.OffensiveSystem == OffensiveSystem.FiveOut ? 1.3 : 1)
            * (def.Tactics.DefensiveSystem == DefensiveSystem.Zone23 ? 1.3 : 1);
        var top = 0.24 * (role is TacticalRole.PrimaryBallHandler or TacticalRole.ShotCreator ? 1.5 : 1);
        var wing = 0.27;
        var r = _random.NextDouble() * ((2 * corner) + (2 * wing) + top);
        return r < corner ? ShotZone.CornerThreeLeft
            : r < corner + wing ? ShotZone.WingThreeLeft
            : r < corner + wing + top ? ShotZone.TopThree
            : r < corner + (2 * wing) + top ? ShotZone.WingThreeRight
            : ShotZone.CornerThreeRight;
    }

    /// <summary>A random spot inside the zone (rejection sampling; jumpers within realistic distance).</summary>
    private (int X, int Y) Spot(ShotZone zone, bool hurried)
    {
        var (x0, x1, y0, y1, fallback) = zone switch
        {
            ShotZone.RestrictedArea => (-12, 12, 4, 28, (0, 12)),
            ShotZone.Paint => (-24, 24, 0, 58, (0, 45)),
            ShotZone.MidRangeLeft => (-65, -1, 0, 70, (-45, 20)),
            ShotZone.MidRangeCenter => (-35, 35, 30, 82, (0, 70)),
            ShotZone.MidRangeRight => (1, 65, 0, 70, (45, 20)),
            ShotZone.CornerThreeLeft => (-74, -66, 0, 29, (-70, 10)),
            ShotZone.CornerThreeRight => (66, 74, 0, 29, (70, 10)),
            ShotZone.WingThreeLeft => (-74, -20, 30, 95, (-55, 65)),
            ShotZone.WingThreeRight => (20, 74, 30, 95, (55, 65)),
            _ => (-35, 35, 80, hurried ? 140 : 100, (0, 88)),
        };
        var maxDistance = hurried ? 14.0 : ShotZones.IsThree(zone) ? 7.8 : 99;
        for (var i = 0; i < 40; i++)
        {
            var x = _random.Next(x0, x1 + 1);
            var y = _random.Next(y0, y1 + 1);
            if (ShotZones.Classify(x, y) == zone && ShotZones.Distance(x, y) <= maxDistance)
            {
                return (x, y);
            }
        }

        return fallback;
    }

    /// <summary>Make-probability edge of the spot: corner threes and shots at the rim are easier (about zero on average).</summary>
    private static double ZoneEdge(ShotType type, ShotZone zone, double distance) => (type, zone) switch
    {
        (ShotType.Rim, ShotZone.RestrictedArea) => 0.02,
        (ShotType.Rim, _) => -0.11,
        (ShotType.Post, ShotZone.RestrictedArea) => 0.05,
        (ShotType.Post, _) => -0.02,
        (_, ShotZone.CornerThreeLeft or ShotZone.CornerThreeRight) => 0.025,
        (_, ShotZone.WingThreeLeft or ShotZone.WingThreeRight) => -0.004,
        (_, ShotZone.TopThree) => -0.008 - (Math.Max(0, distance - 7.5) * 0.03),
        _ => distance < 4 ? 0.02 : distance > 5.5 ? -0.02 : 0,
    };

    // ---------------------------------------------------------------- rebounds and transition

    /// <summary>Rebound weight from court position: long rebounds off threes go to guards, misses inside to bigs.</summary>
    private static double PositionBoard(Team team, Runner r, ShotZone? zone, Runner? shooter)
    {
        if (zone is not { } z)
        {
            return 1;
        }

        var slot = team.SlotOf(r);
        var guard = slot is 0 or 1;
        var big = slot is 3 or 4;
        var factor = ShotZones.IsThree(z) ? (guard ? 1.35 : big ? 0.85 : 1.1)
            : ShotZones.IsInside(z) ? (guard ? 0.8 : big ? 1.15 : 1.0)
            : 1.0;
        return r == shooter && ShotZones.IsInside(z) ? factor * 1.3 : factor;
    }

    /// <summary>Fast-break edge of the running team's speed over the retreating team's (±0.1).</summary>
    private static double SpeedEdge(Team running, Team back) => Math.Clamp(0.004 * (running.MeanSpeed - back.MeanSpeed), -0.1, 0.1);

    /// <summary>Chance of a secondary break (early offense against a defense not yet set) when no fast break follows.</summary>
    private static double SecondaryBreakChance(Team running) => Math.Clamp(
        0.16 + (0.08 * Level(running.Tactics.Offense.Tempo)) + (running.Tactics.OffensiveSystem == OffensiveSystem.Transition ? 0.1 : 0),
        0,
        0.5);

    /// <summary>Sets the next possession's transition: fast break with the given chance, else maybe a secondary break.</summary>
    private void Transition(Team running, double fastBreakChance)
    {
        _nextFastBreak = _random.NextDouble() < Math.Clamp(fastBreakChance, 0, 0.9);
        _nextSecondary = !_nextFastBreak && _random.NextDouble() < SecondaryBreakChance(running);
    }

    // ---------------------------------------------------------------- turnover causes

    private static double PassRisk(Team off, Runner r) =>
        (110 - r.A.Passing) * (0.5 + (r.T.PassFirst / 100.0)) * (1 + (0.15 * Level(off.Tactics.Offense.BallMovement)));

    private static double DribbleRisk(Runner r) => (110 - r.A.BallHandling) * (0.5 + (r.T.Isolation / 100.0));

    private TurnoverCause DeadBallCause(Team off, Runner loser)
    {
        var pass = PassRisk(off, loser);
        var dribble = DribbleRisk(loser);
        var r = _random.NextDouble() * (pass + (1.1 * dribble));
        return r < pass ? TurnoverCause.BadPass : r < pass + (0.6 * dribble) ? TurnoverCause.Travelling : TurnoverCause.OutOfBounds;
    }
}
