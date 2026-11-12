using Ebm.Domain.Clubs;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Domain.Tactics;

namespace Ebm.Services.Tactics;

/// <summary>
/// Creates, edits and selects club tactics, and hands the selected one to the match engine. One DI scope per call;
/// rule violations throw <see cref="Domain.Common.DomainException"/> with a player-facing message.
/// </summary>
public interface ITacticsService
{
    /// <summary>The club's tactics and squad assignments, or <c>null</c> when the club does not exist.</summary>
    Task<ClubTactics?> GetClubTacticsAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates (<see cref="TacticDraft.Id"/> <c>null</c>) or updates a tactic. Assignments may name only current club
    /// players. A club's first tactic is selected automatically.
    /// </summary>
    Task<Tactic> SaveAsync(ClubId clubId, TacticDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Makes the tactic the one its club plays the next match with.</summary>
    Task SelectAsync(TacticId tacticId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a tactic. When it was selected, the club's first remaining tactic (by name) is selected.</summary>
    Task DeleteAsync(TacticId tacticId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The configuration the club plays with: its selected tactic, or <see cref="TeamTactics.Default"/> when it has
    /// none. Assignments of players who left the club are dropped.
    /// </summary>
    Task<TeamTactics> GetMatchTacticsAsync(ClubId clubId, CancellationToken cancellationToken = default);
}

/// <summary>The fields of a tactic being saved.</summary>
public sealed record TacticDraft(
    TacticId? Id,
    string Name,
    OffensiveSystem OffensiveSystem,
    OffensiveSettings Offense,
    DefensiveSystem DefensiveSystem,
    DefensiveSettings Defense,
    IReadOnlyList<TacticalAssignment> Assignments);

/// <summary>A club player in the tactics screen with his stored assignment in the shown tactic (or the suggestion).</summary>
public sealed record TacticSquadPlayer(Player Player, RosterRole RosterRole, Position? LineupPosition, TacticalRole SuggestedRole);

/// <summary>Everything the tactics screen shows for a club.</summary>
public sealed record ClubTactics(Club Club, IReadOnlyList<Tactic> Tactics, IReadOnlyList<TacticSquadPlayer> Squad)
{
    public Tactic? Selected => Tactics.FirstOrDefault(t => t.IsSelected);
}
