using System.Reflection;
using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Ebm.Services.Competitions;
using Ebm.Services.Import;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Import;

public sealed class ImportTests : IAsyncLifetime
{
    private const string Countries = "code,name\nITA,Italy\n";
    private static readonly string PlayerHeader =
        $"id,firstName,lastName,dateOfBirth,nationality,heightCm,weightKg,position,{string.Join(',', PlayerCatalog.Attributes.Select(PlayerCatalog.FieldName))}\n";

    private static readonly string DataFolder = typeof(ImportTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!;

    private static readonly string SampleFile = Path.Combine(DataFolder, "sample", "italy-sample.json");

    private TestDatabase _db = null!;

    private IDataImportService Importer => _db.Services.GetRequiredService<IDataImportService>();

    public async Task InitializeAsync() => _db = await TestDatabase.CreateAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Sample_dataset_imports_completely()
    {
        var report = await Importer.ImportAsync([SampleFile], new ImportOptions());

        Assert.Empty(report.Problems);
        Assert.Equal(0, report.Rejected);
        Assert.True(report.Committed);
        Assert.Equal(5 + 8 + 14 + 1 + 2 + 3 + 1 + 14 + 37 + 33, report.Added);
        Assert.Equal(37, await CountAsync<Player, PlayerId>()); // 4 free agents
        Assert.Equal(14, await CountAsync<Club, ClubId>());
        Assert.Equal(33, await CountAsync<Contract, ContractId>());
        Assert.True(File.Exists(report.LogFile));
        Assert.Contains("Added", await File.ReadAllTextAsync(report.LogFile!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csv_templates_import_as_one_dataset()
    {
        var files = Directory.GetFiles(Path.Combine(DataFolder, "templates"), "*.csv");

        var report = await Importer.ImportAsync(files, new ImportOptions());

        Assert.Empty(report.Problems);
        Assert.Equal(10, report.Added);
        Assert.True(report.Committed);
        var player = Assert.Single(await ListAsync<Player, PlayerId>());
        Assert.Equal(Position.SmallForward, player.Position);
        Assert.Equal(65, player.Attributes.CloseShot);
        Assert.Equal(205, player.WingspanCm);
        Assert.Equal(PositionSet.Of(Position.ShootingGuard), player.SecondaryPositions);
        Assert.Equal(PlayerTendencies.Suggest(player.Attributes).ToDictionary(), player.Tendencies.ToDictionary());
        var contract = Assert.Single(await ListAsync<Contract, ContractId>());
        Assert.Equal((PaymentFrequency.Monthly, 10_000L, (long?)1_500_000), (contract.PaymentFrequency, contract.SigningBonus, contract.ReleaseClause));
        Assert.Equal((new DateOnly(2029, 6, 30), (DateOnly?)null), (contract.ClubOptionUntil, contract.PlayerOptionUntil));
        Assert.Equal(PerformanceBonuses.Of((BonusCondition.ReachPlayoffs, 15_000), (BonusCondition.LeagueTitle, 30_000)), contract.PerformanceBonuses);
    }

    [Fact]
    public async Task Invalid_contract_terms_are_reported_per_field()
    {
        await Importer.ImportAsync([Path.Combine(DataFolder, "templates", "countries.csv"), Path.Combine(DataFolder, "templates", "clubs.csv"),
            Path.Combine(DataFolder, "templates", "arenas.csv"), Path.Combine(DataFolder, "templates", "players.csv")], new ImportOptions());
        var file = await WriteAsync("contracts.csv",
            "clubId,playerId,startDate,endDate,annualSalary,paymentFrequency,performanceBonuses,clubOptionUntil\n" +
            "0198a000-0000-7000-8000-000000000002,0198a000-0000-7000-8000-000000000006,2026-07-01,2028-06-30,150000,Weekly,Title:5,2028-06-30\n");

        var report = await Importer.ImportAsync([file], new ImportOptions());

        var messages = Assert.Single(report.Records).Messages;
        Assert.Contains(messages, m => m.Contains("paymentFrequency", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("performanceBonuses", StringComparison.Ordinal));
        Assert.False(report.Committed);
    }

    [Fact]
    public async Task Reimport_without_update_rejects_existing_records_and_writes_nothing()
    {
        await Importer.ImportAsync([SampleFile], new ImportOptions());

        var report = await Importer.ImportAsync([SampleFile], new ImportOptions());

        Assert.Equal(0, report.Added + report.Updated);
        Assert.All(report.Records, r => Assert.Equal(ImportOutcome.Rejected, r.Outcome));
        Assert.Contains("already exists", report.Records.First(r => r.Kind == ImportRecordKind.Player).Messages[0], StringComparison.Ordinal);
        Assert.False(report.Committed);
        Assert.Equal(37, await CountAsync<Player, PlayerId>());
    }

    [Fact]
    public async Task Explicit_update_changes_existing_records()
    {
        await Importer.ImportAsync([SampleFile], new ImportOptions());
        var player = (await ListAsync<Player, PlayerId>())[0];
        var file = await WriteAsync("players.csv", PlayerHeader +
            $"{player.Id},{player.FirstName},{player.LastName},{player.DateOfBirth:yyyy-MM-dd},ITA,{player.HeightCm},{player.WeightKg},C{R(99)}\n");

        var report = await Importer.ImportAsync([file], new ImportOptions(ImportMode.AddOrUpdate));

        Assert.Equal(1, report.Updated);
        Assert.True(report.Committed);
        var updated = await _db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(player.Id));
        Assert.Equal(Position.Center, updated!.Position);
        Assert.Equal(99, updated.Attributes.WorkRate);
        Assert.Equal(37, await CountAsync<Player, PlayerId>());
    }

    [Fact]
    public async Task Invalid_records_are_rejected_with_understandable_errors_and_valid_ones_are_kept()
    {
        var countries = await WriteAsync("countries.csv", Countries);
        var players = await WriteAsync("players.csv", PlayerHeader +
            $",Valid,Player,2000-01-01,ITA,200,95,SF{R()}\n" +
            $",Too,Tall,2000-01-01,ITA,300,95,SF{R()}\n" +
            $",Bad,Date,01/02/2000,ITA,200,95,SF{R()}\n" +
            $",No,Country,2000-01-01,XYZ,200,95,Wing{R()[..^2]}500\n" +
            ",Short,Row,2000-01-01\n");

        var report = await Importer.ImportAsync([countries, players], new ImportOptions());

        Assert.True(report.Committed);
        Assert.Equal(2, report.Added);
        var rejected = report.Records.Where(r => r.Outcome == ImportOutcome.Rejected).ToList();
        Assert.Equal(4, rejected.Count);
        Assert.Contains("Height (cm) must be between 150 and 250.", rejected[0].Messages);
        Assert.Contains(rejected[1].Messages, m => m.Contains("'dateOfBirth' must be a date in the form YYYY-MM-DD", StringComparison.Ordinal));
        Assert.Contains(rejected[2].Messages, m => m.Contains("unknown country code 'XYZ'", StringComparison.Ordinal));
        Assert.Contains(rejected[2].Messages, m => m.Contains("'position' must be one of", StringComparison.Ordinal));
        Assert.Contains("'workRate' must be between 1 and 100 (got '500').", rejected[2].Messages);
        Assert.Contains(rejected[3].Messages, m => m.Contains("header has 35 columns", StringComparison.Ordinal));
        Assert.Equal("players.csv:3", rejected[0].Location);
        Assert.Single(await ListAsync<Player, PlayerId>());
    }

    [Fact]
    public async Task Optional_player_fields_are_read_and_validated()
    {
        var countries = await WriteAsync("countries.csv", Countries);
        var header = PlayerHeader.TrimEnd('\n') + ",wingspanCm,secondaryPositions,potential,fitness,injuryStatus,injuryReturnDate,postUp\n";
        var players = await WriteAsync("players.csv", header +
            $",Full,Profile,2000-01-01,ITA,205,100,PF{R()},214,C|SF,88,77,Injured,2026-12-01,12\n" +
            $",Bad,Values,2000-01-01,ITA,205,100,PF{R()},250,PF;XX,88,77,Injured,,0\n");

        var report = await Importer.ImportAsync([countries, players], new ImportOptions());

        var player = Assert.Single(await ListAsync<Player, PlayerId>());
        Assert.Equal((214, 88, 77), (player.WingspanCm, player.Potential, player.Fitness));
        Assert.Equal([Position.SmallForward, Position.Center], player.SecondaryPositions.Positions);
        Assert.Equal((InjuryStatus.Injured, (DateOnly?)new DateOnly(2026, 12, 1)), (player.InjuryStatus, player.InjuryReturnDate));
        Assert.Equal(12, player.Tendencies.PostUp);
        Assert.Equal(50, player.Tendencies.Isolation); // not given: suggested from the attributes
        var messages = report.Records.Single(r => r.Outcome == ImportOutcome.Rejected).Messages;
        Assert.Contains(messages, m => m.Contains("'secondaryPositions' contains 'XX'", StringComparison.Ordinal));
        Assert.Contains("'postUp' must be between 1 and 100 (got '0').", messages);
    }

    [Fact]
    public async Task Optional_club_fields_are_read_and_validated()
    {
        var countries = await WriteAsync("countries.csv", Countries);
        var clubs = await WriteAsync("clubs.csv",
            "name,shortName,country,city,reputation,balance,wageBudget,boardExpectation\n" +
            "Full Club,FUL,ITA,Turin,72,-1500000,6000000,ReachPlayoffs\n" +
            "Plain Club,PLA,ITA,,,,,\n" +
            "Bad Club,BAD,ITA,,0,x,-5,Champions\n");

        var report = await Importer.ImportAsync([countries, clubs], new ImportOptions());

        var stored = (await ListAsync<Club, ClubId>()).ToDictionary(c => c.ShortName);
        Assert.Equal(2, stored.Count);
        var full = stored["FUL"];
        Assert.Equal(("Turin", 72, -1_500_000L, 6_000_000L, BoardExpectation.ReachPlayoffs), (full.City, full.Reputation, full.Balance, full.WageBudget, full.BoardExpectation));
        var plain = stored["PLA"];
        Assert.Equal(((string?)null, Club.DefaultReputation, 0L, Club.DefaultBoardExpectation), (plain.City, plain.Reputation, plain.WageBudget, plain.BoardExpectation));
        var messages = report.Records.Single(r => r.Outcome == ImportOutcome.Rejected).Messages;
        Assert.Contains("'reputation' must be between 1 and 100 (got '0').", messages);
        Assert.Contains(messages, m => m.Contains("'balance' must be a whole number", StringComparison.Ordinal));
        Assert.Contains(messages, m => m.Contains("'boardExpectation' must be one of", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Duplicate_players_and_clubs_are_detected_in_the_database_and_within_the_import()
    {
        await Importer.ImportAsync([SampleFile], new ImportOptions());
        var existing = (await ListAsync<Player, PlayerId>())[0];
        var existingClub = (await ListAsync<Club, ClubId>())[0];
        var players = await WriteAsync("players.csv", PlayerHeader +
            $",{existing.FirstName.ToUpperInvariant()},{existing.LastName},{existing.DateOfBirth:yyyy-MM-dd},ITA,200,95,SF{R()}\n" +
            $",New,Twin,2001-01-01,ITA,200,95,SF{R()}\n" +
            $",New,Twin,2001-01-01,ITA,201,96,PF{R()}\n");
        var clubs = await WriteAsync("clubs.csv", $"name,shortName,country\n{existingClub.Name},XXX,ITA\n");

        var report = await Importer.ImportAsync([players, clubs], new ImportOptions(ImportMode.AddOrUpdate));

        var results = report.Records.ToDictionary(r => r.Location);
        Assert.Contains("in the database", results["clubs.csv:2"].Messages[0], StringComparison.Ordinal);
        Assert.Contains("in the database", results["players.csv:2"].Messages[0], StringComparison.Ordinal);
        Assert.Contains(existing.Id.ToString(), results["players.csv:2"].Messages[0], StringComparison.Ordinal);
        Assert.Equal(ImportOutcome.Added, results["players.csv:3"].Outcome);
        Assert.Contains("earlier in this import", results["players.csv:4"].Messages[0], StringComparison.Ordinal);
        Assert.Equal(38, await CountAsync<Player, PlayerId>());
    }

    [Fact]
    public async Task Records_without_id_get_a_new_unique_id_and_ids_cannot_repeat()
    {
        var id = Guid.CreateVersion7();
        var file = await WriteAsync("countries.csv", $"id,code,name\n,ITA,Italy\n{id},ESP,Spain\n{id},FRA,France\n");

        var report = await Importer.ImportAsync([file], new ImportOptions());

        Assert.StartsWith("New ID assigned:", report.Records[0].Messages[0], StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, report.Records[0].Id);
        Assert.Equal(ImportOutcome.Added, report.Records[1].Outcome);
        Assert.Equal("The record appears more than once in this import.", report.Records[2].Messages[0]);
    }

    [Fact]
    public async Task Records_depending_on_a_rejected_record_are_rejected_too()
    {
        var playerId = Guid.CreateVersion7();
        var clubId = Guid.CreateVersion7();
        var file = await WriteAsync("data.json", $$"""
            {
              "version": 1,
              "countries": [ { "code": "ITA", "name": "Italy" } ],
              "clubs": [ { "id": "{{clubId}}", "name": "Club", "shortName": "CLB", "country": "ITA" } ],
              "players": [ { "id": "{{playerId}}", "firstName": "", "lastName": "Missing", "dateOfBirth": "2000-01-01", "nationality": "ITA",
                             "heightCm": 200, "weightKg": 95, "position": "C", "shooting": 50, "insideScoring": 50, "passing": 50,
                             "ballHandling": 50, "rebounding": 50, "defense": 50, "athleticism": 50, "basketballIq": 50 } ],
              "contracts": [ { "clubId": "{{clubId}}", "playerId": "{{playerId}}", "startDate": "2026-07-01", "endDate": "2027-06-30", "annualSalary": 1000 } ]
            }
            """);

        var report = await Importer.ImportAsync([file], new ImportOptions());

        var player = report.Records.Single(r => r.Kind == ImportRecordKind.Player);
        Assert.Contains("'firstName' is required.", player.Messages);
        var contract = report.Records.Single(r => r.Kind == ImportRecordKind.Contract);
        Assert.Equal(ImportOutcome.Rejected, contract.Outcome);
        Assert.Contains(contract.Messages, m => m.Contains($"refers to player {playerId}", StringComparison.Ordinal));
        Assert.Equal(2, report.Added);
    }

    [Fact]
    public async Task Dry_run_and_all_or_nothing_write_nothing()
    {
        var dryRun = await Importer.ImportAsync([SampleFile], new ImportOptions(DryRun: true));
        var file = await WriteAsync("countries.csv", "code,name\nITA,Italy\nXX,Broken\n");
        var allOrNothing = await Importer.ImportAsync([file], new ImportOptions(AllOrNothing: true));

        Assert.Equal(118, dryRun.Added);
        Assert.False(dryRun.Committed);
        Assert.Equal(1, allOrNothing.Added);
        Assert.False(allOrNothing.Committed);
        Assert.Equal(0, await CountAsync<Country, CountryId>());
    }

    [Fact]
    public async Task Contract_club_cannot_be_changed_by_an_update()
    {
        await Importer.ImportAsync([SampleFile], new ImportOptions());
        var contract = (await ListAsync<Contract, ContractId>())[0];
        var otherClub = (await ListAsync<Club, ClubId>()).First(c => c.Id != contract.ClubId);
        var file = await WriteAsync("contracts.csv",
            $"id,clubId,playerId,startDate,endDate,annualSalary\n{contract.Id},{otherClub.Id},{contract.PlayerId},2026-07-01,2029-06-30,1\n");

        var report = await Importer.ImportAsync([file], new ImportOptions(ImportMode.AddOrUpdate));

        Assert.Contains("cannot be changed", Assert.Single(report.Records).Messages[0], StringComparison.Ordinal);
        Assert.False(report.Committed);
    }

    [Fact]
    public async Task Malformed_and_unrecognised_files_are_reported()
    {
        var csv = await WriteAsync("players.csv", "id,firstName\n\"unterminated\n");
        var json = await WriteAsync("broken.json", "{ \"countries\": [ ");
        var unknown = await WriteAsync("roster.csv", "a,b\n");
        var columns = await WriteAsync("countries-extra.csv", "code,name,flag\nITA,Italy,green\n");

        var report = await Importer.ImportAsync([csv, json, unknown, columns, Path.Combine(_db.TempFolder, "missing.csv")], new ImportOptions());

        Assert.Contains(report.Problems, p => p.StartsWith("players.csv: malformed CSV", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.StartsWith("broken.json: malformed JSON", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.StartsWith("roster.csv: cannot tell the record type", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.Contains("unknown field(s) flag ignored", StringComparison.Ordinal));
        Assert.Contains(report.Problems, p => p.StartsWith("missing.csv: the file cannot be read", StringComparison.Ordinal));
        Assert.Equal(1, report.Added);
    }

    [Fact]
    public async Task Competition_entries_follow_the_registration_rules()
    {
        await Importer.ImportAsync([SampleFile], new ImportOptions());
        var competitions = await ListAsync<Competition, CompetitionId>();
        var serieA = competitions.Single(c => c.Name == "Serie A").Id.Value;
        var serieA2 = competitions.Single(c => c.Name == "Serie A2").Id.Value;
        var season = (await ListAsync<Season, SeasonId>()).Single().Id.Value;
        var clubs = await ListAsync<Club, ClubId>();
        var milano = clubs.Single(c => c.Name == "Aurora Milano").Id.Value;
        await _db.Services.GetRequiredService<ICompetitionService>().GenerateScheduleAsync(CompetitionId.From(serieA), SeasonId.From(season));
        var seasons = await WriteAsync("seasons.csv", "id,name,startDate,endDate\n0198a000-0000-7000-8000-0000000000aa,2027-28,2027-10-01,2028-06-30\n");
        var participants = await WriteAsync("participants.csv",
            "competitionId,seasonId,clubId\n" +
            $"{serieA2},0198a000-0000-7000-8000-0000000000aa,{milano}\n" +
            $"{serieA},0198a000-0000-7000-8000-0000000000aa,{milano}\n" +
            $"{serieA},{season},{clubs.Single(c => c.Name == "Lince Verona").Id.Value}\n" +
            $"{serieA2},{Guid.NewGuid()},{milano}\n");

        var report = await Importer.ImportAsync([seasons, participants], new ImportOptions());

        Assert.Equal(2, report.Added);
        var rejected = report.Records.Where(r => r.Outcome == ImportOutcome.Rejected).ToList();
        Assert.Equal(3, rejected.Count);
        Assert.Contains("another league", rejected[0].Messages[0], StringComparison.Ordinal);
        Assert.Contains("closed", rejected[1].Messages[0], StringComparison.Ordinal);
        Assert.Contains("seasonId", rejected[2].Messages[0], StringComparison.Ordinal);
    }

    /// <summary>Every attribute column of a player row at <paramref name="value"/>, each preceded by a comma.</summary>
    private static string R(int value = 50) => string.Concat(PlayerCatalog.Attributes.Select(_ => $",{value}"));

    private async Task<string> WriteAsync(string name, string content)
    {
        var path = Path.Combine(_db.TempFolder, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private Task<IReadOnlyList<TEntity>> ListAsync<TEntity, TId>()
        where TEntity : Ebm.Domain.Common.Entity<TId>
        where TId : struct, Ebm.Domain.Common.IEntityId<TId> =>
        _db.ReadAsync(s => s.Repo<TEntity, TId>().ListAsync());

    private async Task<int> CountAsync<TEntity, TId>()
        where TEntity : Ebm.Domain.Common.Entity<TId>
        where TId : struct, Ebm.Domain.Common.IEntityId<TId> =>
        (await ListAsync<TEntity, TId>()).Count;
}
