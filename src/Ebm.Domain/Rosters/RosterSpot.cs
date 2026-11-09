using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;

namespace Ebm.Domain.Rosters;

/// <summary>A player's role in his club's squad. Every role except <see cref="Youth"/> belongs to the first team.</summary>
public enum RosterRole
{
    /// <summary>In the starting five, at a lineup position.</summary>
    Starter,

    /// <summary>Comes off the bench in the playing rotation, in rotation order.</summary>
    Rotation,

    /// <summary>In the matchday squad but outside the rotation.</summary>
    Bench,

    /// <summary>First-team player left out of the matchday squad.</summary>
    Inactive,

    /// <summary>Youth squad; not part of the first team.</summary>
    Youth,
}

/// <summary>Whether a player can play today, from his injury status.</summary>
public enum PlayerAvailability
{
    Available,
    Doubtful,
    Unavailable,
}

public static class RosterRoles
{
    public static bool IsFirstTeam(this RosterRole role) => role != RosterRole.Youth;

    /// <summary>Starters, rotation and bench: the players named in the matchday squad.</summary>
    public static bool IsActive(this RosterRole role) => role is RosterRole.Starter or RosterRole.Rotation or RosterRole.Bench;

    public static PlayerAvailability AvailabilityOf(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.InjuryStatus switch
        {
            InjuryStatus.Injured => PlayerAvailability.Unavailable,
            InjuryStatus.DayToDay => PlayerAvailability.Doubtful,
            _ => PlayerAvailability.Available,
        };
    }
}

/// <summary>
/// A player's place in his current club's squad. Current state, not history: one spot per player, replaced when
/// he moves. A club player without a stored spot is on the bench (see docs/ROSTER_RULES.md).
/// </summary>
public sealed class RosterSpot : Entity<RosterSpotId>
{
    public RosterSpot(RosterSpotId id, PlayerId playerId, ClubId clubId, RosterRole role, Position? lineupPosition = null, int? rotationOrder = null)
        : base(id)
    {
        PlayerId = Guard.Required(playerId, nameof(playerId));
        ClubId = Guard.Required(clubId, nameof(clubId));
        Assign(role, lineupPosition, rotationOrder);
    }

    private RosterSpot()
    {
    }

    public PlayerId PlayerId { get; private set; }

    public ClubId ClubId { get; private set; }

    public RosterRole Role { get; private set; }

    /// <summary>Position in the starting five; set only for starters.</summary>
    public Position? LineupPosition { get; private set; }

    /// <summary>1 = first player off the bench; set only for rotation players.</summary>
    public int? RotationOrder { get; private set; }

    /// <summary>Moves the spot to the player's new club; the caller assigns his role there.</summary>
    public void MoveTo(ClubId clubId) => ClubId = Guard.Required(clubId, nameof(clubId));

    public void Assign(RosterRole role, Position? lineupPosition, int? rotationOrder)
    {
        Guard.Defined(role, "Roster role");
        if ((role == RosterRole.Starter) != lineupPosition.HasValue)
        {
            throw new DomainValidationException(role == RosterRole.Starter
                ? "A starter needs a lineup position."
                : "Only starters have a lineup position.");
        }

        if ((role == RosterRole.Rotation) != rotationOrder.HasValue)
        {
            throw new DomainValidationException(role == RosterRole.Rotation
                ? "A rotation player needs a rotation order."
                : "Only rotation players have a rotation order.");
        }

        Role = role;
        LineupPosition = lineupPosition is { } position ? Guard.Defined(position, "Lineup position") : null;
        RotationOrder = rotationOrder is { } order ? Guard.Range(order, 1, SquadRules.PlayerLimit, "Rotation order") : null;
    }
}
