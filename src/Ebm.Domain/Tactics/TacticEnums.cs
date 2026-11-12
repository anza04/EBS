namespace Ebm.Domain.Tactics;

/// <summary>The team's half-court offense. Effects on the match engine: docs/TACTICS.md §2.</summary>
public enum OffensiveSystem
{
    MotionOffense,
    PickAndRoll,
    FiveOut,
    FourOutOneIn,
    PostOriented,
    Isolation,
    Transition,
}

/// <summary>The team's defense. Effects on the match engine: docs/TACTICS.md §3.</summary>
public enum DefensiveSystem
{
    ManToMan,
    Zone23,
    Zone32,
    Zone131,
    FullCourtPress,
    HalfCourtPress,
}

/// <summary>Where the offense looks for its shots.</summary>
public enum OffensiveFocus
{
    Balanced,
    Inside,
    Perimeter,

    /// <summary>The players with the highest shot priority take more of the shots.</summary>
    KeyPlayers,
}

/// <summary>Which defenders switch on screens.</summary>
public enum SwitchingPolicy
{
    Never,
    SameSize,
    Everything,
}

/// <summary>How the screener's defender plays the pick and roll.</summary>
public enum PickAndRollCoverage
{
    Drop,
    Hedge,
    Switch,
    Blitz,
}

/// <summary>Whether players crash the defensive glass or get back against the fast break.</summary>
public enum ReboundingPriority
{
    GetBack,
    Balanced,
    CrashBoards,
}

/// <summary>A player's job in the tactic. Suggested from his attributes when not assigned (docs/TACTICS.md §4).</summary>
public enum TacticalRole
{
    PrimaryBallHandler,
    ShotCreator,
    SpotUpShooter,
    Slasher,
    PostScorer,
    RollMan,
    StretchBig,
    RimProtector,
    PerimeterStopper,
    Rebounder,
}
