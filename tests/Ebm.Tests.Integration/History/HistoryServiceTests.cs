using System.Reflection;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.History;
using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.History;
using Ebm.Domain.Matches;
using Ebm.Domain.News;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Domain.Staff;
using Ebm.Services.Careers;
using Ebm.Services.Competitions;
using Ebm.Services.Editing;
using Ebm.Services.History;
using Ebm.Services.Import;
using Ebm.Services.News;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.History;

/// <summary>Step 32: a played season turns into history — awards, records, hall of fame, coaches, youth (docs/HISTORY.md).</summary>
public sealed class HistoryServiceTests : IAsyncLifetime
{
    internal static readonly string SampleFolder = Path.Combine(
        typeof(HistoryServiceTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "sample");

    private TestDatabase _db = null!;

    private ICareerService Careers => _db.Services.GetRequiredService<ICareerService>();

    private IHistoryService History => _db.Services.GetRequiredService<IHistoryService>();

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task A_played_season_is_recorded_and_the_world_moves_on()
    {
        var user = await StartSampleCareerAsync(_db);
        var start = (await Careers.LoadAsync())!;
        var season = start.Season.Id;

        // A 42-year-old star leader at another club: he must retire, enter the hall of fame and start coaching.
        var veteran = await _db.ReadAsync(async s =>
        {
            var ids = (await s.Repo<Contract, ContractId>().ListAsync(c => c.PlayerId != null && c.ClubId != user)).Select(c => c.PlayerId!.Value).ToList();
            return (await s.Repo<Player, PlayerId>().ListAsync(p => ids.Contains(p.Id))).OrderBy(p => p.LastName).First();
        });
        await _db.ExecuteSqlAsync($"UPDATE Players SET DateOfBirth = '1985-01-15', Reputation = 100 WHERE FirstName = '{veteran.FirstName}' AND LastName = '{veteran.LastName}' AND DateOfBirth = '{veteran.DateOfBirth:yyyy-MM-dd}'");
        await _db.ExecuteSqlAsync($"UPDATE PlayerAttributes SET Leadership = 95, DecisionMaking = 95, CourtVision = 95, WorkRate = 95 WHERE PlayerId IN (SELECT Id FROM Players WHERE FirstName = '{veteran.FirstName}' AND LastName = '{veteran.LastName}' AND DateOfBirth = '1985-01-15')");
        await _db.WriteAsync(s => s.Repo<SeasonAward, SeasonAwardId>().Add(new SeasonAward(SeasonAwardId.New(), season, CompetitionId.New(), AwardKind.MostValuablePlayer, 1, user, veteran.Id, null, 300)));

        await PlaySeasonAsync(Careers);
        var seasonEnd = start.Season.EndDate;
        await Careers.StartNextSeasonAsync();
        await Careers.ContinueAsync(); // through 30 June (retirements, youth intake) to the next season's first game

        // Awards: every kind once per completed league (five all-league players), from players who played enough.
        var leagues = await _db.ReadAsync(s => s.Repo<Competition, CompetitionId>().ListAsync(c => c.Type == CompetitionType.League));
        var played = leagues.Where(l => l.Name.StartsWith("Serie", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(played);
        var fixtures = (await _db.ReadAsync(s => s.Repo<Fixture, FixtureId>().ListAsync(f => f.SeasonId == season))).ToDictionary(f => f.Id);
        var matches = (await _db.ReadAsync(s => s.Repo<Match, MatchId>().ListAsync())).Where(m => fixtures.ContainsKey(m.FixtureId) && !m.IsForfeit).ToDictionary(m => m.Id);
        var stats = (await _db.ReadAsync(s => s.Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>().ListAsync())).Where(p => matches.ContainsKey(p.MatchId)).ToList();
        foreach (var league in played)
        {
            var awards = await History.GetAwardsAsync(league.Id, season);
            Assert.Equal(6 + 5 + 1, awards.Count);
            Assert.All(Enum.GetValues<AwardKind>(), k => Assert.Contains(awards, a => a.Kind == k));
            var regular = stats.Where(p => fixtures[matches[p.MatchId].FixtureId].CompetitionId == league.Id && fixtures[matches[p.MatchId].FixtureId].Stage == FixtureStage.RegularSeason).ToList();
            var games = regular.GroupBy(p => p.PlayerId).ToDictionary(g => g.Key, g => (Games: g.Count(), Points: g.Sum(p => p.BoxScore.Points)));
            var min = (int)Math.Ceiling(games.Values.Max(g => g.Games) * HistoryRules.MinGameShare);
            Assert.All(awards.Where(a => a.PlayerId is not null), a => Assert.True(games[a.PlayerId!.Value].Games >= min));
            var bestAverage = games.Values.Where(g => g.Games >= min).Max(g => Math.Round((double)g.Points / g.Games, 1));
            Assert.Equal(bestAverage, Math.Round(awards.Single(a => a.Kind == AwardKind.TopScorer).Value, 1));

            // Winners, snapshot and records of the season.
            var outcome = await _db.Services.GetRequiredService<ICompetitionService>().GetOutcomeAsync(league.Id, season);
            var champion = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().FindAsync(outcome.Champion)))!.Name;
            Assert.Equal(champion, Assert.Single(await History.GetWinnersAsync(league.Id)).Champion);
            var evolution = Assert.Single(await History.GetLeagueEvolutionAsync(league.Id));
            Assert.Equal(champion, evolution.Champion);
            Assert.InRange(evolution.MeanOverall, 20, 90);
            var records = await History.GetRecordsAsync(league.Id);
            var maxPoints = stats.Where(p => fixtures[matches[p.MatchId].FixtureId].CompetitionId == league.Id).Max(p => p.BoxScore.Points);
            Assert.StartsWith($"{maxPoints} pts", records.Single(r => r.Record == "Most points in a game").Value, StringComparison.Ordinal);
            Assert.Contains(records, r => r.Record == "Most titles" && r.Holder == champion);
            Assert.Contains(new Trophy(start.Season.Name, league.Name), (await History.GetClubHonoursAsync(outcome.Champion)).Trophies);
        }

        // Reputation drift is recorded and every club's history still adds up to its reputation.
        var changes = await _db.ReadAsync(s => s.Repo<ReputationChange, ReputationChangeId>().ListAsync(c => c.Cause == ReputationCause.LongTermDrift));
        var clubs = await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync());
        Assert.All(changes, c => Assert.Equal(season, c.SeasonId));
        Assert.Equal(changes.Count, changes.Select(c => c.ClubId).Distinct().Count());
        foreach (var club in clubs)
        {
            var history = await _db.Services.GetRequiredService<INewsService>().GetReputationHistoryAsync(club.Id);
            if (history.Count > 0)
            {
                Assert.Equal(club.Reputation, history[^1].After);
            }
        }

        // The veteran: retired, kept, in the hall of fame, now a head coach on the staff market or hired.
        var retired = (await _db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(veteran.Id)))!;
        Assert.Equal(seasonEnd.AddDays(1), retired.RetiredOn);
        var career = (await History.GetPlayerCareerAsync(veteran.Id))!;
        Assert.True(career.Totals.Games > 0, "The retired player's career is still there.");
        Assert.Equal(StaffRole.HeadCoach, career.CoachingRole);
        Assert.NotNull(career.HallOfFame);
        Assert.Contains(await History.GetHallOfFameAsync(), h => h.PlayerId == veteran.Id && h.InductedOn == seasonEnd);
        var coach = Assert.Single(await _db.ReadAsync(s => s.Repo<StaffMember, StaffMemberId>().ListAsync(m => m.FormerPlayerId == veteran.Id)));
        Assert.Equal((veteran.FirstName, veteran.LastName), (coach.FirstName, coach.LastName));

        // Youth intake: one or two academy players per club, 17, on a youth contract in the youth squad.
        var academy = await _db.ReadAsync(s => s.Repo<Player, PlayerId>().ListAsync(p => p.YouthClubId != null));
        Assert.All(clubs, c => Assert.InRange(academy.Count(p => p.YouthClubId == c.Id), 1, 2));
        var contracts = await _db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync(c => c.PlayerId != null));
        var spots = (await _db.ReadAsync(s => s.Repo<RosterSpot, RosterSpotId>().ListAsync())).ToDictionary(x => x.PlayerId);
        foreach (var youth in academy)
        {
            Assert.Equal(HistoryRules.YouthAge, youth.AgeOn(seasonEnd));
            Assert.Contains(contracts, c => c.PlayerId == youth.Id && c.ClubId == youth.YouthClubId && c.StartDate == seasonEnd);
            Assert.Equal((youth.YouthClubId!.Value, RosterRole.Youth), (spots[youth.Id].ClubId, spots[youth.Id].Role));
        }

        Assert.Equal(academy.Count(p => p.YouthClubId == user), (await History.GetClubHonoursAsync(user)).AcademyPlayers);

        // Everything is safe to repeat.
        Assert.Equal(new YouthIntake(0, 0), await History.ProcessYouthIntakeAsync(seasonEnd, [user]));
        Assert.Equal(HistorySeason.None, await History.ProcessSeasonEndAsync(season, seasonEnd));
        var again = await History.ProcessRetirementsAsync([veteran.Id], seasonEnd);
        Assert.Empty(again.Inducted);
        Assert.Empty(again.NewCoaches);

        // The history screen, rendered with the real themes.
        var lookups = new EditorLookups();
        lookups.Refresh(await _db.Services.GetRequiredService<IDatabaseEditor>().LoadAsync());
        var screen = new HistoryViewModel(History, lookups) { CompetitionId = played[0].Id, PlayerId = veteran.Id, ClubId = user };
        await screen.RefreshAsync();
        Assert.Equal(12, screen.Awards.Count);
        Assert.NotEmpty(screen.Records);
        Assert.Contains("Hall of fame", screen.CareerText, StringComparison.Ordinal);
        Assert.Contains(screen.HallOfFame, h => h.PlayerId == veteran.Id);
        Assert.Equal("Career", screen.Career[^1].Season);
        var rendered = Players.PlayerScreensRenderTests.RunOnSta(() =>
        {
            var resources = (System.Windows.Application.Current ?? new System.Windows.Application()).Resources;
            foreach (var name in new[] { "Shell", "Editor", "History" })
            {
                resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"pack://application:,,,/Ebm.Desktop;component/Themes/{name}.xaml") });
            }

            return Players.PlayerScreensRenderTests.Render("history", screen);
        });
        Assert.True(rendered > 0);
    }

    internal static async Task<ClubId> StartSampleCareerAsync(TestDatabase db)
    {
        var report = await db.Services.GetRequiredService<IDataImportService>()
            .ImportAsync([Path.Combine(SampleFolder, "italy-sample.json"), Path.Combine(SampleFolder, "italy-squads.json")], new ImportOptions());
        Assert.True(report.Committed);
        var user = (await db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync(c => c.Name == "Aurora Milano"))).Single().Id;
        await db.Services.GetRequiredService<ICareerService>().StartAsync(user, "Test Manager");
        return user;
    }

    internal static async Task PlaySeasonAsync(ICareerService careers)
    {
        while (true)
        {
            var report = await careers.ContinueAsync();
            if (report.StoppedBecause == CalendarStop.SeasonComplete)
            {
                return;
            }

            await careers.PlayUserMatchAsync();
            if ((await careers.LoadAsync())!.SeasonComplete)
            {
                return;
            }
        }
    }
}
