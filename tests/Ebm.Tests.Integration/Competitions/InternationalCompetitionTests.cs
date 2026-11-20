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
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Competitions;

/// <summary>
/// Step 18 acceptance: clubs play their league, a domestic cup and an international competition in one season;
/// international fixtures are generated on free days, calendar conflicts are detected, results coexist, winners
/// are recorded, rewards are paid and the next season qualifies from the finished one.
/// </summary>
public sealed class InternationalCompetitionTests : IAsyncLifetime
{
    // Tier 1: Italy's top two, then the best reputation; a league phase of four, a neutral final.
    private const string EuroA = """
        {"pointsForWin":2,"pointsForLoss":1,"registration":{"minTeams":4,"maxTeams":4},
         "cup":{"draw":"Bracket","startOffsetDays":0,"daysBetweenRounds":7,
          "entry":{"tier":1,"slots":[{"type":"Holder"},{"type":"League","countries":["ITA"],"clubs":2},{"type":"Reputation","clubs":0}]},
          "groupStage":{"groups":1,"legs":2,"advance":2,"daysBetweenRounds":7},
          "rounds":[{"name":"Final","neutral":true,"daysAfter":3}],
          "rewards":{"entry":100000,"win":10000,"knockout":50000,"winner":500000,"runnerUp":200000,"reputationKnockout":1,"reputationWinner":3,"reputationRunnerUp":2}}}
        """;

    // Tier 2: the next four by reputation; two-legged semi-finals and a final.
    private const string EuroB = """
        {"registration":{"minTeams":4,"maxTeams":4},
         "cup":{"draw":"Bracket","startOffsetDays":30,"daysBetweenRounds":14,
          "entry":{"tier":2,"slots":[{"type":"Holder"},{"type":"Reputation","clubs":0}]},
          "rounds":[{"name":"Semi-finals","legs":2},{"name":"Final","neutral":true}],
          "rewards":{"entry":20000,"winner":100000,"reputationWinner":1}}}
        """;

    private const string FinalFour = """
        {"cup":{"draw":"Bracket","startOffsetDays":20,"daysBetweenRounds":2,"qualification":{"level":1,"positions":4,"afterRound":7},
         "rounds":[{"name":"Semi-finals"},{"name":"Final"}]}}
        """;

    private static readonly string SampleFolder = Path.Combine(
        typeof(InternationalCompetitionTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "sample");

    private TestDatabase _db = null!;
    private Competition _domesticCup = null!;
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
        _domesticCup = competitions.Single(c => c.Type == CompetitionType.Cup);
        _serieA = competitions.Single(c => c.Name == "Serie A");
        _season = (await _db.ReadAsync(s => s.Repo<Season, SeasonId>().ListAsync())).Single();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Clubs_play_league_domestic_cup_and_international_competitions_in_one_season_and_requalify()
    {
        var editor = _db.Services.GetRequiredService<IDatabaseEditor>();
        await editor.SaveCompetitionAsync(new CompetitionDraft(_domesticCup.Id, _domesticCup.Name, _domesticCup.ShortName, CompetitionType.Cup, _domesticCup.CountryId, null, FinalFour));
        var euroA = await editor.SaveCompetitionAsync(new CompetitionDraft(null, "Euro A Cup", "EA", CompetitionType.Cup, null, null, EuroA));
        var euroB = await editor.SaveCompetitionAsync(new CompetitionDraft(null, "Euro B Cup", "EB", CompetitionType.Cup, null, null, EuroB));
        var serieA = await EntriesAsync(_serieA.Id, _season.Id);
        var careers = _db.Services.GetRequiredService<ICareerService>();

        await careers.StartAsync(serieA[0], "Euro Manager");

        // Qualification: the eight Serie A clubs split between the tiers, best reputations in the top tier.
        var a = await EntriesAsync(euroA, _season.Id);
        var b = await EntriesAsync(euroB, _season.Id);
        Assert.Equal((4, 4), (a.Count, b.Count));
        Assert.Empty(a.Intersect(b));
        Assert.True(serieA.ToHashSet().SetEquals(a.Concat(b)));
        var reputation = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync())).ToDictionary(c => c.Id, c => c.Reputation);
        Assert.True(a.Min(c => reputation[c]) >= b.Max(c => reputation[c]));
        Assert.Equal(0, await Service.QualifyAsync(_season.Id));
        await Assert.ThrowsAsync<DomainValidationException>(() => Service.RegisterClubsAsync(euroB, _season.Id, [a[0]]));

        await careers.SetAssistantAsync(true);
        CalendarReport report;
        do
        {
            report = await careers.ContinueAsync();
        }
        while (report.StoppedBecause != CalendarStop.SeasonComplete);

        // Fixtures were generated for every competition, and no club ever played twice on one day.
        var fixtures = await _db.ReadAsync(s => s.Repo<Fixture, FixtureId>().ListAsync(f => f.SeasonId == _season.Id));
        Assert.Contains(fixtures, f => f.CompetitionId == euroA && f.Stage == FixtureStage.CupGroup);
        Assert.Contains(fixtures, f => f.CompetitionId == euroB && f.Stage == FixtureStage.Cup && f.GameNumber == 2);
        Assert.Empty(await Service.GetCalendarConflictsAsync(_season.Id));

        // Domestic and international results coexist: one club has results in all three competitions.
        var played = (await _db.ReadAsync(s => s.Repo<Match, MatchId>().ListAsync())).Select(m => m.FixtureId).ToHashSet();
        Assert.All(fixtures, f => Assert.Contains(f.Id, played));
        var cupClubs = await EntriesAsync(_domesticCup.Id, _season.Id);
        Assert.Contains(cupClubs, club => new[] { _serieA.Id, _domesticCup.Id, euroA }.All(c => fixtures.Any(f => f.CompetitionId == c && (f.HomeClubId == club || f.AwayClubId == club)))
            || new[] { _serieA.Id, _domesticCup.Id, euroB }.All(c => fixtures.Any(f => f.CompetitionId == c && (f.HomeClubId == club || f.AwayClubId == club))));
        var state = (await careers.LoadAsync())!;
        Assert.All(new[] { _serieA.Id, _domesticCup.Id, euroA, euroB }, id => Assert.Contains(state.Competitions, c => c.Competition.Id == id && c.Phase == CompetitionPhase.Completed));

        // Winners are recorded, and the rewards paid once.
        var winnerA = (await Service.GetOutcomeAsync(euroA, _season.Id)).Champion;
        var cupA = await Service.GetCupAsync(euroA, _season.Id);
        Assert.Equal(winnerA, Assert.Single(await Service.GetHistoryAsync(euroA)).Champion);
        Assert.Contains((await careers.GetSeasonReviewAsync()).Competitions, c => c.Competition.Id == euroA && c.Champion.Length > 0);
        var paid = await _db.ReadAsync(s => s.Repo<CompetitionParticipant, CompetitionParticipantId>().ListAsync(p => p.CompetitionId == euroA && p.SeasonId == _season.Id));
        Assert.All(paid, p => Assert.True(p.PrizeMoney >= 100_000));
        var winnerEntry = paid.Single(p => p.ClubId == winnerA);
        Assert.True(winnerEntry.PrizeMoney >= 100_000 + 50_000 + 500_000 + 10_000);
        Assert.Equal(1 + 3, winnerEntry.ReputationGain);
        Assert.Equal(1 + 2, paid.Single(p => p.ClubId == cupA.RunnerUp).ReputationGain);

        // Step 31: each reward is a stored reputation change linked to the competition (docs/NEWS.md §4).
        var rewards = await _db.ReadAsync(s => s.Repo<Ebm.Domain.News.ReputationChange, Ebm.Domain.News.ReputationChangeId>()
            .ListAsync(c => c.Cause == Ebm.Domain.News.ReputationCause.CompetitionReward && c.SeasonId == _season.Id));
        var winnerReward = Assert.Single(rewards, c => c.ClubId == winnerA && c.Key.StartsWith($"reputation:{euroA}:", StringComparison.Ordinal));
        Assert.Equal(1 + 3, winnerReward.Points);
        var champion = (await Service.GetOutcomeAsync(_serieA.Id, _season.Id)).Champion;

        // Next season: the holder and the Serie A champion qualify from the finished season.
        var next = await careers.StartNextSeasonAsync();
        var nextA = await EntriesAsync(euroA, next.Season.Id);
        var nextB = await EntriesAsync(euroB, next.Season.Id);
        Assert.Equal((4, 4), (nextA.Count, nextB.Count));
        Assert.Contains(winnerA, nextA);
        Assert.Contains(champion, nextA);
        Assert.Empty(nextA.Intersect(nextB));
    }

    [Fact]
    public async Task Calendar_conflicts_between_competitions_are_detected()
    {
        await _db.Services.GetRequiredService<IDatabaseEditor>().SaveCompetitionAsync(new CompetitionDraft(
            _domesticCup.Id, _domesticCup.Name, _domesticCup.ShortName, CompetitionType.Cup, _domesticCup.CountryId, null,
            """{"registration":{"maxTeams":8},"cup":{"startOffsetDays":0,"rounds":[{"name":"QF"},{"name":"SF"},{"name":"F"}]}}"""));
        var clubs = await EntriesAsync(_serieA.Id, _season.Id);
        await Service.RegisterClubsAsync(_domesticCup.Id, _season.Id, clubs);
        await Service.GenerateScheduleAsync(_domesticCup.Id, _season.Id);
        Assert.Empty(await Service.GetCalendarConflictsAsync(_season.Id));

        // The league opens on the day of the cup quarter-finals: every club has two games that day.
        await Service.GenerateScheduleAsync(_serieA.Id, _season.Id, _season.StartDate);

        var conflicts = await Service.GetCalendarConflictsAsync(_season.Id);
        Assert.Equal(clubs.OrderBy(c => c.Value).ToList(), conflicts.Select(c => c.ClubId).OrderBy(c => c.Value).ToList());
        Assert.All(conflicts, c => Assert.Equal((_season.StartDate, 2), (c.Date, c.Fixtures.Count)));
    }

    private async Task<List<ClubId>> EntriesAsync(CompetitionId competitionId, SeasonId seasonId) =>
        [.. (await _db.ReadAsync(s => s.Repo<CompetitionParticipant, CompetitionParticipantId>().ListAsync(p => p.CompetitionId == competitionId && p.SeasonId == seasonId)))
            .Select(p => p.ClubId)];
}
