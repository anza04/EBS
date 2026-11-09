using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;

namespace Ebm.Domain.Matches;

/// <summary>
/// A player's statistics in one match. The club the player represented is stored on the line,
/// so statistics stay attributed correctly after the player changes club.
/// </summary>
public sealed class PlayerMatchStatistics : Entity<PlayerMatchStatisticsId>
{
    public PlayerMatchStatistics(PlayerMatchStatisticsId id, MatchId matchId, PlayerId playerId, ClubId clubId, BoxScore boxScore)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(boxScore);
        boxScore.Validate();

        MatchId = Guard.Required(matchId, nameof(matchId));
        PlayerId = Guard.Required(playerId, nameof(playerId));
        ClubId = Guard.Required(clubId, nameof(clubId));
        BoxScore = boxScore;
    }

    private PlayerMatchStatistics()
    {
    }

    public MatchId MatchId { get; private set; }

    public PlayerId PlayerId { get; private set; }

    /// <summary>Club the player represented in this match.</summary>
    public ClubId ClubId { get; private set; }

    public BoxScore BoxScore { get; private set; } = null!;
}
