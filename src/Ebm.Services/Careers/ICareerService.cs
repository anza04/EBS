using Ebm.Domain.Careers;
using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;
using Ebm.Services.Competitions;
using Ebm.Services.Matches;
using Ebm.Services.Transfers;

namespace Ebm.Services.Careers;

/// <summary>
/// The career: club selection, the calendar (one game day at a time), the end of a season with promotion and
/// relegation, and the next season (docs/SEASON_LIFECYCLE.md). The career date is the game's "today" (<see cref="GameClock"/>).
/// Rule violations throw <see cref="Domain.Common.DomainException"/>.
/// </summary>
public interface ICareerService
{
    /// <summary>Loads the stored career, if any, and sets the game clock to its date. Call once at startup.</summary>
    Task<CareerState?> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>The clubs a new career can be started with: those entered in a league competition of the first season with leagues.</summary>
    Task<IReadOnlyList<CareerClubOption>> GetClubOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a career with <paramref name="clubId"/>: schedules every league competition of the season that has none yet
    /// and sets the date to one week before the season starts. A database holds one career.
    /// </summary>
    Task<CareerState> StartAsync(ClubId clubId, string managerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes the current game day (every other fixture of the day is played, competitions advance) and moves the date
    /// on by one day, applying contract and transfer dates. Refused while the user's club has an unplayed fixture today.
    /// </summary>
    Task<GameDayReport> AdvanceDayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Advances day by day until the user's club plays, the season is complete, or <paramref name="maxDays"/> days have passed.
    /// </summary>
    Task<CalendarReport> ContinueAsync(int maxDays = 366, CancellationToken cancellationToken = default);

    /// <summary>Plays the user's fixture of today instantly (live games are played in the match centre).</summary>
    Task<Match> PlayUserMatchAsync(CancellationToken cancellationToken = default);

    /// <summary>Champions, promoted and relegated clubs of the career season's leagues, once the season is complete.</summary>
    Task<SeasonReview> GetSeasonReviewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a complete season: creates the next season (unless it exists), enters the clubs after promotion and relegation,
    /// schedules the leagues and moves the career on. Safe to repeat after a failure.
    /// </summary>
    Task<CareerState> StartNextSeasonAsync(CancellationToken cancellationToken = default);

    /// <summary>Every league season of a club: position, record and outcome. Nothing is ever deleted, so this is the full history.</summary>
    Task<IReadOnlyList<ClubSeasonRecord>> GetClubHistoryAsync(ClubId clubId, CancellationToken cancellationToken = default);
}

/// <summary>A club that can be managed, with its league.</summary>
public sealed record CareerClubOption(Club Club, string Competition, int Reputation);

/// <summary>A league competition of the career season and where it stands.</summary>
public sealed record CareerCompetition(Competition Competition, CompetitionPhase Phase, bool UserClubEntered);

/// <summary>The career as it is now.</summary>
public sealed record CareerState(
    Career Career,
    Club Club,
    Season Season,
    IReadOnlyList<CareerCompetition> Competitions,
    FixtureLine? NextFixture)
{
    public DateOnly Today => Career.CurrentDate;

    /// <summary>The user's club has an unplayed fixture today (or earlier): it must be played before the calendar moves on.</summary>
    public bool MatchToday => NextFixture is { } next && next.Fixture.Date <= Today;

    /// <summary>Every scheduled league competition of the season is finished.</summary>
    public bool SeasonComplete => Competitions.Count > 0 && Competitions.All(c => c.Phase == CompetitionPhase.Completed);
}

/// <summary>One played fixture of a game day.</summary>
public sealed record PlayedFixture(string Competition, string Home, string Away, Match Match)
{
    public string Score => Match.IsForfeit ? $"{Match.HomeScore}–{Match.AwayScore} (forfeit)" : $"{Match.HomeScore}–{Match.AwayScore}";
}

/// <summary>What the AI clubs and the club finances did on one game day.</summary>
public sealed record ManagementDay(int LineupsChanged, int TacticsPlanned, int Renewals, int Releases, int EmergencySignings, int PlayersCreated, long SalariesPaid, long Income);

/// <summary>What happened on one game day.</summary>
public sealed record GameDayReport(
    DateOnly Date,
    IReadOnlyList<PlayedFixture> Results,
    int PlayoffFixturesScheduled,
    TransferDateChanges DateChanges,
    TransferWindowReport? TransferWindow,
    ManagementDay Management);

/// <summary>Why <see cref="ICareerService.ContinueAsync"/> stopped.</summary>
public enum CalendarStop
{
    UserMatch,
    SeasonComplete,
    DayLimit,
}

/// <summary>The days processed by one <see cref="ICareerService.ContinueAsync"/> call.</summary>
public sealed record CalendarReport(IReadOnlyList<GameDayReport> Days, CalendarStop StoppedBecause, CareerState State)
{
    public int MatchesPlayed => Days.Sum(d => d.Results.Count);
}

/// <summary>A club's board objective for the season and whether it was met.</summary>
public sealed record ObjectiveResult(Club Club, int Position, BoardExpectation Expectation, bool Met);

/// <summary>The result of one league competition's season.</summary>
public sealed record CompetitionReview(
    Competition Competition,
    string Champion,
    IReadOnlyList<string> Promoted,
    IReadOnlyList<string> Relegated,
    int? UserPosition,
    IReadOnlyList<ObjectiveResult> Objectives);

public sealed record SeasonReview(Season Season, IReadOnlyList<CompetitionReview> Competitions);

/// <summary>A club's season in one league competition.</summary>
public sealed record ClubSeasonRecord(
    Season Season,
    Competition Competition,
    int Position,
    int Won,
    int Lost,
    CompetitionPhase Phase,
    bool Champion,
    bool Promoted,
    bool Relegated)
{
    public string Outcome =>
        Phase != CompetitionPhase.Completed ? "In progress"
        : string.Join(", ", new[] { Champion ? "Champion" : null, Promoted ? "Promoted" : null, Relegated ? "Relegated" : null }.OfType<string>()) is { Length: > 0 } text ? text
        : "—";
}
