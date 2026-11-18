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

    /// <summary>Push the ball handler to the sideline, away from the screen: protects the rim, concedes mid-range.</summary>
    Ice,

    /// <summary>Go under the screen: takes away the drive, concedes the pull-up three.</summary>
    Under,
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

/// <summary>Which opponent a player guards in man-based defenses (docs/TACTICS.md §7).</summary>
public enum DefensiveMatchup
{
    /// <summary>The opponent in the same slot (a perimeter stopper takes the best scorer).</summary>
    Positional,

    /// <summary>The opponent with most points (before he scores: the main option).</summary>
    BestScorer,

    BestShooter,

    BestPlaymaker,

    BestBig,
}

/// <summary>Whether the offense hunts a favourable matchup against man-based defenses.</summary>
public enum MatchupTargeting
{
    Off,

    /// <summary>Attack the player guarded by the weakest defender.</summary>
    WeakestDefender,

    /// <summary>Post up the player with the largest height advantage over his defender.</summary>
    SizeMismatch,
}

/// <summary>How the coach calls the half-court plays.</summary>
public enum PlayCalling
{
    /// <summary>Free flow inside the system.</summary>
    Flow,

    /// <summary>Feed whoever is scoring tonight.</summary>
    HotHand,

    /// <summary>Called set plays: slower, better shots, fewer turnovers.</summary>
    SetPlays,
}

/// <summary>How the offense attacks a zone.</summary>
public enum ZoneOffense
{
    Standard,

    /// <summary>Overload one side for open threes.</summary>
    Overload,

    /// <summary>Flash a player to the high post: mid-range and post touches.</summary>
    HighPost,

    /// <summary>Drive the gaps of the zone.</summary>
    AttackGaps,
}

/// <summary>What the offense does in the last possessions of a quarter.</summary>
public enum EndOfQuarter
{
    Normal,

    /// <summary>Shoot early with 30–44 seconds left to get the last possession too.</summary>
    TwoForOne,

    /// <summary>Hold the ball for the last shot when the shot clock is off.</summary>
    HoldForLastShot,
}

/// <summary>How the coach handles players in foul trouble.</summary>
public enum FoulManagement
{
    Normal,

    /// <summary>Players in foul trouble defend carefully, and sit in the whole fourth quarter at four fouls.</summary>
    Protect,

    /// <summary>Nobody sits for fouls until he fouls out.</summary>
    Ignore,
}

/// <summary>How many players the coach uses.</summary>
public enum RotationStrategy
{
    Standard,

    /// <summary>An eight-man rotation: starters play through more fatigue.</summary>
    Tight,

    /// <summary>Rest early and often: fresher players, more bench minutes.</summary>
    Deep,
}
