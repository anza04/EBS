using System.Reflection;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;
using Ebm.Services;
using Ebm.Services.Careers;
using Ebm.Services.Competitions;
using Ebm.Services.Import;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Careers;

/// <summary>Plays careers on the sample dataset through the calendar: whole seasons, promotion/relegation and the next season.</summary>
public sealed class CareerServiceTests : IAsyncLifetime
{
    private static readonly string SampleFolder = Path.Combine(
        typeof(CareerServiceTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "sample");

    private TestDatabase _db = null!;

    private ICareerService Careers => _db.Services.GetRequiredService<ICareerService>();

    private ICompetitionService Competitions => _db.Services.GetRequiredService<ICompetitionService>();

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task ImportAsync(params string[] files)
    {
        var report = await _db.Services.GetRequiredService<IDataImportService>()
            .ImportAsync([.. files.Select(f => Path.Combine(SampleFolder, f))], new ImportOptions());
        Assert.True(report.Committed, string.Join(Environment.NewLine, report.Problems.Select(p => p.ToString())));
    }

    private async Task<Club> ClubAsync(string name) =>
        (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync(c => c.Name == name))).Single();

    /// <summary>Continues until the season is complete, playing the user's matches instantly.</summary>
    private async Task<int> PlaySeasonAsync()
    {
        var matches = 0;
        while (true)
        {
            var report = await Careers.ContinueAsync();
            matches += report.MatchesPlayed;
            if (report.StoppedBecause == CalendarStop.SeasonComplete)
            {
                return matches;
            }

            Assert.Equal(CalendarStop.UserMatch, report.StoppedBecause);
            await Careers.PlayUserMatchAsync();
            matches++;
            if ((await Careers.LoadAsync())!.SeasonComplete)
            {
                return matches; // the user's club played the last game (a final)
            }
        }
    }

    [Fact]
    public async Task A_full_season_is_played_and_the_next_one_is_generated()
    {
        await ImportAsync("italy-sample.json", "italy-squads.json");
        Assert.Equal(14, (await Careers.GetClubOptionsAsync()).Count);
        var aurora = await ClubAsync("Aurora Milano");

        var state = await Careers.StartAsync(aurora.Id, "Test Manager");
        Assert.Equal(new DateOnly(2026, 9, 24), state.Today);
        Assert.Equal(state.Today, _db.Services.GetRequiredService<GameClock>().Today);
        Assert.Equal(2, state.Competitions.Count);
        await Assert.ThrowsAsync<DomainValidationException>(() => Careers.StartAsync(aurora.Id, "Again"));

        var played = await PlaySeasonAsync();

        // Every fixture has a result, nobody forfeited, and the standings add up.
        var season = state.Season;
        var fixtures = await _db.ReadAsync(s => s.Repo<Fixture, FixtureId>().ListAsync(f => f.SeasonId == season.Id));
        var results = await _db.ReadAsync(s => s.Repo<Match, MatchId>().ListAsync());
        Assert.Equal(fixtures.Count, results.Count);
        Assert.Equal(fixtures.Count, played);
        Assert.DoesNotContain(results, m => m.IsForfeit);
        Assert.Contains(fixtures, f => f.Stage == FixtureStage.Playoffs);

        var review = await Careers.GetSeasonReviewAsync();
        var serieA = review.Competitions.Single(c => c.Competition.Name == "Serie A");
        var serieA2 = review.Competitions.Single(c => c.Competition.Name == "Serie A2");
        Assert.Equal(2, serieA.Relegated.Count);
        Assert.Equal(2, serieA2.Promoted.Count);
        Assert.NotNull(serieA.UserPosition);
        Assert.Null(serieA2.UserPosition);
        await Assert.ThrowsAsync<DomainValidationException>(() => Careers.ContinueAsync());

        // The next season: entries after promotion/relegation, schedules, the career moved on.
        var next = await Careers.StartNextSeasonAsync();
        Assert.Equal("2027-28", next.Season.Name);
        Assert.Equal(new DateOnly(2027, 10, 1), next.Season.StartDate);
        Assert.Equal(2, next.Career.SeasonsPlayed);
        Assert.Equal(2, next.Competitions.Count);
        Assert.All(next.Competitions, c => Assert.Equal(CompetitionPhase.RegularSeason, c.Phase));
        var names = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync())).ToDictionary(c => c.Id, c => c.Name);
        var serieANext = (await Competitions.GetStandingsAsync(serieA.Competition.Id, next.Season.Id)).Select(r => names[r.ClubId]).ToList();
        var serieA2Next = (await Competitions.GetStandingsAsync(serieA2.Competition.Id, next.Season.Id)).Select(r => names[r.ClubId]).ToList();
        Assert.Equal(8, serieANext.Count);
        Assert.Equal(6, serieA2Next.Count);
        Assert.All(serieA2.Promoted, c => Assert.Contains(c, serieANext));
        Assert.All(serieA.Relegated, c => Assert.Contains(c, serieA2Next));

        // History is kept: last season's results and the club's record.
        Assert.Equal(results.Count, (await _db.ReadAsync(s => s.Repo<Match, MatchId>().ListAsync())).Count);
        var history = await Careers.GetClubHistoryAsync(aurora.Id);
        var first = history[0];
        Assert.Equal((season.Id, CompetitionPhase.Completed), (first.Season.Id, first.Phase));
        Assert.Equal(serieA.UserPosition, first.Position);
        Assert.Equal(serieA.Champion == "Aurora Milano", first.Champion);
        Assert.Equal(2, history.Count);

        // The offseason: contracts ending on 30 June expire or are renewed, the AI transfer window opens, the new season starts.
        var expiring = await _db.ReadAsync(s => s.Repo<Ebm.Domain.Contracts.Contract, Ebm.Domain.Contracts.ContractId>()
            .ListAsync(c => c.EndDate == new DateOnly(2027, 6, 30)));
        Assert.NotEmpty(expiring);
        var report = await Careers.ContinueAsync();
        Assert.Equal(CalendarStop.UserMatch, report.StoppedBecause);
        Assert.Contains(report.Days, d => d.TransferWindow is not null && d.Date == new DateOnly(2027, 6, 30));
        Assert.True(report.Days.Sum(d => d.DateChanges.Contracts.Expired) > 0);
        Assert.True(report.Days.Sum(d => d.Management.Renewals) > 0);
        Assert.True(report.Days.Sum(d => d.Management.SalariesPaid) > 0);
        Assert.True(report.State.Today >= next.Season.StartDate);
    }

    [Fact]
    public async Task Clubs_without_five_players_forfeit_and_the_user_must_play_first()
    {
        await ImportAsync("italy-sample.json");
        var aurora = await ClubAsync("Aurora Milano");
        var start = await Careers.StartAsync(aurora.Id, "Test Manager");

        // Serie A2 clubs have no players at all: their games are forfeited by the home side.
        var a2 = start.Competitions.Single(c => c.Competition.Name == "Serie A2").Competition;
        var a2Fixture = (await _db.Services.GetRequiredService<Ebm.Services.Matches.IMatchService>().GetFixturesAsync(a2.Id, start.Season.Id)).Last();
        var forfeit = await _db.Services.GetRequiredService<Ebm.Services.Matches.IMatchService>().PlayOrForfeitAsync(a2Fixture.Fixture.Id);
        Assert.Equal((true, 0, Match.ForfeitScore), (forfeit.IsForfeit, forfeit.HomeScore, forfeit.AwayScore));

        var report = await Careers.ContinueAsync();
        Assert.Equal(CalendarStop.UserMatch, report.StoppedBecause);
        Assert.Equal(new DateOnly(2026, 10, 1), report.State.Today);
        Assert.Equal(7, report.Days.Count);
        Assert.Contains(report.Days, d => d.Date.DayOfWeek == DayOfWeek.Monday && d.TransferWindow is not null); // clubs short of players recruit
        await Assert.ThrowsAsync<DomainValidationException>(() => Careers.AdvanceDayAsync());

        await Careers.PlayUserMatchAsync();
        await Assert.ThrowsAsync<DomainValidationException>(() => Careers.PlayUserMatchAsync());

        var day = await Careers.AdvanceDayAsync();
        Assert.Equal(new DateOnly(2026, 10, 1), day.Date);
        Assert.Equal(6, day.Results.Count); // the other three Serie A games and three Serie A2 games
        Assert.Equal(new DateOnly(2026, 10, 2), (await Careers.LoadAsync())!.Today);
    }

    [Fact]
    public async Task Next_season_is_refused_before_the_season_ends()
    {
        await ImportAsync("italy-sample.json");
        await Assert.ThrowsAsync<DomainValidationException>(() => Careers.ContinueAsync());
        var aurora = await ClubAsync("Aurora Milano");
        await Careers.StartAsync(aurora.Id, "Test Manager");
        await Assert.ThrowsAsync<DomainValidationException>(() => Careers.StartNextSeasonAsync());
        Assert.Single(await _db.ReadAsync(s => s.Repo<Season, SeasonId>().ListAsync()));
    }
}
