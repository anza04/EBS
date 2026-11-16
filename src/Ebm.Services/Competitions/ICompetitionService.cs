using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;

namespace Ebm.Services.Competitions;

/// <summary>Where a competition season stands.</summary>
public enum CompetitionPhase
{
    /// <summary>No schedule yet: clubs can still be registered.</summary>
    Registration,
    RegularSeason,
    SecondPhase,
    PlayIn,
    Playoffs,
    Completed,

    /// <summary>A cup's group stage.</summary>
    GroupStage,

    /// <summary>A cup's knockout rounds.</summary>
    Knockout,
}

/// <param name="FixturesCreated">Fixtures scheduled by the call that returned this progress.</param>
public sealed record CompetitionProgress(CompetitionPhase Phase, int FixturesCreated);

/// <summary>A cup season: seeds (best first), groups, byes, ties round by round, and the finalists once it is over.</summary>
public sealed record CupView(
    CompetitionPhase Phase,
    IReadOnlyList<ClubId> Seeds,
    IReadOnlyList<GroupTable> Groups,
    IReadOnlyList<ClubId> Byes,
    IReadOnlyList<CupTie> Ties,
    ClubId? Winner,
    ClubId? RunnerUp);

/// <summary>A club with more than one fixture on the same day (a calendar clash between competitions).</summary>
public sealed record CalendarConflict(ClubId ClubId, DateOnly Date, IReadOnlyList<FixtureId> Fixtures);

/// <summary>One season of a competition's history: the champion (cup winner) and runner-up once it is completed.</summary>
public sealed record CompetitionHistoryEntry(Season Season, CompetitionPhase Phase, ClubId? Champion, ClubId? RunnerUp);

/// <summary>
/// Runs league and cup competitions from their rules data: registration, schedule, results, standings, playoffs and
/// promotion/relegation. Every call is one unit of work; rule violations throw <see cref="Domain.Common.DomainException"/>.
/// </summary>
public interface ICompetitionService
{
    /// <summary>Enters clubs into a competition season, checking the registration rules. Closed once the schedule exists.</summary>
    Task RegisterClubsAsync(CompetitionId competitionId, SeasonId seasonId, IReadOnlyList<ClubId> clubIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates the regular-season fixtures from the entered clubs, starting on <paramref name="firstRound"/>
    /// (default: the season start). The whole schedule must fit in the season. For a cup it makes the draw: seeds
    /// the clubs (entered, or qualified from the league table), stores the seeds and schedules the first games.
    /// </summary>
    Task<CompetitionProgress> GenerateScheduleAsync(CompetitionId competitionId, SeasonId seasonId, DateOnly? firstRound = null, CancellationToken cancellationToken = default);

    /// <summary>Records the result of a fixture and updates the stored standings of regular-season games.</summary>
    Task RecordResultAsync(FixtureId fixtureId, int homeScore, int awayScore, int overtimes = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the season on. A cup is drawn as soon as its clubs are known (its qualification decided), then its next
    /// round and second legs are scheduled as results come in, on days its clubs have no other game. A league: once the regular season is over, schedules the second phase, the play-in or the
    /// playoffs, then the next game of each series and the next round as results come in. Safe to call at any time.
    /// </summary>
    Task<CompetitionProgress> AdvanceAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default);

    /// <summary>Where the competition season stands, without changing anything.</summary>
    Task<CompetitionPhase> GetPhaseAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The final table, ranked by the rules (points, then tie-breakers): the regular-season table, or with a second
    /// phase its groups in order followed by the clubs in no group.
    /// </summary>
    Task<IReadOnlyList<StandingRow>> GetStandingsAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default);

    /// <summary>The second-phase group tables once the regular season is over; empty without a second phase.</summary>
    Task<IReadOnlyList<GroupTable>> GetGroupTablesAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default);

    /// <summary>Champion (cup winner), runner-up, promoted and relegated clubs. Only for a completed season.</summary>
    Task<SeasonOutcome> GetOutcomeAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default);

    /// <summary>The draw, groups and ties of a cup season.</summary>
    Task<CupView> GetCupAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default);

    /// <summary>Every season of a competition with entries, oldest first, with its champion and runner-up when completed.</summary>
    Task<IReadOnlyList<CompetitionHistoryEntry>> GetHistoryAsync(CompetitionId competitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enters the season's qualified clubs in every international competition (a cup with <c>cup.entry</c> slots,
    /// docs/INTERNATIONAL_COMPETITIONS.md) from the previous season's tables and winners, or from club reputation
    /// when there is no finished previous season; hand entries are kept, drawn competitions left alone. Returns the
    /// clubs entered. Safe to call again: full competitions take nobody.
    /// </summary>
    Task<int> QualifyAsync(SeasonId seasonId, CancellationToken cancellationToken = default);

    /// <summary>Every club that has two or more fixtures on one day of the season, across all competitions.</summary>
    Task<IReadOnlyList<CalendarConflict>> GetCalendarConflictsAsync(SeasonId seasonId, CancellationToken cancellationToken = default);
}
