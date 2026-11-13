using System.Reflection;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Domain.Seasons;
using Ebm.Services.Careers;
using Ebm.Services.Clubs;
using Ebm.Services.Import;
using Ebm.Services.Players;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Import;

/// <summary>
/// Step 16 acceptance: the European player database (data/europe) imports for every country with stable IDs and
/// correct player-club relationships; duplicates are found across leagues; missing data is marked, never verified.
/// </summary>
public sealed class EuropePlayerDatabaseTests : IAsyncLifetime
{
    private static readonly string Folder = Path.Combine(
        typeof(EuropePlayerDatabaseTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "europe");

    private static readonly string[] DatasetFiles =
    [
        Path.Combine(Folder, "europe-2026-27.json"),
        Path.Combine(Folder, "players-europe-2026-27.csv"),
        Path.Combine(Folder, "contracts-europe-2026-27.csv"),
        Path.Combine(Folder, "playerHistory-europe-2026-27.csv"),
    ];

    private static readonly RosterIssueCode[] RegistrationErrors =
        [RosterIssueCode.TooFewPlayers, RosterIssueCode.TooManyPlayers, RosterIssueCode.TooManyForeignPlayers, RosterIssueCode.TooManyNonEuPlayers, RosterIssueCode.TooFewDomesticPlayers];

    private TestDatabase _db = null!;

    private IDataImportService Importer => _db.Services.GetRequiredService<IDataImportService>();

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Every_country_imports_with_correct_clubs_and_stable_ids()
    {
        var report = await Importer.ImportAsync(DatasetFiles, new ImportOptions());
        Assert.Empty(report.Problems);
        Assert.Equal(0, report.Rejected);
        Assert.True(report.Committed);
        Assert.DoesNotContain(report.Records, r => r.Messages.Any(m => m.StartsWith("Possible duplicate", StringComparison.Ordinal)));

        var (clubs, players, contracts, spells) = await LoadAsync();
        Assert.Equal(6146, players.Count);
        Assert.Equal(240, clubs.Count(c => c.DataStatus == DataStatus.Unverified));
        Assert.Equal(272, clubs.Count(c => c.DataStatus == DataStatus.Generated));
        var cantu = clubs.Single(c => c.Name == "Pallacanestro Cantù").Id;
        var real = contracts.Where(c => c.ClubId == cantu).Select(c => c.PlayerId!.Value).ToHashSet();
        Assert.All(players.Where(p => !real.Contains(p.Id)), p => Assert.Equal((DataStatus.Generated, DataStatus.Generated), (p.ProfileStatus, p.AttributesStatus)));
        Assert.All(contracts.Where(c => c.ClubId != cantu), c => Assert.Equal(DataStatus.Generated, c.TermsStatus));
        Assert.Contains(players, p => p.SecondNationalityId is not null);

        // The real Cantù squad (build/real-rosters): sourced profiles, estimated attributes and terms; Christon has no cited birth date.
        var squad = players.Where(p => real.Contains(p.Id)).ToList();
        Assert.Equal(14, squad.Count);
        Assert.All(squad, p => Assert.Equal(DataStatus.Estimated, p.AttributesStatus));
        Assert.All(squad, p => Assert.NotNull(p.Source));
        Assert.Equal(["Semaj Christon"], squad.Where(p => p.ProfileStatus == DataStatus.Unverified).Select(p => p.FullName));
        Assert.All(contracts.Where(c => c.ClubId == cantu), c => Assert.Equal(DataStatus.Estimated, c.TermsStatus));
        var today = contracts.Where(c => c.ClubId == cantu && c.IsRunningOn(new DateOnly(2026, 11, 13))).Select(c => squad.Single(p => p.Id == c.PlayerId).LastName).Order();
        Assert.Equal(["Brown", "Flagg", "Gravett", "Gražulis", "Green", "Moraschini", "Moretti", "Okeke", "Olisevičius", "Parravicini", "Udom", "Ventura"], today);

        // Player-club relationships: one contract per player, twelve per club at the season start, from every country.
        var season = (await _db.ReadAsync(s => s.Repo<Season, SeasonId>().ListAsync())).Single();
        Assert.Equal(players.Count, contracts.Select(c => c.PlayerId).Distinct().Count());
        Assert.All(contracts.Where(c => c.ClubId != cantu), c => Assert.True(c.IsRunningOn(season.StartDate) && c.IsRunningOn(new DateOnly(2027, 6, 30))));
        Assert.All(contracts.Where(c => c.IsRunningOn(season.StartDate)).GroupBy(c => c.ClubId), g => Assert.Equal(12, g.Count()));
        Assert.Equal(clubs.Count, contracts.Select(c => c.ClubId).Distinct().Count());
        Assert.Equal(18, clubs.Where(c => contracts.Any(k => k.ClubId == c.Id)).Select(c => c.CountryId).Distinct().Count());

        // History: closed spells at another club, before the current contract.
        var contractOf = contracts.ToDictionary(c => c.PlayerId!.Value);
        Assert.Equal(1504, spells.Count);
        Assert.All(spells, s =>
        {
            Assert.NotNull(s.EndDate);
            Assert.NotEqual(contractOf[s.PlayerId].ClubId, s.ClubId);
            Assert.True(s.EndDate < contractOf[s.PlayerId].StartDate);
        });

        // Registration limits (foreign, non-EU with second nationalities, domestic) hold for one club of every competition.
        var entries = await _db.ReadAsync(s => s.Repo<CompetitionParticipant, CompetitionParticipantId>().ListAsync());
        var service = _db.Services.GetRequiredService<IClubService>();
        foreach (var entry in entries.GroupBy(e => e.CompetitionId).Select(g => g.First()))
        {
            var roster = (await service.GetRosterAsync(entry.ClubId))!;
            Assert.Equal(12, roster.Players.Count);
            Assert.DoesNotContain(roster.Validation.Issues, i => i.Code == RosterIssueCode.NoContract);
            Assert.All(roster.Registrations, r => Assert.DoesNotContain(r.Validation.Errors, i => RegistrationErrors.Contains(i.Code)));
        }

        // Stable IDs: an explicit update of the same files changes no ID and adds nothing.
        var update = await Importer.ImportAsync(DatasetFiles, new ImportOptions(ImportMode.AddOrUpdate));
        Assert.True(update.Committed);
        Assert.Equal(0, update.Added);
        Assert.Equal(0, update.Rejected);
        var again = await LoadAsync();
        Assert.Equal(players.Select(p => p.Id.Value).Order(), again.Players.Select(p => p.Id.Value).Order());
        Assert.Equal(contracts.Select(c => c.Id.Value).Order(), again.Contracts.Select(c => c.Id.Value).Order());

        // Profiles show the history and an estimated market value.
        var withHistory = spells[0].PlayerId;
        var profile = (await _db.Services.GetRequiredService<IPlayerProfileService>().GetProfileAsync(withHistory))!;
        Assert.Single(profile.History!);
        Assert.True(profile.MarketValue > 0);
    }

    [Fact]
    public async Task Duplicates_across_leagues_and_missing_data_are_identified()
    {
        Assert.True((await Importer.ImportAsync([DatasetFiles[0]], new ImportOptions())).Committed);
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
    public async Task The_european_world_plays_as_a_career_without_forfeits()
    {
        Assert.True((await Importer.ImportAsync(DatasetFiles, new ImportOptions())).Committed);
        var careers = _db.Services.GetRequiredService<ICareerService>();
        var option = (await careers.GetClubOptionsAsync()).Single(o => o.Club.Name == "Olimpia Milano");
        await careers.StartAsync(option.Club.Id, "Europe");
        await careers.SetAssistantAsync(true);

        var report = await careers.ContinueAsync(maxDays: 20);

        var matches = await _db.ReadAsync(s => s.Repo<Match, MatchId>().ListAsync());
        Assert.True(report.MatchesPlayed >= 240, $"{report.MatchesPlayed} matches played");
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
