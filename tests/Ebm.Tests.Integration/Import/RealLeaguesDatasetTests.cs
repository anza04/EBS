using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Services.Careers;
using Ebm.Services.Import;
using Ebm.Services.Players;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Import;

/// <summary>
/// The real-leagues dataset (data/real-leagues, build/generate-real-leagues-dataset.py) imports on its own: eight real
/// divisions with 138 real clubs, the real players with contracts and the rest as free agents, with stable IDs.
/// Duplicates are found across leagues' files and missing data is marked, never verified (Step 16).
/// </summary>
[Trait("Category", "Slow")] // full datasets or multi-season simulation: run before releases, not on every change
public sealed class RealLeaguesDatasetTests : IAsyncLifetime
{
    private TestDatabase _db = null!;

    private IDataImportService Importer => _db.Services.GetRequiredService<IDataImportService>();

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Real_leagues_import_on_their_own_with_stable_ids()
    {
        var report = await Importer.ImportAsync(RealLeagues.Files, new ImportOptions());
        Assert.Empty(report.Problems);
        Assert.Empty(report.Records.Where(r => r.Outcome == ImportOutcome.Rejected).Select(r => $"{r.Location} {r.Label}: {string.Join(" | ", r.Messages)}"));
        Assert.True(report.Committed);

        var (clubs, players, contracts, spells) = await LoadAsync();
        var competitions = await _db.ReadAsync(s => s.Repo<Competition, CompetitionId>().ListAsync());
        Assert.Equal(138, clubs.Count);
        Assert.Equal(8, competitions.Count(c => c.Type == CompetitionType.League));
        Assert.Equal(6 + 4, competitions.Count(c => c.Type == CompetitionType.Cup)); // domestic cups (Step 17), international (Step 18)
        Assert.Equal(File.ReadLines(RealLeagues.Files[1]).Count() - 1, players.Count);
        var contracted = contracts.Select(c => c.PlayerId).ToHashSet();
        Assert.InRange(players.Count(p => !contracted.Contains(p.Id)), 400, 700); // free agents
        Assert.All(clubs, c => Assert.InRange(contracts.Count(k => k.ClubId == c.Id), 12, 30));
        Assert.Contains(players, p => p.SecondNationalityId is not null);

        // Real records cite a source; only the squad top-ups are Generated, and they have no source.
        Assert.All(players.Where(p => p.ProfileStatus != DataStatus.Generated), p => Assert.NotNull(p.Source));
        Assert.All(players.Where(p => p.ProfileStatus == DataStatus.Generated), p => Assert.Null(p.Source));
        Assert.All(players.Where(p => p.ProfileStatus != DataStatus.Generated), p => Assert.Equal(DataStatus.Estimated, p.AttributesStatus));

        // History: closed spells at another club, before the current contract.
        var contractOf = contracts.GroupBy(c => c.PlayerId!.Value).ToDictionary(g => g.Key, g => g.MinBy(c => c.StartDate)!);
        Assert.NotEmpty(spells);
        Assert.All(spells, s =>
        {
            Assert.NotNull(s.EndDate);
            if (contractOf.TryGetValue(s.PlayerId, out var contract))
            {
                Assert.True(s.EndDate < contract.StartDate);
            }
        });

        // Stable IDs: an explicit update of the same files changes no ID and adds nothing.
        var update = await Importer.ImportAsync(RealLeagues.Files, new ImportOptions(ImportMode.AddOrUpdate));
        Assert.True(update.Committed);
        Assert.Equal(0, update.Added);
        Assert.Equal(0, update.Rejected);
        var again = await LoadAsync();
        Assert.Equal(players.Select(p => p.Id.Value).Order(), again.Players.Select(p => p.Id.Value).Order());
        Assert.Equal(contracts.Select(c => c.Id.Value).Order(), again.Contracts.Select(c => c.Id.Value).Order());

        // Profiles show the history and an estimated market value.
        var profile = (await _db.Services.GetRequiredService<IPlayerProfileService>().GetProfileAsync(spells[0].PlayerId))!;
        Assert.NotEmpty(profile.History!);
        Assert.True(profile.MarketValue > 0);
    }

    [Fact]
    public async Task Duplicates_across_leagues_and_missing_data_are_identified()
    {
        Assert.True((await Importer.ImportAsync([RealLeagues.World], new ImportOptions())).Committed);
        var clubs = await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync());
        var milano = clubs.Single(c => c.Name == "Olimpia Milano");
        var madrid = clubs.Single(c => c.Name == "Real Madrid");

        // The same player listed by two leagues' files: accents differ, the IDs differ.
        var first = await ImportCsvAsync("players-ita.csv",
            "id,firstName,lastName,dateOfBirth,nationality,secondNationality,heightCm,weightKg,position,reputation,closeShot,threePoint,passing,source,dataStatus\n" +
            "11111111-1111-1111-1111-111111111111,Nikola,Đorđević,1999-03-04,SRB,ITA,200,98,SF,60,70,72,65,Club website,Verified\n");
        var record = Assert.Single(first.Records);
        Assert.Equal(ImportOutcome.Added, record.Outcome);
        Assert.Contains(record.Messages, m => m.Contains("24 of 27 attributes missing", StringComparison.Ordinal));
        var second = await ImportCsvAsync("players-esp.csv",
            "id,firstName,lastName,dateOfBirth,nationality,heightCm,weightKg,position\n" +
            "22222222-2222-2222-2222-222222222222,Nikola,Djordjevic,1999-03-04,SRB,200,98,SF\n" +
            "33333333-3333-3333-3333-333333333333,Nikola,Djordjević,1999-04-03,SRB,200,98,SF\n");
        Assert.Contains("Duplicate of 'Nikola Đorđević'", second.Records[0].Messages[0], StringComparison.Ordinal);
        Assert.Equal(ImportOutcome.Added, second.Records[1].Outcome);
        Assert.Contains(second.Records[1].Messages, m => m.StartsWith("Possible duplicate of 'Nikola Đorđević'", StringComparison.Ordinal));

        var player = await _db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(PlayerId.From(Guid.Parse("11111111-1111-1111-1111-111111111111"))));
        Assert.Equal((DataStatus.Verified, DataStatus.Estimated, "Club website"), (player!.ProfileStatus, player.AttributesStatus, player.Source));
        Assert.NotNull(player.SecondNationalityId);
        var unknown = await _db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(PlayerId.From(Guid.Parse("33333333-3333-3333-3333-333333333333"))));
        Assert.Equal((DataStatus.Unverified, DataStatus.Estimated), (unknown!.ProfileStatus, unknown.AttributesStatus));

        // Contracts: a missing salary is estimated (never verified); a verified profile needs a source.
        var contracts = await ImportCsvAsync("contracts.csv",
            "clubId,playerId,startDate,endDate,annualSalary,dataStatus,source\n" +
            $"{milano.Id.Value},11111111-1111-1111-1111-111111111111,2026-07-01,2028-06-30,,,\n" +
            $"{madrid.Id.Value},33333333-3333-3333-3333-333333333333,2026-07-01,2028-06-30,,Verified,Press release\n");
        Assert.Equal(ImportOutcome.Added, contracts.Records[0].Outcome);
        Assert.Contains(contracts.Records[0].Messages, m => m.StartsWith("Salary unknown", StringComparison.Ordinal));
        Assert.Equal(ImportOutcome.Rejected, contracts.Records[1].Outcome);
        var contract = (await _db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync())).Single();
        Assert.Equal(DataStatus.Estimated, contract.TermsStatus);
        Assert.True(contract.AnnualSalary > 0);

        var unsourced = await ImportCsvAsync("players-x.csv",
            "firstName,lastName,dateOfBirth,nationality,heightCm,weightKg,position,dataStatus\nAna,Test,2000-01-01,ITA,180,80,PG,Verified\n");
        Assert.Contains("can only be 'Verified' with a source", Assert.Single(unsourced.Records).Messages[0], StringComparison.Ordinal);

        // History: overlapping spells of one player are refused.
        var history = await ImportCsvAsync("playerHistory.csv",
            "playerId,clubId,startDate,endDate\n" +
            $"11111111-1111-1111-1111-111111111111,{madrid.Id.Value},2022-07-01,2024-06-30\n" +
            $"11111111-1111-1111-1111-111111111111,{milano.Id.Value},2024-01-01,2025-06-30\n");
        Assert.Equal([ImportOutcome.Added, ImportOutcome.Rejected], history.Records.Select(r => r.Outcome));
        Assert.Contains("overlaps", history.Records[1].Messages[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_real_world_plays_as_a_career_without_forfeits()
    {
        await RealLeagues.ImportAsync(_db);
        var careers = _db.Services.GetRequiredService<ICareerService>();
        var option = (await careers.GetClubOptionsAsync()).Single(o => o.Club.Name == "Olimpia Milano");
        await careers.StartAsync(option.Club.Id, "Real leagues");
        await careers.SetAssistantAsync(true);

        var report = await careers.ContinueAsync(maxDays: 20);

        // A week of preseason, then the leagues start a week in: at least the first round of all eight (69 games).
        var matches = await _db.ReadAsync(s => s.Repo<Match, MatchId>().ListAsync());
        Assert.True(report.MatchesPlayed >= 69, $"{report.MatchesPlayed} matches played");
        Assert.DoesNotContain(matches, m => m.IsForfeit);
    }

    private async Task<ImportReport> ImportCsvAsync(string fileName, string content)
    {
        var path = Path.Combine(_db.TempFolder, fileName);
        await File.WriteAllTextAsync(path, content);
        return await Importer.ImportAsync([path], new ImportOptions());
    }

    private async Task<(List<Club> Clubs, List<Player> Players, List<Contract> Contracts, List<PlayerClubHistoryEntry> Spells)> LoadAsync()
    {
        var clubs = await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync());
        var players = await _db.ReadAsync(s => s.Repo<Player, PlayerId>().ListAsync());
        var contracts = await _db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync());
        var spells = await _db.ReadAsync(s => s.Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>().ListAsync());
        return ([.. clubs], [.. players], [.. contracts], [.. spells]);
    }
}
