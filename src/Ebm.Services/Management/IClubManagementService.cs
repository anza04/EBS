using Ebm.Domain.Clubs;
using Ebm.Domain.Seasons;

namespace Ebm.Services.Management;

/// <summary>
/// AI management of the clubs the user does not manage (docs/AI_MANAGEMENT.md): matchday roles, tactics, contract
/// renewals, releases and board objectives. The career calendar calls it; every call skips <c>managedClubs</c>.
/// </summary>
public interface IClubManagementService
{
    /// <summary>Name of the tactic AI clubs play with.</summary>
    public const string AiTacticName = "AI plan";

    /// <summary>Sets the squad roles (starting five, rotation, bench) of each club. Returns the clubs whose roles changed.</summary>
    Task<int> PrepareMatchdayAsync(IReadOnlyCollection<ClubId> clubs, CancellationToken cancellationToken = default);

    /// <summary>Saves and selects the AI tactic ("AI plan") of each club that is not managed. Returns the clubs planned.</summary>
    Task<int> PlanTacticsAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default);

    /// <summary>Renews expiring contracts of the AI clubs' core players. Returns the contracts renewed.</summary>
    Task<int> RenewContractsAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default);

    /// <summary>Releases (mutual agreement) the weakest players of AI clubs above the squad limit. Returns the players released.</summary>
    Task<int> ReleaseSurplusAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Last resort for AI clubs still below the minimum first team after a transfer window: signs the best free agents
    /// whose asking salary the club can pay for a year from its balance, ignoring the wage budget. Returns the signings.
    /// </summary>
    Task<int> FillSquadsAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps at least <see cref="FreeAgentsPerClub"/> free agents per club on the market by creating fictional young
    /// players (stand-in for youth intake). Returns the players created.
    /// </summary>
    Task<int> ReplenishFreeAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Free agents per club the market keeps (see <see cref="ReplenishFreeAgentsAsync"/>).</summary>
    public const int FreeAgentsPerClub = 2;

    /// <summary>AI clubs below the minimum first team or without five available players.</summary>
    Task<IReadOnlyList<ClubId>> ClubsShortOfPlayersAsync(IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default);

    /// <summary>Sets every club's board objective for the season from its reputation rank in its league competition.</summary>
    Task SetObjectivesAsync(SeasonId seasonId, CancellationToken cancellationToken = default);
}
