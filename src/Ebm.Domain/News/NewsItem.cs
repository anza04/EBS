using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Matches;
using Ebm.Domain.Medical;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Ebm.Domain.Transfers;

namespace Ebm.Domain.News;

/// <summary>What a news item reports (docs/NEWS.md §2).</summary>
public enum NewsCategory
{
    Transfer,
    Injury,
    MatchPreview,
    MatchReview,
    PressConference,
    Interview,
    Rumor,
    MediaReaction,
    Reputation,
    FanExpectations,
}

public readonly record struct NewsItemId(Guid Value) : IEntityId<NewsItemId>
{
    public static NewsItemId New() => new(Guid.CreateVersion7());

    public static NewsItemId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// One news story, written from a game event that already happened (a match, a transfer, an injury, a reputation
/// change…). Stories are never edited or deleted: they are the news history. <see cref="Key"/> names the event, so the
/// same event is never reported twice.
/// </summary>
/// <remarks>The links are plain IDs without foreign keys: a story outlives a player or club removed in the editor.</remarks>
public sealed class NewsItem : Entity<NewsItemId>
{
    public const int KeyMaxLength = 160;
    public const int HeadlineMaxLength = 200;
    public const int BodyMaxLength = 2000;

    public NewsItem(NewsItemId id, string key, DateOnly date, NewsCategory category, string headline, string body, int sentiment = 0)
        : base(id)
    {
        Key = Guard.Text(key, "News key", KeyMaxLength);
        Date = date;
        Category = Guard.Defined(category, "News category");
        Headline = Guard.Text(headline, "Headline", HeadlineMaxLength);
        Body = Guard.Text(body, "News body", BodyMaxLength);
        Sentiment = Guard.Range(sentiment, -2, 2, "Sentiment");
    }

    private NewsItem()
    {
    }

    /// <summary>The event reported, e.g. <c>review:{matchId}</c>; unique.</summary>
    public string Key { get; private set; } = string.Empty;

    public DateOnly Date { get; private set; }

    public NewsCategory Category { get; private set; }

    public string Headline { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    /// <summary>Tone of the story for its club, −2 (very negative) to +2; for fan stories, the fans' mood.</summary>
    public int Sentiment { get; private set; }

    /// <summary>The club the story is about.</summary>
    public ClubId? ClubId { get; init; }

    /// <summary>The other club involved (opponent, selling club…).</summary>
    public ClubId? OtherClubId { get; init; }

    public PlayerId? PlayerId { get; init; }

    public SeasonId? SeasonId { get; init; }

    public FixtureId? FixtureId { get; init; }

    public MatchId? MatchId { get; init; }

    public TransferId? TransferId { get; init; }

    public InjuryId? InjuryId { get; init; }

    public ReputationChangeId? ReputationChangeId { get; init; }

    /// <summary>The story concerns <paramref name="club"/> (as main or other club).</summary>
    public bool Involves(ClubId club) => ClubId == club || OtherClubId == club;
}
