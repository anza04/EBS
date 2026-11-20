using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;

namespace Ebm.Domain.News;

/// <summary>The game event that moved a club's reputation (docs/NEWS.md §4).</summary>
public enum ReputationCause
{
    /// <summary>Beat a club with a much higher reputation.</summary>
    UpsetWin,

    /// <summary>Lost to a club with a much lower reputation.</summary>
    UpsetDefeat,

    LeagueTitle,
    CupWin,
    Promotion,
    Relegation,
    ObjectiveMet,
    ObjectiveMissed,

    /// <summary>A competition's reputation reward, paid when it ends (docs/INTERNATIONAL_COMPETITIONS.md).</summary>
    CompetitionReward,
}

public readonly record struct ReputationChangeId(Guid Value) : IEntityId<ReputationChangeId>
{
    public static ReputationChangeId New() => new(Guid.CreateVersion7());

    public static ReputationChangeId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// One change of a club's reputation and the event behind it: a match (<see cref="MatchId"/>) or a season result
/// (<see cref="SeasonId"/>). The changes of a club, in order, chain from one <see cref="After"/> to the next
/// <see cref="Before"/>; they are never deleted.
/// </summary>
public sealed class ReputationChange : Entity<ReputationChangeId>
{
    public ReputationChange(ReputationChangeId id, string key, ClubId clubId, DateOnly date, ReputationCause cause, int before, int after, SeasonId seasonId, MatchId? matchId)
        : base(id)
    {
        Key = Guard.Text(key, "Reputation key", NewsItem.KeyMaxLength);
        ClubId = Guard.Required(clubId, nameof(clubId));
        Date = date;
        Cause = Guard.Defined(cause, "Reputation cause");
        Before = Guard.Range(before, Club.MinReputation, Club.MaxReputation, "Reputation before");
        After = Guard.Range(after, Club.MinReputation, Club.MaxReputation, "Reputation after");
        SeasonId = Guard.Required(seasonId, nameof(seasonId));
        MatchId = Guard.Optional(matchId, nameof(matchId));
    }

    private ReputationChange()
    {
    }

    /// <summary>The event, e.g. <c>reputation:{matchId}:{clubId}</c>; unique, so an event moves a reputation once.</summary>
    public string Key { get; private set; } = string.Empty;

    public ClubId ClubId { get; private set; }

    public DateOnly Date { get; private set; }

    public ReputationCause Cause { get; private set; }

    public int Before { get; private set; }

    public int After { get; private set; }

    public int Points => After - Before;

    public SeasonId SeasonId { get; private set; }

    /// <summary>The match behind an upset; <c>null</c> for a season result or a competition reward.</summary>
    public MatchId? MatchId { get; private set; }
}
