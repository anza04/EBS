using Ebm.Domain.Clubs;
using Ebm.Domain.Common;

namespace Ebm.Domain.Players;

/// <summary>
/// One spell of a player at a club. Entries are never deleted, so the full career is preserved.
/// A player has at most one open entry (no end date) at a time.
/// </summary>
public sealed class PlayerClubHistoryEntry : Entity<PlayerClubHistoryEntryId>
{
    public PlayerClubHistoryEntry(PlayerClubHistoryEntryId id, PlayerId playerId, ClubId clubId, DateOnly startDate)
        : base(id)
    {
        PlayerId = Guard.Required(playerId, nameof(playerId));
        ClubId = Guard.Required(clubId, nameof(clubId));
        StartDate = startDate;
    }

    private PlayerClubHistoryEntry()
    {
    }

    public PlayerId PlayerId { get; private set; }

    public ClubId ClubId { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>Last day at the club; <c>null</c> while the player is still there.</summary>
    public DateOnly? EndDate { get; private set; }

    public bool IsCurrent => EndDate is null;

    public void Close(DateOnly endDate)
    {
        if (EndDate is not null)
        {
            throw new DomainValidationException("This club spell has already ended.");
        }

        if (endDate < StartDate)
        {
            throw new DomainValidationException($"A club spell cannot end before it starts ({StartDate:yyyy-MM-dd}).");
        }

        EndDate = endDate;
    }
}
