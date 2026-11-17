using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Scouting;
using Ebm.Domain.Staff;

namespace Ebm.Services.Scouting;

/// <summary>
/// Scouting (docs/SCOUTING.md): the clubs' scouts, their assignments, the reports they write when an assignment is
/// due, and searches over what a club knows. One DI scope per call; "today" comes from the <see cref="GameClock"/>.
/// </summary>
/// <remarks>Writes throw <see cref="Ebm.Domain.Common.DomainException"/> with a player-facing message.</remarks>
public interface IScoutingService
{
    /// <summary>The club's scouts (staff with a running scout contract), each with his active assignment.</summary>
    Task<IReadOnlyList<Scout>> GetScoutsAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>Sends an available scout of the club on an assignment starting today.</summary>
    Task<AssignmentLine> AssignAsync(ClubId clubId, AssignmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Stops an active assignment today; no reports are written.</summary>
    Task CancelAsync(ScoutingAssignmentId assignmentId, CancellationToken cancellationToken = default);

    /// <summary>The club's assignments, newest first (<paramref name="activeOnly"/>: only those in progress).</summary>
    Task<IReadOnlyList<AssignmentLine>> GetAssignmentsAsync(ClubId clubId, bool activeOnly = false, CancellationToken cancellationToken = default);

    /// <summary>Player search over the club's knowledge: the newest report on each scouted player matching <paramref name="search"/>, best estimate first.</summary>
    Task<IReadOnlyList<ReportLine>> SearchReportsAsync(ClubId clubId, ReportSearch search, CancellationToken cancellationToken = default);

    /// <summary>Every report of the club on one player, newest first.</summary>
    Task<IReadOnlyList<ReportLine>> GetPlayerReportsAsync(ClubId clubId, PlayerId playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives every club without enough scouts new ones on a three-year contract (<see cref="ScoutingRules.ScoutsFor"/>):
    /// abilities follow the club's reputation. Idempotent; returns the scouts hired.
    /// </summary>
    Task<int> HireMissingScoutsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The scouting part of a game day: hires missing scouts, then writes the reports of every assignment due today
    /// (or earlier) and completes it. Assignments of scouts no longer employed are cancelled. Idempotent.
    /// </summary>
    Task<ScoutingDay> ProcessDayAsync(CancellationToken cancellationToken = default);
}

/// <summary>A new assignment: the scout, the focus and its target, and optional filters (docs/SCOUTING.md §2).</summary>
/// <param name="CountryId">Region target; optional country of a youth assignment.</param>
/// <param name="Position">Position target; optional filter of the other group assignments.</param>
/// <param name="MaxAge">Oldest player watched (youth default: <see cref="ScoutingRules.YouthMaxAge"/>).</param>
public sealed record AssignmentRequest(
    StaffMemberId ScoutId,
    ScoutingFocus Focus,
    PlayerId? PlayerId = null,
    CountryId? CountryId = null,
    CompetitionId? CompetitionId = null,
    Position? Position = null,
    int? MaxAge = null);

/// <summary>A scout of the club and what he is doing.</summary>
public sealed record Scout(StaffMember Member, Contract Contract, AssignmentLine? Assignment)
{
    public bool IsAvailable => Assignment is null;

    public string Name => Member.FullName;
}

/// <param name="Target">What is watched, e.g. "League: Serie A" or "Player: Marco Rossi", with its filters.</param>
/// <param name="Reports">Reports written (0 until completed).</param>
public sealed record AssignmentLine(ScoutingAssignment Assignment, string Scout, string Target, int Reports)
{
    public DateOnly DueDate => Assignment.DueDate;

    public AssignmentStatus Status => Assignment.Status;
}

/// <summary>A report as the screens show it, with the player's current situation.</summary>
/// <param name="Club">The player's current club; <c>null</c> for a free agent.</param>
/// <param name="Scout">The scout who wrote it.</param>
public sealed record ReportLine(ScoutReport Report, Player Player, int Age, string? Club, string Nationality, string Scout)
{
    public string OverallText => Range(Report.OverallRange);

    public string PotentialText => Range(Report.PotentialRange);

    private static string Range((int Low, int High) range) => range.Low == range.High ? $"{range.Low}" : $"{range.Low}–{range.High}";
}

/// <summary>Criteria of <see cref="IScoutingService.SearchReportsAsync"/>, on the scouts' estimates; every field is optional.</summary>
/// <param name="MinOverall">Lowest estimated overall rating.</param>
/// <param name="MinPotential">Lowest estimated potential.</param>
/// <param name="MaxSalary">Highest estimated salary expectation.</param>
public sealed record ReportSearch(
    string? Name = null,
    Position? Position = null,
    int? MaxAge = null,
    int? MinOverall = null,
    int? MinPotential = null,
    long? MaxSalary = null,
    CountryId? NationalityId = null,
    int MaxResults = 200)
{
    public static ReportSearch Everyone { get; } = new();
}

/// <summary>What <see cref="IScoutingService.ProcessDayAsync"/> did.</summary>
public sealed record ScoutingDay(int ScoutsHired, int AssignmentsCompleted, int AssignmentsCancelled, int ReportsWritten)
{
    public static ScoutingDay None { get; } = new(0, 0, 0, 0);
}
