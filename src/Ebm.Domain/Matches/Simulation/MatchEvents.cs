using Ebm.Domain.Players;
using Ebm.Domain.Tactics;

namespace Ebm.Domain.Matches.Simulation;

public enum MatchEventKind
{
    PeriodStart,
    JumpBall,
    FieldGoalMade,
    FieldGoalMissed,
    FreeThrowMade,
    FreeThrowMissed,
    OffensiveRebound,
    DefensiveRebound,
    Turnover,
    ShotClockViolation,
    Foul,
    FouledOut,
    Substitution,
    Timeout,
    TacticsChanged,

    /// <summary>A situational play: intentional foul, two-for-one, last-shot play, set play after a timeout (Step 27).</summary>
    Situation,
    PeriodEnd,
    GameEnd,
}

/// <summary>
/// One play-by-play entry. <paramref name="Clock"/> is the game clock (seconds left in the period) when it happened;
/// the score is the score after it. <paramref name="Text"/> is the commentary line.
/// </summary>
public sealed record MatchEvent(
    int Period,
    int Clock,
    MatchEventKind Kind,
    TeamSide? Side,
    PlayerId? Player,
    PlayerId? OtherPlayer,
    int Points,
    int HomeScore,
    int AwayScore,
    string Text)
{
    /// <summary>"Q1 07:45", "OT1 03:12".</summary>
    public string Time => $"{PeriodName(Period)} {Clock / 60:00}:{Clock % 60:00}";

    public static string PeriodName(int period) => period <= 4 ? $"Q{period}" : $"OT{period - 4}";

    /// <summary>The attempt of a field-goal event (its spot on the court, contest, block; Step 30); null otherwise.</summary>
    public ShotRecord? Shot { get; init; }

    /// <summary>The side's offensive system after a <see cref="MatchEventKind.TacticsChanged"/> event (for the court); null otherwise.</summary>
    public OffensiveSystem? Offense { get; init; }
}

/// <summary>A player's running statistics and condition during a simulated match.</summary>
public sealed class PlayerLine
{
    internal PlayerLine(Player player, Position? startingPosition)
    {
        Player = player;
        StartingPosition = startingPosition;
    }

    public Player Player { get; }

    public Position? StartingPosition { get; }

    public bool IsStarter => StartingPosition is not null;

    public double SecondsPlayed { get; internal set; }

    public int TwoPointMade { get; internal set; }

    public int TwoPointAttempted { get; internal set; }

    public int ThreePointMade { get; internal set; }

    public int ThreePointAttempted { get; internal set; }

    public int FreeThrowMade { get; internal set; }

    public int FreeThrowAttempted { get; internal set; }

    public int OffensiveRebounds { get; internal set; }

    public int DefensiveRebounds { get; internal set; }

    public int Assists { get; internal set; }

    public int Steals { get; internal set; }

    public int Blocks { get; internal set; }

    public int Turnovers { get; internal set; }

    public int PersonalFouls { get; internal set; }

    public int PointsFor { get; internal set; }

    public int PointsAgainst { get; internal set; }

    public int PlusMinus => PointsFor - PointsAgainst;

    public int OffensivePossessions { get; internal set; }

    public int DefensivePossessions { get; internal set; }

    public int DefendedAttempted { get; internal set; }

    public int DefendedMade { get; internal set; }

    /// <summary>Field-goal attempts in order (Step 29).</summary>
    public IReadOnlyList<ShotRecord> Shots => _shots;

    private readonly List<ShotRecord> _shots = [];

    internal void AddShot(ShotRecord shot) => _shots.Add(shot);

    /// <summary>Current energy, 0–100: falls on court, recovers on the bench (docs/MATCH_ENGINE.md §4).</summary>
    public double Energy { get; internal set; }

    public bool IsOnCourt { get; internal set; }

    public bool FouledOut { get; internal set; }

    public int Points => (2 * TwoPointMade) + (3 * ThreePointMade) + FreeThrowMade;

    public int Minutes => (int)Math.Round(SecondsPlayed / 60, MidpointRounding.AwayFromZero);

    public BoxScore ToBoxScore() => new()
    {
        Minutes = Minutes,
        TwoPointMade = TwoPointMade,
        TwoPointAttempted = TwoPointAttempted,
        ThreePointMade = ThreePointMade,
        ThreePointAttempted = ThreePointAttempted,
        FreeThrowMade = FreeThrowMade,
        FreeThrowAttempted = FreeThrowAttempted,
        OffensiveRebounds = OffensiveRebounds,
        DefensiveRebounds = DefensiveRebounds,
        Assists = Assists,
        Steals = Steals,
        Blocks = Blocks,
        Turnovers = Turnovers,
        PersonalFouls = PersonalFouls,
        PointsFor = PointsFor,
        PointsAgainst = PointsAgainst,
        OffensivePossessions = OffensivePossessions,
        DefensivePossessions = DefensivePossessions,
        DefendedAttempted = DefendedAttempted,
        DefendedMade = DefendedMade,
        ShotData = ShotChart.Encode(_shots),
    };
}

/// <summary>What caused a turnover (Step 29). Shot-clock violations are team turnovers, not in the box score.</summary>
public enum TurnoverCause
{
    BadPass,
    LostBall,
    OffensiveFoul,
    Travelling,
    OutOfBounds,
    ShotClockViolation,
}

/// <summary>Team play-type figures of a match that the box score does not hold (Step 29).</summary>
public sealed record TeamAnalytics(
    IReadOnlyDictionary<TurnoverCause, int> Turnovers,
    int Steals,
    int FastBreakPossessions,
    int SecondaryBreakPossessions,
    int FastBreakPoints,
    int SecondChancePoints,
    int PassedUpShots)
{
    public static TeamAnalytics Empty { get; } = new(new Dictionary<TurnoverCause, int>(), 0, 0, 0, 0, 0, 0);
}

/// <summary>Team totals of a set of box scores, with shooting percentages.</summary>
public sealed record TeamStatistics(
    int Points,
    int FieldGoalsMade,
    int FieldGoalsAttempted,
    int TwoPointMade,
    int TwoPointAttempted,
    int ThreePointMade,
    int ThreePointAttempted,
    int FreeThrowMade,
    int FreeThrowAttempted,
    int OffensiveRebounds,
    int DefensiveRebounds,
    int Assists,
    int Steals,
    int Blocks,
    int Turnovers,
    int PersonalFouls)
{
    public int Rebounds => OffensiveRebounds + DefensiveRebounds;

    public double FieldGoalPercentage => Ratio(FieldGoalsMade, FieldGoalsAttempted);

    public double TwoPointPercentage => Ratio(TwoPointMade, TwoPointAttempted);

    public double ThreePointPercentage => Ratio(ThreePointMade, ThreePointAttempted);

    public double FreeThrowPercentage => Ratio(FreeThrowMade, FreeThrowAttempted);

    public static TeamStatistics From(IEnumerable<BoxScore> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var l = lines.ToList();
        return new TeamStatistics(
            l.Sum(b => b.Points),
            l.Sum(b => b.TwoPointMade + b.ThreePointMade),
            l.Sum(b => b.TwoPointAttempted + b.ThreePointAttempted),
            l.Sum(b => b.TwoPointMade),
            l.Sum(b => b.TwoPointAttempted),
            l.Sum(b => b.ThreePointMade),
            l.Sum(b => b.ThreePointAttempted),
            l.Sum(b => b.FreeThrowMade),
            l.Sum(b => b.FreeThrowAttempted),
            l.Sum(b => b.OffensiveRebounds),
            l.Sum(b => b.DefensiveRebounds),
            l.Sum(b => b.Assists),
            l.Sum(b => b.Steals),
            l.Sum(b => b.Blocks),
            l.Sum(b => b.Turnovers),
            l.Sum(b => b.PersonalFouls));
    }

    private static double Ratio(int made, int attempted) => attempted == 0 ? 0 : (double)made / attempted;
}

/// <summary>The final result of a simulation.</summary>
/// <param name="EndedByDefault">A team was left with fewer than two players (FIBA art. 21): the score follows that rule.</param>
public sealed record MatchResult(
    int HomeScore,
    int AwayScore,
    int Overtimes,
    IReadOnlyList<PlayerLine> HomeLines,
    IReadOnlyList<PlayerLine> AwayLines,
    int Possessions,
    bool EndedByDefault)
{
    public IReadOnlyList<PlayerLine> Lines(TeamSide side) => side == TeamSide.Home ? HomeLines : AwayLines;

    /// <summary>Tactical decisions and situational summaries (Step 27), in match order.</summary>
    public IReadOnlyList<TacticalNote> TacticalNotes { get; init; } = [];

    public TeamAnalytics HomeAnalytics { get; init; } = TeamAnalytics.Empty;

    public TeamAnalytics AwayAnalytics { get; init; } = TeamAnalytics.Empty;

    public TeamAnalytics Analytics(TeamSide side) => side == TeamSide.Home ? HomeAnalytics : AwayAnalytics;

    public TeamStatistics Statistics(TeamSide side) => TeamStatistics.From(Lines(side).Select(l => l.ToBoxScore()));
}
