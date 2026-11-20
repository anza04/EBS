using System.Reflection;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.News;
using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Matches;
using Ebm.Domain.Medical;
using Ebm.Domain.News;
using Ebm.Domain.Transfers;
using Ebm.Services.Careers;
using Ebm.Services.Editing;
using Ebm.Services.Import;
using Ebm.Services.News;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.News;

/// <summary>Step 31: news written from a played career season, its consistency, history and reputation (docs/NEWS.md).</summary>
public sealed class NewsServiceTests : IAsyncLifetime
{
    private static readonly string SampleFolder = Path.Combine(
        typeof(NewsServiceTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "sample");

    private static readonly TransferKind[] HeadlineMoves = [TransferKind.Transfer, TransferKind.ReleaseClause, TransferKind.OptionToBuy];

    private TestDatabase _db = null!;

    private ICareerService Careers => _db.Services.GetRequiredService<ICareerService>();

    private INewsService News => _db.Services.GetRequiredService<INewsService>();

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task A_season_of_news_follows_the_games_events_without_contradictions()
    {
        var report = await _db.Services.GetRequiredService<IDataImportService>()
            .ImportAsync([Path.Combine(SampleFolder, "italy-sample.json"), Path.Combine(SampleFolder, "italy-squads.json")], new ImportOptions());
        Assert.True(report.Committed);
        var user = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync(c => c.Name == "Aurora Milano"))).Single().Id;
        var start = await Careers.StartAsync(user, "Test Manager");
        var initial = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync())).ToDictionary(c => c.Id, c => c.Reputation);

        // Fans state their expectations at the start.
        var opening = Assert.Single(await News.GetNewsAsync(new NewsQuery(user, NewsCategory.FanExpectations)));
        Assert.Equal(start.Today, opening.Date);
        Assert.Contains("fans", opening.Headline, StringComparison.Ordinal);

        await PlaySeasonAsync();
        var season = start.Season.Id;
        await Careers.StartNextSeasonAsync();
        await Careers.ContinueAsync(); // through the summer window to the first game of the next season

        var news = await News.GetNewsAsync(new NewsQuery(Max: 100_000));
        var fixtures = (await _db.ReadAsync(s => s.Repo<Fixture, FixtureId>().ListAsync())).ToDictionary(f => f.Id);
        var matches = (await _db.ReadAsync(s => s.Repo<Match, MatchId>().ListAsync())).ToDictionary(m => m.Id);
        var today = (await Careers.LoadAsync())!.Today;

        // Every event is reported once.
        Assert.Equal(news.Count, news.Select(n => n.Key).Distinct().Count());
        Assert.Equal(news.OrderByDescending(n => n.Date).Select(n => n.Date), news.Select(n => n.Date));

        // Match reviews, press conferences and interviews: one per played match of the user's club, with its score.
        var userMatches = matches.Values.Where(m => !m.IsForfeit && fixtures[m.FixtureId].Date < today && Involves(fixtures[m.FixtureId], user)).ToList();
        Assert.True(userMatches.Count >= 14, $"{userMatches.Count} user matches");
        var reviews = news.Where(n => n.Category == NewsCategory.MatchReview).ToList();
        Assert.Equal(userMatches.Select(m => m.Id.Value).Order(), reviews.Select(r => r.MatchId!.Value.Value).Order());
        foreach (var review in reviews)
        {
            var match = matches[review.MatchId!.Value];
            var fixture = fixtures[match.FixtureId];
            var (own, other) = fixture.HomeClubId == user ? (match.HomeScore, match.AwayScore) : (match.AwayScore, match.HomeScore);
            Assert.EndsWith($"{own}–{other}", review.Headline, StringComparison.Ordinal);
            Assert.Equal(fixture.Date, review.Date);
            Assert.Equal(own > other, review.Sentiment > 0);
        }

        Assert.Equal(reviews.Count, news.Count(n => n.Category == NewsCategory.PressConference));
        Assert.Equal(reviews.Count, news.Count(n => n.Category == NewsCategory.Interview && n.PlayerId is not null));

        // Previews: the day before each of the user's games, never after it was played.
        var previews = news.Where(n => n.Category == NewsCategory.MatchPreview).ToList();
        Assert.NotEmpty(previews);
        Assert.All(previews, p => Assert.Equal(fixtures[p.FixtureId!.Value].Date.AddDays(-1), p.Date));
        Assert.All(userMatches.Where(m => fixtures[m.FixtureId].Date > start.Today), m => Assert.Contains(previews, p => p.FixtureId == m.FixtureId));

        // Streak stories match the results.
        foreach (var streak in news.Where(n => n.Key.StartsWith("streak:", StringComparison.Ordinal)))
        {
            var match = matches[streak.MatchId!.Value];
            Assert.Equal(match.HomeWon == (fixtures[match.FixtureId].HomeClubId == user), streak.Sentiment > 0);
        }

        // Transfers: a story for each move of the user's club and each fee-paying move, linking the real transfer.
        var transfers = (await _db.ReadAsync(s => s.Repo<Transfer, TransferId>().ListAsync(t => t.Date >= start.Today && t.Date < today))).ToDictionary(t => t.Id);
        var transferNews = news.Where(n => n.Category == NewsCategory.Transfer).ToList();
        Assert.Equal(
            transfers.Values.Where(t => t.ToClubId == user || t.FromClubId == user || HeadlineMoves.Contains(t.Kind)).Select(t => t.Id.Value).Order(),
            transferNews.Select(n => n.TransferId!.Value.Value).Order());
        Assert.All(transferNews, n => Assert.Equal((transfers[n.TransferId!.Value].ToClubId, transfers[n.TransferId!.Value].FromClubId, transfers[n.TransferId!.Value].Date), (n.ClubId!.Value, n.OtherClubId, n.Date)));

        // Injuries: every one at the user's club, serious ones elsewhere.
        var injuries = (await _db.ReadAsync(s => s.Repo<Injury, InjuryId>().ListAsync(i => i.OccurredOn >= start.Today && i.OccurredOn < today && i.ClubId != null))).ToDictionary(i => i.Id);
        var injuryNews = news.Where(n => n.Category == NewsCategory.Injury).ToList();
        Assert.Contains(injuries.Values, i => i.ClubId == user);
        Assert.Equal(
            injuries.Values.Where(i => i.ClubId == user || i.Severity >= MediaRules.HeadlineSeverity).Select(i => i.Id.Value).Order(),
            injuryNews.Select(n => n.InjuryId!.Value.Value).Order());
        Assert.All(injuryNews, n => Assert.Equal((injuries[n.InjuryId!.Value].PlayerId, injuries[n.InjuryId!.Value].OccurredOn), (n.PlayerId!.Value, n.Date)));

        // Rumours: the player was under a running contract with the user's club that day, and is rumoured once per contract.
        var contracts = await _db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync(c => c.PlayerId != null));
        foreach (var rumor in news.Where(n => n.Category == NewsCategory.Rumor))
        {
            var contract = contracts.Single(c => rumor.Key == $"rumor:contract:{c.Id}");
            Assert.Equal((user, rumor.PlayerId), (contract.ClubId, contract.PlayerId));
            Assert.True(contract.StartDate <= rumor.Date && contract.EndDate >= rumor.Date && contract.EndDate <= rumor.Date.AddDays(MediaRules.ContractRumorDays));
        }

        // Fans: a mood story only when the mood changes.
        var moods = news.Where(n => n.Category == NewsCategory.FanExpectations && n.SeasonId == season && n.Sentiment != 0).OrderBy(n => n.Date).ToList();
        Assert.NotEmpty(moods);
        Assert.All(moods.Zip(moods.Skip(1)), pair => Assert.NotEqual(pair.First.Sentiment, pair.Second.Sentiment));
        Assert.Equal(2, news.Count(n => n.Category == NewsCategory.FanExpectations && n.Key.EndsWith(":start", StringComparison.Ordinal)));

        // Reputation: every change has its event (an upset match or the season's results), chains per club, and ends at the club's reputation.
        var clubs = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync())).ToDictionary(c => c.Id);
        var changes = await _db.ReadAsync(s => s.Repo<ReputationChange, ReputationChangeId>().ListAsync());
        Assert.Contains(changes, c => c.Cause == ReputationCause.LeagueTitle && c.SeasonId == season);
        Assert.Contains(changes, c => c.Cause == ReputationCause.Relegation);
        Assert.All(changes.Where(c => c.Cause is ReputationCause.UpsetWin or ReputationCause.UpsetDefeat), c => Assert.True(c.MatchId is { } m && matches.ContainsKey(m)));
        Assert.All(changes.Where(c => c.Cause is not (ReputationCause.UpsetWin or ReputationCause.UpsetDefeat)), c => Assert.Null(c.MatchId));
        foreach (var (club, reputation) in initial)
        {
            var history = await News.GetReputationHistoryAsync(club);
            var at = reputation;
            foreach (var change in history)
            {
                Assert.Equal(at, change.Before);
                at = change.After;
            }

            Assert.True(clubs[club].Reputation == at, $"{clubs[club].Name}: initial {reputation}, now {clubs[club].Reputation}; " + string.Join(" | ", history.Select(h => $"{h.Date} {h.Cause} {h.Before}->{h.After} {h.Key}")));
        }

        Assert.All(news.Where(n => n.Category == NewsCategory.Reputation), n => Assert.Contains(changes, c => c.Id == n.ReputationChangeId));
        Assert.All(news.Where(n => n.Key.StartsWith("upset:", StringComparison.Ordinal)), n => Assert.Contains(changes, c => c.MatchId == n.MatchId));

        // History: filters by club, category and dates; last season's stories stay after the new season starts.
        var mine = await News.GetNewsAsync(new NewsQuery(user, Max: 100_000));
        Assert.All(mine, n => Assert.True(n.Involves(user)));
        Assert.Contains(mine, n => n.SeasonId == season && n.Category == NewsCategory.MatchReview);
        var october = await News.GetNewsAsync(new NewsQuery(From: new DateOnly(2026, 10, 1), To: new DateOnly(2026, 10, 31)));
        Assert.NotEmpty(october);
        Assert.All(october, n => Assert.Equal(10, n.Date.Month));
        Assert.Equal(3, (await News.GetNewsAsync(new NewsQuery(Max: 3))).Count);

        // Repeating the last day writes nothing new.
        var count = news.Count;
        Assert.Equal(NewsDay.None, await News.ProcessDayAsync(today.AddDays(-1), (await Careers.LoadAsync())!.Season.Id, user));
        Assert.Equal(count, (await News.GetNewsAsync(new NewsQuery(Max: 100_000))).Count);

        var fans = await News.GetFansAsync(user);
        Assert.NotNull(fans);
        Assert.Equal(clubs[user].Reputation, fans.Reputation);

        // The news screen: the club's stories, the selected story's text, fans and reputation history.
        var lookups = new EditorLookups();
        lookups.Refresh(await _db.Services.GetRequiredService<IDatabaseEditor>().LoadAsync());
        var screen = new NewsViewModel(News, lookups);
        screen.ClubId = user;
        await screen.RefreshAsync();
        Assert.Equal(mine.Count, screen.Stories.Count);
        Assert.Contains(screen.Stories[0].Headline, screen.SelectedText, StringComparison.Ordinal);
        Assert.Contains("fans expect", screen.FansText, StringComparison.Ordinal);
        Assert.Equal((await News.GetReputationHistoryAsync(user)).Count, screen.Reputation.Count);
        Assert.Equal(lookups.Clubs.Count + 1, screen.ClubChoices.Count);
        screen.Category = NewsCategory.MatchReview;
        await screen.RefreshAsync();
        Assert.All(screen.Stories, r => Assert.Equal(NewsCategory.MatchReview, r.Item.Category));

        // The screen renders with the real themes (missing resources or broken bindings fail here).
        var rendered = Players.PlayerScreensRenderTests.RunOnSta(() =>
        {
            var resources = (System.Windows.Application.Current ?? new System.Windows.Application()).Resources;
            foreach (var name in new[] { "Shell", "Editor", "News" })
            {
                resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"pack://application:,,,/Ebm.Desktop;component/Themes/{name}.xaml") });
            }

            return Players.PlayerScreensRenderTests.Render("news", screen);
        });
        Assert.True(rendered > 0);
    }

    private static bool Involves(Fixture fixture, ClubId club) => fixture.HomeClubId == club || fixture.AwayClubId == club;

    private async Task PlaySeasonAsync()
    {
        while (true)
        {
            var report = await Careers.ContinueAsync();
            if (report.StoppedBecause == CalendarStop.SeasonComplete)
            {
                return;
            }

            await Careers.PlayUserMatchAsync();
            if ((await Careers.LoadAsync())!.SeasonComplete)
            {
                return;
            }
        }
    }
}
