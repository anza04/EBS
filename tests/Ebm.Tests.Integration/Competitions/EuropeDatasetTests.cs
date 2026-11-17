using System.Reflection;
using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Competitions.Rules;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;
using Ebm.Services.Competitions;
using Ebm.Services.Import;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Competitions;

/// <summary>
/// Step 15 acceptance: every league of the European dataset (data/europe) imports, schedules inside the season,
/// produces standings and finishes with a champion and the configured promotion and relegation.
/// </summary>
[Trait("Category", "Slow")] // full datasets or multi-season simulation: run before releases, not on every change
public sealed class EuropeDatasetTests : IAsyncLifetime
{
    private static readonly string DatasetFile = Path.Combine(
        typeof(EuropeDatasetTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "europe",
        "europe-2026-27.json");

    private TestDatabase _db = null!;

    private ICompetitionService Service => _db.Services.GetRequiredService<ICompetitionService>();

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Every_league_plays_a_full_season_with_promotion_and_relegation()
    {
        var report = await _db.Services.GetRequiredService<IDataImportService>().ImportAsync([DatasetFile], new ImportOptions());
        Assert.True(report.Committed);
        var season = (await _db.ReadAsync(s => s.Repo<Season, SeasonId>().ListAsync())).Single();
        var all = await _db.ReadAsync(s => s.Repo<Competition, CompetitionId>().ListAsync());
        var competitions = all.Where(c => c.Type == CompetitionType.League).ToList();
        var divisions = (await _db.ReadAsync(s => s.Repo<Division, DivisionId>().ListAsync())).ToDictionary(d => d.Id);
        Assert.Equal(35, competitions.Count);
        Assert.Equal(21, all.Count(c => c.Type == CompetitionType.Cup));
        Assert.Equal(18, competitions.Select(c => c.CountryId).OfType<object>().Distinct().Count());

        var finished = new List<(Division Division, IReadOnlyList<ClubId> Clubs, SeasonOutcome Outcome)>();
        var random = new Random(15);
        foreach (var competition in competitions.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            var rules = CompetitionRules.Parse(competition.RulesJson);
            await Service.GenerateScheduleAsync(competition.Id, season.Id);
            var outcome = await PlaySeasonAsync(competition, season, random);
            var fixtures = await FixturesAsync(competition, season);
            Assert.All(fixtures, f => Assert.InRange(f.Date, season.StartDate, season.EndDate));
            Assert.Equal(season.StartDate.AddDays(rules.RegularSeason.StartOffsetDays), fixtures.Min(f => f.Date));

            var table = await Service.GetStandingsAsync(competition.Id, season.Id);
            Assert.Equal(rules.Registration.MaxTeams, table.Count);
            Assert.Equal(table.Sum(r => r.Won), table.Sum(r => r.Lost));
            Assert.Equal(rules.Promotion.Places, outcome.Promoted.Count);
            Assert.Equal(rules.Relegation.Direct, outcome.Relegated.Count);
            Assert.Equal(table.TakeLast(rules.Relegation.Direct).Select(r => r.ClubId), outcome.Relegated);
            if (rules.Playoffs is null)
            {
                Assert.Equal(table[0].ClubId, outcome.Champion);
            }

            finished.Add((divisions[competition.DivisionId!.Value], table.Select(r => r.ClubId).ToList(), outcome));
        }

        // Promotion and relegation keep every division's size for the next season.
        foreach (var league in finished.GroupBy(f => f.Division.LeagueId))
        {
            var next = SeasonTransition.NextEntries([.. league.Select(f => new DivisionSeason(f.Division.Level, f.Clubs, f.Outcome))]);
            Assert.All(league, f => Assert.Equal(f.Clubs.Count, next[f.Division.Level].Count));
            Assert.Equal(league.Sum(f => f.Clubs.Count), next.Values.SelectMany(c => c).Distinct().Count());
        }
    }

    [Fact]
    public async Task The_international_competitions_qualify_distinct_clubs_and_are_drawn_on_free_days()
    {
        Assert.True((await _db.Services.GetRequiredService<IDataImportService>().ImportAsync([DatasetFile], new ImportOptions())).Committed);
        var season = (await _db.ReadAsync(s => s.Repo<Season, SeasonId>().ListAsync())).Single();
        var all = await _db.ReadAsync(s => s.Repo<Competition, CompetitionId>().ListAsync());
        foreach (var league in all.Where(c => c.Type == CompetitionType.League))
        {
            await Service.GenerateScheduleAsync(league.Id, season.Id);
        }

        var international = all.Where(c => c.ParseRules().Cup?.Entry is not null).OrderBy(c => c.ParseRules().Cup!.Entry!.Tier).ToList();
        Assert.Equal(["EL", "EC", "BCL", "FEC"], international.Select(c => c.ShortName));
        Assert.Equal(104, await Service.QualifyAsync(season.Id));

        var entries = await _db.ReadAsync(s => s.Repo<CompetitionParticipant, CompetitionParticipantId>().ListAsync(p => p.SeasonId == season.Id));
        var clubs = international.Select(c => entries.Where(p => p.CompetitionId == c.Id).Select(p => p.ClubId).ToList()).ToList();
        Assert.Equal([20, 20, 32, 32], clubs.Select(c => c.Count));
        Assert.Equal(104, clubs.SelectMany(c => c).Distinct().Count());
        foreach (var competition in international)
        {
            await Service.GenerateScheduleAsync(competition.Id, season.Id);
            Assert.All(await FixturesAsync(competition, season), f => Assert.InRange(f.Date, season.StartDate, season.EndDate));
        }

        Assert.Empty(await Service.GetCalendarConflictsAsync(season.Id));
    }

    private async Task<List<Fixture>> FixturesAsync(Competition competition, Season season) =>
        [.. await _db.ReadAsync(s => s.Repo<Fixture, FixtureId>().ListAsync(f => f.CompetitionId == competition.Id && f.SeasonId == season.Id))];

    /// <summary>Plays every fixture with seeded random scores, advancing until the season is complete.</summary>
    private async Task<SeasonOutcome> PlaySeasonAsync(Competition competition, Season season, Random random)
    {
        var played = new HashSet<FixtureId>();
        for (var step = 0; step < 100; step++)
        {
            foreach (var fixture in (await FixturesAsync(competition, season)).Where(f => played.Add(f.Id)))
            {
                var home = random.Next(60, 100);
                var away = random.Next(60, 100);
                await Service.RecordResultAsync(fixture.Id, home, away == home ? home + 1 : away);
            }

            var progress = await Service.AdvanceAsync(competition.Id, season.Id);
            if (progress.Phase == CompetitionPhase.Completed)
            {
                return await Service.GetOutcomeAsync(competition.Id, season.Id);
            }

            Assert.True(progress.FixturesCreated > 0, $"{competition.Name}: the season stalled.");
        }

        throw new InvalidOperationException($"{competition.Name}: the season did not finish.");
    }
}
