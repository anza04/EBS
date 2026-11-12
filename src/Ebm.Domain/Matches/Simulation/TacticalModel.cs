using Ebm.Domain.Tactics;

namespace Ebm.Domain.Matches.Simulation;

/// <summary>Shot zones the engine distinguishes.</summary>
public enum ShotType
{
    /// <summary>Layups and dunks at the rim (two points).</summary>
    Rim,

    /// <summary>Post-ups and close shots (two points).</summary>
    Post,

    /// <summary>Mid-range jumpers (two points).</summary>
    MidRange,

    Three,
}

/// <summary>
/// How tactics change the engine's numbers. Every value is documented in docs/MATCH_ENGINE.md §3; levels are read with
/// <see cref="TacticLevel.Offset"/> (−1 … +1).
/// </summary>
public static class TacticalModel
{
    /// <summary>Multiplier of each shot type's weight from the offensive system.</summary>
    public static double ShotMix(OffensiveSystem system, ShotType type) => (system, type) switch
    {
        (OffensiveSystem.PickAndRoll, ShotType.Rim) => 1.2,
        (OffensiveSystem.PickAndRoll, ShotType.Post) => 0.7,
        (OffensiveSystem.FiveOut, ShotType.Three) => 1.35,
        (OffensiveSystem.FiveOut, ShotType.Rim) => 1.1,
        (OffensiveSystem.FiveOut, ShotType.Post) => 0.4,
        (OffensiveSystem.FourOutOneIn, ShotType.Three) => 1.15,
        (OffensiveSystem.FourOutOneIn, ShotType.Post) => 1.3,
        (OffensiveSystem.FourOutOneIn, ShotType.MidRange) => 0.8,
        (OffensiveSystem.PostOriented, ShotType.Post) => 1.9,
        (OffensiveSystem.PostOriented, ShotType.Three) => 0.8,
        (OffensiveSystem.Isolation, ShotType.MidRange) => 1.45,
        (OffensiveSystem.Isolation, ShotType.Three) => 0.9,
        (OffensiveSystem.Transition, ShotType.Rim) => 1.2,
        (OffensiveSystem.Transition, ShotType.Post) => 0.6,
        _ => 1.0,
    };

    /// <summary>Multiplier of each shot type's weight from the defensive system: what the defense gives up.</summary>
    public static double ShotMix(DefensiveSystem system, ShotType type) => (system, type) switch
    {
        (DefensiveSystem.Zone23, ShotType.Three) => 1.2,
        (DefensiveSystem.Zone23, ShotType.Rim) => 0.85,
        (DefensiveSystem.Zone23, ShotType.Post) => 0.8,
        (DefensiveSystem.Zone32, ShotType.Three) => 0.85,
        (DefensiveSystem.Zone32, ShotType.Post) => 1.2,
        (DefensiveSystem.Zone32, ShotType.Rim) => 1.1,
        (DefensiveSystem.Zone131, ShotType.Three) => 1.15,
        (DefensiveSystem.Zone131, ShotType.MidRange) => 0.9,
        (DefensiveSystem.FullCourtPress, ShotType.Rim) => 1.15,
        (DefensiveSystem.HalfCourtPress, ShotType.Rim) => 1.1,
        _ => 1.0,
    };

    /// <summary>Make-probability change of a shot type from the offense–defense system pairing.</summary>
    public static double MakeEdge(OffensiveSystem offense, DefensiveSystem defense, ShotType type)
    {
        var edge = (defense, type) switch
        {
            (DefensiveSystem.Zone23, ShotType.Rim or ShotType.Post) => -0.03,
            (DefensiveSystem.Zone23, ShotType.Three) => 0.015,
            (DefensiveSystem.Zone32, ShotType.Three) => -0.02,
            (DefensiveSystem.Zone32, ShotType.Post) => 0.02,
            (DefensiveSystem.Zone131, ShotType.Three) => 0.01,
            (DefensiveSystem.FullCourtPress, ShotType.Rim) => 0.03,
            (DefensiveSystem.HalfCourtPress, ShotType.Rim) => 0.02,
            _ => 0.0,
        };

        // Systems built to beat zones (spacing and ball movement) or man-to-man (isolations and post-ups).
        var zone = defense is DefensiveSystem.Zone23 or DefensiveSystem.Zone32 or DefensiveSystem.Zone131;
        edge += (offense, zone) switch
        {
            (OffensiveSystem.FiveOut or OffensiveSystem.MotionOffense, true) => 0.015,
            (OffensiveSystem.Isolation, true) => -0.02,
            (OffensiveSystem.Isolation or OffensiveSystem.PostOriented, false) => 0.01,
            _ => 0.0,
        };
        return edge;
    }

    /// <summary>Turnover-rate change from the defensive system.</summary>
    public static double TurnoverEdge(DefensiveSystem defense) => defense switch
    {
        DefensiveSystem.FullCourtPress => 0.035,
        DefensiveSystem.HalfCourtPress => 0.02,
        DefensiveSystem.Zone131 => 0.012,
        DefensiveSystem.Zone23 or DefensiveSystem.Zone32 => -0.005,
        _ => 0.0,
    };

    /// <summary>Change of the mean seconds of a possession from the offensive system.</summary>
    public static double PaceSeconds(OffensiveSystem system) => system switch
    {
        OffensiveSystem.Transition => -1.5,
        OffensiveSystem.PostOriented => 1.0,
        OffensiveSystem.MotionOffense => 0.5,
        OffensiveSystem.Isolation => 0.5,
        _ => 0.0,
    };

    /// <summary>Change of the mean seconds of a possession from the defense facing it.</summary>
    public static double PaceSeconds(DefensiveSystem system) => system switch
    {
        DefensiveSystem.FullCourtPress => -1.5,
        DefensiveSystem.HalfCourtPress => -0.5,
        DefensiveSystem.Zone23 or DefensiveSystem.Zone32 or DefensiveSystem.Zone131 => 0.75,
        _ => 0.0,
    };

    /// <summary>Extra energy drain (share of the base drain) of the defense played.</summary>
    public static double DefensiveLoad(DefensiveSystem system) => system switch
    {
        DefensiveSystem.FullCourtPress => 0.35,
        DefensiveSystem.HalfCourtPress => 0.2,
        DefensiveSystem.Zone23 or DefensiveSystem.Zone32 or DefensiveSystem.Zone131 => -0.1,
        _ => 0.0,
    };

    /// <summary>Offensive-rebound probability change from the systems and rebounding priorities.</summary>
    public static double OffensiveReboundEdge(TeamTactics offense, TeamTactics defense)
    {
        ArgumentNullException.ThrowIfNull(offense);
        ArgumentNullException.ThrowIfNull(defense);
        var edge = offense.Defense.Rebounding switch
        {
            ReboundingPriority.CrashBoards => 0.04,
            ReboundingPriority.GetBack => -0.05,
            _ => 0.0,
        };
        edge += defense.Defense.Rebounding switch
        {
            ReboundingPriority.CrashBoards => -0.03,
            ReboundingPriority.GetBack => 0.03,
            _ => 0.0,
        };
        edge += offense.OffensiveSystem switch
        {
            OffensiveSystem.PostOriented => 0.03,
            OffensiveSystem.FiveOut => -0.04,
            OffensiveSystem.Transition => -0.02,
            _ => 0.0,
        };
        edge += defense.DefensiveSystem switch
        {
            DefensiveSystem.Zone23 => 0.02,
            DefensiveSystem.Zone131 or DefensiveSystem.FullCourtPress => 0.03,
            _ => 0.0,
        };
        return edge;
    }

    /// <summary>Chance of a fast break after a defensive rebound, from the rebounding team's tempo and the opponent's priority.</summary>
    public static double FastBreakAfterRebound(TeamTactics rebounding, TeamTactics opponent)
    {
        ArgumentNullException.ThrowIfNull(rebounding);
        ArgumentNullException.ThrowIfNull(opponent);
        var chance = 0.11 + (0.06 * TacticLevel.Offset(rebounding.Offense.Tempo));
        chance += rebounding.OffensiveSystem == OffensiveSystem.Transition ? 0.08 : 0;
        chance += opponent.Defense.Rebounding switch
        {
            ReboundingPriority.CrashBoards => 0.05,
            ReboundingPriority.GetBack => -0.05,
            _ => 0,
        };
        return Math.Clamp(chance, 0.02, 0.4);
    }

    /// <summary>Make-probability change of a pick-and-roll play from the coverage (docs/MATCH_ENGINE.md §3).</summary>
    public static double PickAndRollEdge(PickAndRollCoverage coverage, ShotType type) => (coverage, type) switch
    {
        (PickAndRollCoverage.Drop, ShotType.MidRange) => 0.03,
        (PickAndRollCoverage.Drop, ShotType.Rim) => -0.02,
        (PickAndRollCoverage.Hedge, ShotType.Rim) => 0.015,
        (PickAndRollCoverage.Blitz, ShotType.Three) => 0.03,
        (PickAndRollCoverage.Blitz, ShotType.Rim) => 0.02,
        _ => 0.0,
    };

    /// <summary>Turnover change of a pick-and-roll play from the coverage.</summary>
    public static double PickAndRollTurnovers(PickAndRollCoverage coverage) => coverage switch
    {
        PickAndRollCoverage.Blitz => 0.03,
        PickAndRollCoverage.Hedge => 0.012,
        _ => 0.0,
    };
}
