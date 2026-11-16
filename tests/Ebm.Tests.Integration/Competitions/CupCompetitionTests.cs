using System.Reflection;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;
using Ebm.Services.Careers;
using Ebm.Services.Competitions;
using Ebm.Services.Editing;
using Ebm.Services.Import;
using Ebm.Services.Matches;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Competitions;

/// <summary>Step 17 acceptance: cups are configured, drawn, played to a winner and kept in the competition history.</summary>
public sealed class CupCompetitionTests : IAsyncLifetime
{
    private const string KnockoutCup = """
        {"registration":{"maxTeams":16},"cup":{"draw":"Seeded","host":"LowerSeed","startOffsetDays":10,"daysBetweenRounds":14,"daysBetweenLegs":7,
         "rounds":[{"name":"First round"},{"name":"Quarter-finals","legs":2},{"name":"Semi-finals"},{"name":"Final"}]}}
        """;

    private const string FinalFour = """
        {"cup":{"draw":"Bracket","startOffsetDays":20,"daysBetweenRounds":2,"qualification":{"level":1,"positions":4,"afterRound":7},
         "rounds":[{"name":"Semi-finals"},{"name":"Final"}]}}
        """;

    private static readonly string SampleFolder = Path.Combine(
        typeof(CupCompetitionTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "sample");

    private TestDatabase _db = null!;
    private Competition _cup = null!;
    private Competition _serieA = null!;
    private Season _season = null!;

    private ICompetitionService Service => _db.Services.GetRequiredService<ICompetitionService>();

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
        var report = await _db.Services.GetRequiredService<IDataImportService>()
            .ImportAsync([Path.Combine(SampleFolder, "italy-sample.json"), Path.Combine(SampleFolder, "italy-squads.json")], new ImportOptions());
        Assert.True(report.Committed);
        var competitions = await _db.ReadAsync(s => s.Repo<Competition, CompetitionId>().ListAsync());
        _cup = competitions.Single(c => c.Type == CompetitionType.Cup);
        _serieA = competitions.Single(c => c.Name == "Serie A");
        _season = (await _db.ReadAsync(s => s.Repo<Season, SeasonId>().ListAsync())).Single();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task A_knockout_cup_is_drawn_played_on_free_days_and_won()
    {
        await ConfigureAsync(KnockoutCup);
        var clubs = await LeagueClubsAsync();
        Assert.Equal(14, clubs.Count);
        await Service.RegisterClubsAsync(_cup.Id, _season.Id, clubs);
        await Service.GenerateScheduleAsync(_serieA.Id, _season.Id);

        var draw = await Service.GenerateScheduleAsync(_cup.Id, _season.Id);

        Assert.Equal(new CompetitionProgress(CompetitionPhase.Knockout, 6), draw);
        var view = await Service.GetCupAsync(_cup.Id, _season.Id);
        var serieA = (await _db.ReadAsync(s => s.Repo<CompetitionParticipant, CompetitionParticipantId>().ListAsync(p => p.CompetitionId == _serieA.Id))).Select(p => p.ClubId).ToHashSet();
        Assert.All(view.Seeds.Take(8), c => Assert.Contains(c, serieA));
        Assert.Equal(view.Seeds.Take(2), view.Byes);
        var seeds = view.Seeds.ToList();
        Assert.All(view.Ties, t => Assert.True(seeds.IndexOf(t.First) > seeds.IndexOf(t.Second), "the lower seed hosts"));
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.GenerateScheduleAsync(_cup.Id, _season.Id));
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.RegisterClubsAsync(_cup.Id, _season.Id, [clubs[0]]));

        await PlayAsync(_cup);

        view = await Service.GetCupAsync(_cup.Id, _season.Id);
        Assert.Equal(CompetitionPhase.Completed, view.Phase);
        Assert.Equal(["First round", "Quarter-finals", "Semi-finals", "Final"], view.Ties.Select(t => t.RoundName).Distinct());
        Assert.All(view.Ties.Where(t => t.RoundName == "Quarter-finals"), t => Assert.Equal(2, t.Games.Count));
        Assert.All(view.Ties.Where(t => t.Legs == 2 && t.Games.All(g => g.HomeScore != 20 && g.AwayScore != 20)),
            t => Assert.NotEqual(t.FirstPoints, t.SecondPoints));
        var final = view.Ties[^1];
        Assert.Equal((final.Winner, final.Loser), (view.Winner, view.RunnerUp));
        Assert.Equal(view.Winner, (await Service.GetOutcomeAsync(_cup.Id, _season.Id)).Champion);
        Assert.Empty(await Service.GetStandingsAsync(_cup.Id, _season.Id));

        // No club plays twice on a day, and every cup game was played.
        var fixtures = await _db.ReadAsync(s => s.Repo<Fixture, FixtureId>().ListAsync(f => f.SeasonId == _season.Id));
        Assert.All(fixtures.SelectMany(f => new[] { (f.HomeClubId, f.Date), (f.AwayClubId, f.Date) }).GroupBy(x => x), g => Assert.Single(g));
        Assert.All(fixtures.Where(f => f.CompetitionId == _cup.Id), f => Assert.Equal(FixtureStage.Cup, f.Stage));

        var history = Assert.Single(await Service.GetHistoryAsync(_cup.Id));
        Assert.Equal((_season.Id, CompetitionPhase.Completed, view.Winner, view.RunnerUp), (history.Season.Id, history.Phase, history.Champion, history.RunnerUp));
    }

    [Fact]
    public async Task A_qualification_cup_is_drawn_from_the_table_once_its_round_is_played()
    {
        await ConfigureAsync(FinalFour);
        await Service.GenerateScheduleAsync(_serieA.Id, _season.Id);
        var someClub = (await LeagueClubsAsync())[0];
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.RegisterClubsAsync(_cup.Id, _season.Id, [someClub]));

        var fixtures = await _db.ReadAsync(s => s.Repo<Fixture, FixtureId>().ListAsync(f => f.CompetitionId == _serieA.Id));
        var matches = _db.Services.GetRequiredService<IMatchService>();
        foreach (var fixture in fixtures.Where(f => f.Round <= 6))
        {
            await matches.PlayOrForfeitAsync(fixture.Id, seed: fixture.Round);
        }

        Assert.Equal(new CompetitionProgress(CompetitionPhase.Registration, 0), await Service.AdvanceAsync(_cup.Id, _season.Id));
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.GenerateScheduleAsync(_cup.Id, _season.Id));
        foreach (var fixture in fixtures.Where(f => f.Round == 7))
        {
            await matches.PlayOrForfeitAsync(fixture.Id, seed: 7);
        }

        Assert.Equal(new CompetitionProgress(CompetitionPhase.Knockout, 2), await Service.AdvanceAsync(_cup.Id, _season.Id));

        var qualifying = fixtures.Where(f => f.Round <= 7).Select(f => f.Id).ToList();
        Assert.Equal(28, (await _db.ReadAsync(s => s.Repo<Match, MatchId>().ListAsync(m => qualifying.Contains(m.FixtureId)))).Count);
        var view = await Service.GetCupAsync(_cup.Id, _season.Id);
        var standings = await Service.GetStandingsAsync(_serieA.Id, _season.Id);
        Assert.Equal(4, view.Seeds.Count);
        Assert.All(view.Seeds, c => Assert.Contains(c, standings.Select(r => r.ClubId)));
        Assert.Equal([(view.Seeds[0], view.Seeds[3]), (view.Seeds[1], view.Seeds[2])], view.Ties.Select(t => (t.First, t.Second)));
        var cupFixtures = await _db.ReadAsync(s => s.Repo<Fixture, FixtureId>().ListAsync(f => f.CompetitionId == _cup.Id));
        Assert.True(cupFixtures.Min(f => f.Date) > fixtures.Where(f => f.Round == 7).Max(f => f.Date));
        Assert.Equal(4, (await _db.ReadAsync(s => s.Repo<CompetitionParticipant, CompetitionParticipantId>().ListAsync(p => p.CompetitionId == _cup.Id))).Count(p => p.Seed is not null));

        await PlayAsync(_cup);
        Assert.Equal(CompetitionPhase.Completed, await Service.GetPhaseAsync(_cup.Id, _season.Id));
    }

    [Fact]
    public async Task The_career_plays_the_cup_reviews_its_winner_and_draws_it_again_next_season()
    {
        await ConfigureAsync(FinalFour);
        var careers = _db.Services.GetRequiredService<ICareerService>();
        var club = (await LeagueClubsAsync())[0];
        await careers.StartAsync(club, "Cup Manager");
        await careers.SetAssistantAsync(true);

        CalendarReport report;
        do
        {
            report = await careers.ContinueAsync();
        }
        while (report.StoppedBecause != CalendarStop.SeasonComplete);

        var state = (await careers.LoadAsync())!;
        Assert.Contains(state.Competitions, c => c.Competition.Id == _cup.Id && c.Phase == CompetitionPhase.Completed);
        var winner = (await Service.GetOutcomeAsync(_cup.Id, _season.Id)).Champion;
        var review = await careers.GetSeasonReviewAsync();
        Assert.Contains(review.Competitions, c => c.Competition.Id == _cup.Id && c.Champion.Length > 0 && c.Objectives.Count == 0);

        var next = await careers.StartNextSeasonAsync();
        Assert.Empty(await _db.ReadAsync(s => s.Repo<CompetitionParticipant, CompetitionParticipantId>().ListAsync(p => p.CompetitionId == _cup.Id && p.SeasonId == next.Season.Id)));
        var history = await Service.GetHistoryAsync(_cup.Id);
        Assert.Equal(winner, Assert.Single(history).Champion);
    }

    private async Task ConfigureAsync(string rules) =>
        await _db.Services.GetRequiredService<IDatabaseEditor>().SaveCompetitionAsync(
            new CompetitionDraft(_cup.Id, _cup.Name, _cup.ShortName, CompetitionType.Cup, _cup.CountryId, null, rules));

    private async Task<List<ClubId>> LeagueClubsAsync()
    {
        var leagues = (await _db.ReadAsync(s => s.Repo<Competition, CompetitionId>().ListAsync(c => c.Type == CompetitionType.League))).Select(c => c.Id).ToList();
        return [.. (await _db.ReadAsync(s => s.Repo<CompetitionParticipant, CompetitionParticipantId>().ListAsync(p => leagues.Contains(p.CompetitionId))))
            .OrderBy(p => p.CompetitionId == _serieA.Id ? 0 : 1).Select(p => p.ClubId)];
    }

    /// <summary>Plays the competition's games in date order, advancing it after each day, until nothing is left to schedule.</summary>
    private async Task PlayAsync(Competition competition)
    {
        var matches = _db.Services.GetRequiredService<IMatchService>();
        for (var day = 0; day < 200; day++)
        {
            var unplayed = await _db.ReadAsync(async s =>
            {
                var fixtures = await s.Repo<Fixture, FixtureId>().ListAsync(f => f.CompetitionId == competition.Id && f.SeasonId == _season.Id);
                var ids = fixtures.Select(f => f.Id).ToList();
                var played = (await s.Repo<Match, MatchId>().ListAsync(m => ids.Contains(m.FixtureId))).Select(m => m.FixtureId).ToHashSet();
                return fixtures.Where(f => !played.Contains(f.Id)).ToList();
            });
            if (unplayed.Count == 0 && (await Service.AdvanceAsync(competition.Id, _season.Id)).FixturesCreated == 0)
            {
                return;
            }

            foreach (var fixture in unplayed.Where(f => f.Date == unplayed.Min(u => u.Date)))
            {
                await matches.PlayOrForfeitAsync(fixture.Id, seed: day);
            }

            await Service.AdvanceAsync(competition.Id, _season.Id);
        }

        throw new InvalidOperationException("The competition never finished.");
    }
}
