using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.History;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Ebm.Domain.Staff;

namespace Ebm.Services.History;

/// <summary>
/// The long-term world (docs/HISTORY.md): season awards, league snapshots and reputation drift at season end; the hall
/// of fame and coaching careers of retired players; yearly youth intake; and the history queries. Every write is safe
/// to repeat.
/// </summary>
public interface IHistoryService
{
    /// <summary>Awards of every completed league of the season, its league snapshots, and every club's reputation drift.</summary>
    Task<HistorySeason> ProcessSeasonEndAsync(SeasonId seasonId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Hall of fame inductions and new coaching careers for players who just retired.</summary>
    Task<RetirementHistory> ProcessRetirementsAsync(IReadOnlyList<PlayerId> retired, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every club's academy produces its youth players (youth squad, youth contract); AI clubs (not in
    /// <paramref name="managedClubs"/>) promote youth players who are good enough to the first team.
    /// </summary>
    Task<YouthIntake> ProcessYouthIntakeAsync(DateOnly date, IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default);

    /// <summary>A player's career, retired players included; <c>null</c> for an unknown player.</summary>
    Task<PlayerCareer?> GetPlayerCareerAsync(PlayerId playerId, CancellationToken cancellationToken = default);

    /// <summary>A club's trophies and its players' awards.</summary>
    Task<ClubHonours> GetClubHonoursAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>Awards, newest season first; optionally one competition and/or season.</summary>
    Task<IReadOnlyList<AwardLine>> GetAwardsAsync(CompetitionId? competitionId = null, SeasonId? seasonId = null, CancellationToken cancellationToken = default);

    /// <summary>The hall of fame, by score.</summary>
    Task<IReadOnlyList<HallOfFameLine>> GetHallOfFameAsync(CancellationToken cancellationToken = default);

    /// <summary>A competition's winners and runners-up, newest season first (completed seasons).</summary>
    Task<IReadOnlyList<WinnerLine>> GetWinnersAsync(CompetitionId competitionId, CancellationToken cancellationToken = default);

    /// <summary>All-time records of a competition: single games, seasons, titles.</summary>
    Task<IReadOnlyList<RecordLine>> GetRecordsAsync(CompetitionId competitionId, CancellationToken cancellationToken = default);

    /// <summary>A league's snapshots, oldest season first.</summary>
    Task<IReadOnlyList<LeagueEvolutionLine>> GetLeagueEvolutionAsync(CompetitionId competitionId, CancellationToken cancellationToken = default);
}

public sealed record HistorySeason(int Awards, int Snapshots, int Drifted)
{
    public static HistorySeason None { get; } = new(0, 0, 0);
}

public sealed record RetirementHistory(IReadOnlyList<PlayerId> Inducted, IReadOnlyList<StaffMemberId> NewCoaches);

public sealed record YouthIntake(int Players, int Promoted);

/// <summary>An award for display: <see cref="Value"/> is the figure (points a game, value score, places gained).</summary>
public sealed record AwardLine(SeasonId SeasonId, string Season, CompetitionId CompetitionId, string Competition, AwardKind Kind, int Rank, PlayerId? PlayerId, string Winner, ClubId ClubId, string Club, double Value);

public sealed record HallOfFameLine(PlayerId PlayerId, string Name, DateOnly InductedOn, int Score, int Seasons, int Games, int Points, int Titles, int Awards);

/// <summary>A player's figures in one competition season at one club (all stages).</summary>
public sealed record CareerLine(string Season, string Competition, string Club, int Games, double MinutesPerGame, double PointsPerGame, double ReboundsPerGame, double AssistsPerGame, int Points);

public sealed record CareerSpell(string Club, DateOnly From, DateOnly? To);

/// <param name="CoachingRole">The staff role of his coaching career, if he started one.</param>
public sealed record PlayerCareer(
    PlayerId PlayerId,
    string Name,
    DateOnly? RetiredOn,
    HallOfFameLine? HallOfFame,
    IReadOnlyList<CareerLine> Seasons,
    CareerLine Totals,
    IReadOnlyList<AwardLine> Awards,
    IReadOnlyList<CareerSpell> Spells,
    int Titles,
    StaffRole? CoachingRole,
    string? Academy);

public sealed record Trophy(string Season, string Competition);

public sealed record ClubHonours(IReadOnlyList<Trophy> Trophies, IReadOnlyList<AwardLine> Awards, int AcademyPlayers);

public sealed record WinnerLine(string Season, string Champion, string RunnerUp);

public sealed record RecordLine(string Record, string Holder, string Club, string Season, string Value);

public sealed record LeagueEvolutionLine(string Season, int Clubs, int MeanReputation, int TopReputation, int MeanOverall, string Champion);
