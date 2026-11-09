using System.Reflection;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Countries;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;
using Ebm.Domain.Standings;
using Ebm.Services.Competitions;
using Ebm.Services.Import;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Competitions;

/// <summary>Runs full seasons of the sample dataset's two divisions through the competition service.</summary>
public sealed class CompetitionServiceTests : IAsyncLifetime
{
    private static readonly string SampleFile = Path.Combine(
        typeof(CompetitionServiceTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "sample",
        "italy-sample.json");

    private TestDatabase _db = null!;
    private Competition _serieA = null!;
    private Competition _serieA2 = null!;
    private Competition _cup = null!;
    private Season _season = null!;

    private ICompetitionService Service => _db.Services.GetRequiredService<ICompetitionService>();

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
        var report = await _db.Services.GetRequiredService<IDataImportService>().ImportAsync([SampleFile], new ImportOptions());
        Assert.True(report.Committed);
        var competitions = await _db.ReadAsync(s => s.Repo<Competition, CompetitionId>().ListAsync());
        _serieA = competitions.Single(c => c.Name == "Serie A");
        _serieA2 = competitions.Single(c => c.Name == "Serie A2");
        _cup = competitions.Single(c => c.Type == CompetitionType.Cup);
        _season = (await _db.ReadAsync(s => s.Repo<Season, SeasonId>().ListAsync())).Single();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Schedule_gives_every_club_the_right_number_of_games_inside_the_season()
    {
        var progress = await Service.GenerateScheduleAsync(_serieA.Id, _season.Id);

        Assert.Equal(new CompetitionProgress(CompetitionPhase.RegularSeason, 56), progress);
        var fixtures = await FixturesAsync(_serieA);
        Assert.All(fixtures.SelectMany(f => new[] { f.HomeClubId, f.AwayClubId }).GroupBy(c => c), club => Assert.Equal(14, club.Count()));
        Assert.All(fixtures.GroupBy(f => f.HomeClubId), club => Assert.Equal(7, club.Count()));
        Assert.Equal(_season.StartDate, fixtures.Min(f => f.Date));
        Assert.Equal(_season.StartDate.AddDays(13 * 7), fixtures.Max(f => f.Date));
        Assert.All(fixtures, f => Assert.Equal((FixtureStage.RegularSeason, 1), (f.Stage, f.GameNumber)));
        Assert.Equal(CompetitionPhase.RegularSeason, (await Service.AdvanceAsync(_serieA.Id, _season.Id)).Phase);
    }

    [Fact]
    public async Task Full_seasons_produce_standings_playoffs_promotion_and_relegation()
    {
        await Service.GenerateScheduleAsync(_serieA.Id, _season.Id);
        await Service.GenerateScheduleAsync(_serieA2.Id, _season.Id, _season.StartDate.AddDays(3));

        var a = await PlaySeasonAsync(_serieA, seed: 1);
        var a2 = await PlaySeasonAsync(_serieA2, seed: 2);

        // Stored standings agree with the calculated table.
        var table = await Service.GetStandingsAsync(_serieA.Id, _season.Id);
        var stored = await _db.ReadAsync(s => s.Repo<LeagueStanding, LeagueStandingId>().ListAsync(x => x.CompetitionId == _serieA.Id));
        Assert.Equal(8, table.Count);
        Assert.All(table, row =>
        {
            var match = stored.Single(s => s.ClubId == row.ClubId);
            Assert.Equal((14, row.Won, row.PointsFor, row.PointsAgainst, row.Points), (match.Played, match.Won, match.PointsFor, match.PointsAgainst, match.StandingPoints));
            Assert.Equal(row.Won * 2, row.Points);
        });
        Assert.Equal(table.Sum(r => r.Won), table.Sum(r => r.Lost));

        // Serie A: top 4 play best-of-5 series; the bottom two go down.
        Assert.Equal(a.Champion, a.Outcome.Champion);
        Assert.Contains(a.Champion, table.Take(4).Select(r => r.ClubId));
        Assert.Empty(a.Outcome.Promoted);
        Assert.Equal(table.TakeLast(2).Select(r => r.ClubId), a.Outcome.Relegated);
        var playoffs = (await FixturesAsync(_serieA)).Where(f => f.Stage == FixtureStage.Playoffs).ToList();
        Assert.InRange(playoffs.Count(f => f.Round == 1), 6, 10);
        Assert.InRange(playoffs.Count(f => f.Round == 2), 3, 5);
        Assert.True(playoffs.Min(f => f.Date) > _season.StartDate.AddDays(13 * 7));

        // Serie A2: the leader goes up directly, positions 2-5 play off for the second place.
        var tableA2 = await Service.GetStandingsAsync(_serieA2.Id, _season.Id);
        Assert.Equal([tableA2[0].ClubId, a2.Champion], a2.Outcome.Promoted);
        Assert.Contains(a2.Champion, tableA2.Skip(1).Take(4).Select(r => r.ClubId));
        Assert.Empty(a2.Outcome.Relegated);
    }

    [Fact]
    public async Task Registration_rules_are_enforced()
    {
        var portugal = new Country(CountryId.New(), "POR", "Portugal");
        var foreign = new Club(ClubId.New(), "Mock Lisboa", "LIS", portugal.Id);
        var local = new Club(ClubId.New(), "Mock Firenze", "FIR", _serieA.CountryId!.Value);
        var season = new Season(SeasonId.New(), "2027-28", new DateOnly(2027, 10, 1), new DateOnly(2028, 6, 30));
        await _db.WriteAsync(s =>
        {
            s.Repo<Country, CountryId>().Add(portugal);
            s.Repo<Club, ClubId>().Add(foreign);
            s.Repo<Club, ClubId>().Add(local);
            s.Repo<Season, SeasonId>().Add(season);
        });
        var serieAClubs = (await Service.GetStandingsAsync(_serieA.Id, _season.Id)).Select(r => r.ClubId).ToList();

        await Assert.ThrowsAsync<DomainValidationException>(() => Service.RegisterClubsAsync(_serieA2.Id, season.Id, [foreign.Id]));
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.RegisterClubsAsync(_serieA.Id, _season.Id, [local.Id]));
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.RegisterClubsAsync(_serieA2.Id, season.Id, [local.Id, local.Id]));
        await Service.RegisterClubsAsync(_serieA.Id, season.Id, [serieAClubs[0]]);
        var twice = await Assert.ThrowsAsync<DomainValidationException>(() => Service.RegisterClubsAsync(_serieA2.Id, season.Id, [serieAClubs[0]]));
        Assert.Contains("another league", twice.Message, StringComparison.Ordinal);

        await Service.RegisterClubsAsync(_cup.Id, _season.Id, [serieAClubs[0], local.Id]);
        await Service.RegisterClubsAsync(_serieA2.Id, season.Id, [local.Id]);
        Assert.Single(await Service.GetStandingsAsync(_serieA2.Id, season.Id));

        await Service.GenerateScheduleAsync(_serieA.Id, _season.Id);
        var closed = await Assert.ThrowsAsync<DomainValidationException>(() => Service.RegisterClubsAsync(_serieA.Id, _season.Id, [local.Id]));
        Assert.Contains("closed", closed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Schedules_and_results_are_validated()
    {
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.GenerateScheduleAsync(_cup.Id, _season.Id));
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.GenerateScheduleAsync(_serieA.Id, _season.Id, _season.EndDate.AddDays(-30)));
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.GenerateScheduleAsync(_serieA.Id, _season.Id, _season.StartDate.AddDays(-1)));
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.GetOutcomeAsync(_serieA.Id, _season.Id));
        Assert.Equal(CompetitionPhase.Registration, (await Service.AdvanceAsync(_serieA.Id, _season.Id)).Phase);

        await Service.GenerateScheduleAsync(_serieA.Id, _season.Id);
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.GenerateScheduleAsync(_serieA.Id, _season.Id));

        var fixture = (await FixturesAsync(_serieA))[0];
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.RecordResultAsync(fixture.Id, 80, 80));
        await Service.RecordResultAsync(fixture.Id, 81, 80, overtimes: 1);
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.RecordResultAsync(fixture.Id, 90, 80));
        var leader = (await Service.GetStandingsAsync(_serieA.Id, _season.Id))[0];
        Assert.Equal((fixture.HomeClubId, 1, 2), (leader.ClubId, leader.Won, leader.Points));
    }

    [Fact]
    public async Task Promotion_needs_a_division_above()
    {
        await _db.WriteAsync(s =>
        {
            var top = s.Repo<Competition, CompetitionId>().FindAsync(_serieA.Id).GetAwaiter().GetResult()!;
            top.UpdateRules("""{"registration":{"minTeams":8,"maxTeams":8},"promotion":{"direct":1}}""");
        });

        var error = await Assert.ThrowsAsync<DomainValidationException>(() => Service.GenerateScheduleAsync(_serieA.Id, _season.Id));
        Assert.Contains("no division above", error.Message, StringComparison.Ordinal);
    }

    private async Task<List<Fixture>> FixturesAsync(Competition competition) =>
        [.. (await _db.ReadAsync(s => s.Repo<Fixture, FixtureId>().ListAsync(f => f.CompetitionId == competition.Id && f.SeasonId == _season.Id)))
            .OrderBy(f => f.Stage).ThenBy(f => f.Round).ThenBy(f => f.GameNumber)];

    /// <summary>Plays every fixture with seeded random scores, advancing until the season is complete.</summary>
    private async Task<(ClubId Champion, SeasonOutcome Outcome)> PlaySeasonAsync(Competition competition, int seed)
    {
        var random = new Random(seed);
        var played = new HashSet<FixtureId>();
        for (var step = 0; step < 100; step++)
        {
            foreach (var fixture in (await FixturesAsync(competition)).Where(f => played.Add(f.Id)))
            {
                var home = random.Next(60, 100);
                var away = random.Next(60, 100);
                await Service.RecordResultAsync(fixture.Id, home, away == home ? home + 1 : away);
            }

            var progress = await Service.AdvanceAsync(competition.Id, _season.Id);
            if (progress.Phase == CompetitionPhase.Completed)
            {
                var outcome = await Service.GetOutcomeAsync(competition.Id, _season.Id);
                return (outcome.Champion, outcome);
            }

            Assert.True(progress.FixturesCreated > 0, "The season stalled.");
        }

        throw new InvalidOperationException("The season did not finish.");
    }
}
