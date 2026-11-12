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
    Playoffs,
    Completed,
}

/// <param name="FixturesCreated">Fixtures scheduled by the call that returned this progress.</param>
public sealed record CompetitionProgress(CompetitionPhase Phase, int FixturesCreated);

/// <summary>
/// Runs league competitions from their rules data: registration, schedule, results, standings, playoffs and
/// promotion/relegation. Every call is one unit of work; rule violations throw <see cref="Domain.Common.DomainException"/>.
/// </summary>
public interface ICompetitionService
{
    /// <summary>Enters clubs into a competition season, checking the registration rules. Closed once the schedule exists.</summary>
    Task RegisterClubsAsync(CompetitionId competitionId, SeasonId seasonId, IReadOnlyList<ClubId> clubIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates the regular-season fixtures from the entered clubs, starting on <paramref name="firstRound"/>
    /// (default: the season start). The whole schedule must fit in the season.
    /// </summary>
    Task<CompetitionProgress> GenerateScheduleAsync(CompetitionId competitionId, SeasonId seasonId, DateOnly? firstRound = null, CancellationToken cancellationToken = default);

    /// <summary>Records the result of a fixture and updates the stored standings of regular-season games.</summary>
    Task RecordResultAsync(FixtureId fixtureId, int homeScore, int awayScore, int overtimes = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the season on: once the regular season is over, schedules the playoffs, then the next game of each
    /// series and the next round as results come in. Safe to call at any time.
    /// </summary>
    Task<CompetitionProgress> AdvanceAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default);

    /// <summary>Where the competition season stands, without changing anything.</summary>
    Task<CompetitionPhase> GetPhaseAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default);

    /// <summary>The regular-season table, ranked by the rules (points, then tie-breakers).</summary>
    Task<IReadOnlyList<StandingRow>> GetStandingsAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default);

    /// <summary>Champion, promoted and relegated clubs. Only for a completed season.</summary>
    Task<SeasonOutcome> GetOutcomeAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default);
}
