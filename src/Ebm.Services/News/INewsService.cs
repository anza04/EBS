using Ebm.Domain.Clubs;
using Ebm.Domain.News;
using Ebm.Domain.Seasons;

namespace Ebm.Services.News;

/// <summary>
/// News, media and club reputation (docs/NEWS.md). Stories are written by the career calendar from events that already
/// happened and are kept forever; each event is reported once. Reputation changes are stored with the event behind them.
/// </summary>
public interface INewsService
{
    /// <summary>
    /// Writes the stories of game day <paramref name="day"/> (matches, upsets, transfers, injuries, rumours, previews of the
    /// next day, fans) and applies the upsets' reputation changes. Safe to repeat: events already reported are skipped.
    /// </summary>
    /// <param name="userClub">The career club: previews, reviews, press conferences, interviews, rumours and fan stories are about it.</param>
    Task<NewsDay> ProcessDayAsync(DateOnly day, SeasonId seasonId, ClubId? userClub, CancellationToken cancellationToken = default);

    /// <summary>
    /// The season's verdict: titles, cups, promotion, relegation and board objectives move every club's reputation, with
    /// one story per club. Safe to repeat.
    /// </summary>
    Task<NewsDay> ProcessSeasonEndAsync(SeasonId seasonId, IReadOnlyDictionary<ClubId, bool> objectivesMet, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>The fans' expectations of <paramref name="userClub"/> for a new season (one story). Safe to repeat.</summary>
    Task<NewsDay> ProcessSeasonStartAsync(SeasonId seasonId, ClubId userClub, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Stored stories, newest first.</summary>
    Task<IReadOnlyList<NewsItem>> GetNewsAsync(NewsQuery query, CancellationToken cancellationToken = default);

    /// <summary>Every reputation change of a club, oldest first.</summary>
    Task<IReadOnlyList<ReputationChange>> GetReputationHistoryAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>What the fans of <paramref name="clubId"/> expect in the career season and how they feel; <c>null</c> without a career or a league.</summary>
    Task<FanView?> GetFansAsync(ClubId clubId, CancellationToken cancellationToken = default);
}

/// <summary>Filters for <see cref="INewsService.GetNewsAsync"/>; a club matches as main or other club of a story.</summary>
public sealed record NewsQuery(ClubId? ClubId = null, NewsCategory? Category = null, DateOnly? From = null, DateOnly? To = null, int Max = 500);

/// <summary>What one news run wrote.</summary>
public sealed record NewsDay(int Stories, int ReputationChanges)
{
    public static NewsDay None { get; } = new(0, 0);
}

/// <summary>A club's fans in its league season.</summary>
/// <param name="Mood"><c>null</c> before <see cref="MediaRules.FanJudgementGames"/> league games.</param>
public sealed record FanView(string Competition, int Reputation, int ReputationRank, int Clubs, FanExpectation Expectation, int? Position, string Form, FanMood? Mood);
