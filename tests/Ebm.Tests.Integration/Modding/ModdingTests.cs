using System.IO.Compression;
using System.Reflection;
using System.Text;
using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Services.Careers;
using Ebm.Services.Import;
using Ebm.Services.Modding;
using Ebm.Services.Saves;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Modding;

/// <summary>Step 33: database export, whole-database validation and community packages (docs/MODDING.md).</summary>
public sealed class ModdingTests : IAsyncLifetime
{
    private static readonly string SampleFolder = Path.Combine(
        typeof(ModdingTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!,
        "sample");

    private TestDatabase _db = null!;

    private IModPackageService Packages => _db.Services.GetRequiredService<IModPackageService>();

    private ICareerService Careers => _db.Services.GetRequiredService<ICareerService>();

    private ISaveGameService Saves => _db.Services.GetRequiredService<ISaveGameService>();

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
        var report = await _db.Services.GetRequiredService<IDataImportService>()
            .ImportAsync([Path.Combine(SampleFolder, "italy-sample.json"), Path.Combine(SampleFolder, "italy-squads.json")], new ImportOptions());
        Assert.True(report.Committed);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task An_exported_database_imports_into_a_new_game_unchanged()
    {
        var first = Path.Combine(_db.TempFolder, "export-1.json");
        var summary = await _db.Services.GetRequiredService<IDatabaseExportService>().ExportAsync(first);
        Assert.True(summary.Counts.Single(c => c.Section == "players").Count > 100);

        await using var other = await TestDatabase.CreateAsync();
        var report = await other.Services.GetRequiredService<IDataImportService>().ImportAsync([first], new ImportOptions(AllOrNothing: true));
        Assert.True(report.Committed, string.Join(Environment.NewLine, report.Records.Where(r => r.Outcome == ImportOutcome.Rejected).SelectMany(r => r.Messages)));
        Assert.Equal(summary.Total, report.Added);

        // Exporting the imported copy gives the same file: nothing is lost or changed on the way.
        var second = Path.Combine(other.TempFolder, "export-2.json");
        await other.Services.GetRequiredService<IDatabaseExportService>().ExportAsync(second);
        Assert.Equal(await File.ReadAllTextAsync(first), await File.ReadAllTextAsync(second));
    }

    [Fact]
    public async Task Packages_are_validated_and_never_leave_the_current_game_broken()
    {
        var aurora = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync(c => c.Name == "Aurora Milano"))).Single();
        var other = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync(c => c.Name != "Aurora Milano"))).First();
        await Careers.StartAsync(aurora.Id, "Test Manager");
        await Careers.AdvanceDayAsync();
        var before = (await Careers.LoadAsync())!;
        var contracts = await CountAsync<Contract, ContractId>();
        var baseline = await _db.Services.GetRequiredService<IDatabaseValidator>().ValidateAsync();
        Assert.DoesNotContain(baseline.Issues, i => i.Area == "Contracts");

        // Invalid reference: rejected by validation, and installing writes nothing (no restore point is kept).
        var badReference = Package("bad-reference", $$"""
            { "contracts": [ { "clubId": "{{aurora.Id.Value}}", "playerId": "{{Guid.NewGuid()}}", "startDate": "2026-10-01", "endDate": "2028-06-30", "annualSalary": 100000 } ] }
            """);
        var check = await Packages.ValidateAsync(badReference);
        Assert.False(check.CanInstall);
        Assert.Equal(1, check.Import!.Rejected);
        Assert.Contains(check.Import.Records.Single().Messages, m => m.Contains("playerId", StringComparison.Ordinal));
        var failed = await Packages.InstallAsync(badReference, InstallTarget.CurrentGame);
        Assert.False(failed.Installed);
        Assert.Null(failed.RestorePoint);
        Assert.DoesNotContain(await Saves.ListAsync(), s => s.Name.StartsWith("Before", StringComparison.Ordinal));

        // Valid records that break the game (a second club for a player under contract): installed, then undone.
        var player = (await _db.ReadAsync(s => s.Repo<Contract, ContractId>().ListAsync(c => c.ClubId == aurora.Id && c.Status == ContractStatus.Active && c.PlayerId != null))).First().PlayerId!.Value;
        var overlap = Package("overlap", $$"""
            { "contracts": [ { "clubId": "{{other.Id.Value}}", "playerId": "{{player.Value}}", "startDate": "{{before.Today:yyyy-MM-dd}}", "endDate": "2028-06-30", "annualSalary": 100000 } ] }
            """);
        Assert.True((await Packages.ValidateAsync(overlap)).CanInstall);
        var undone = await Packages.InstallAsync(overlap, InstallTarget.CurrentGame);
        Assert.False(undone.Installed);
        Assert.Contains("undone", undone.Message, StringComparison.Ordinal);
        Assert.Equal(contracts, await CountAsync<Contract, ContractId>());
        await AssertSameCareerAsync(before);

        // A custom competition is added; the career goes on unchanged and the previous game is kept as a save.
        var competitions = await CountAsync<Competition, CompetitionId>();
        var cup = Package("custom-cup", """
            { "competitions": [ { "name": "Coppa dei Modder", "shortName": "Modder", "type": "Cup", "country": "ITA" } ] }
            """);
        var installed = await Packages.InstallAsync(cup, InstallTarget.CurrentGame);
        Assert.True(installed.Installed, installed.Message);
        Assert.Equal(competitions + 1, await CountAsync<Competition, CompetitionId>());
        Assert.Contains(await Saves.ListAsync(), s => s.Name == installed.RestorePoint);
        await AssertSameCareerAsync(before);
    }

    [Fact]
    public async Task A_package_made_from_the_game_starts_a_new_game_and_saved_careers_are_unaffected()
    {
        var aurora = (await _db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync(c => c.Name == "Aurora Milano"))).Single();
        await Careers.StartAsync(aurora.Id, "Test Manager");
        await Careers.AdvanceDayAsync();
        var career = (await Careers.LoadAsync())!;
        await Saves.SaveAsync("My career");
        var players = await CountAsync<Player, PlayerId>();

        var created = await Packages.CreateAsync(new PackageManifest("Italia Mod", "Tester", "1.2", "The sample league.", []));
        Assert.Contains(await Packages.ListAsync(), p => p.FileName == "Italia Mod.ebmpack" && p.Manifest!.Author == "Tester");
        await Assert.ThrowsAsync<Ebm.Domain.Common.DomainValidationException>(() => Packages.CreateAsync(created.Manifest!));

        var installed = await Packages.InstallAsync(created.FilePath, InstallTarget.NewGame);
        Assert.True(installed.Installed, installed.Message);
        Assert.Null(await Careers.LoadAsync());
        Assert.Equal(players, await CountAsync<Player, PlayerId>());

        // The saved career is a separate copy: loading it gives back the career as it was.
        await Saves.LoadAsync("My career");
        await AssertSameCareerAsync(career);
    }

    [Theory]
    [InlineData("missing-manifest")]
    [InlineData("path-outside")]
    [InlineData("newer-game")]
    [InlineData("missing-file")]
    public async Task Broken_packages_are_refused_with_a_reason(string kind)
    {
        var path = Path.Combine(_db.TempFolder, kind + ".ebmpack");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Add(zip, "data.json", """{ "countries": [] }""");
            var manifest = kind switch
            {
                "path-outside" => Manifest("../evil.json"),
                "newer-game" => Manifest("data.json", "99.0.0"),
                "missing-file" => Manifest("other.json"),
                _ => null,
            };
            if (manifest is not null)
            {
                Add(zip, "ebmpack.json", manifest);
            }
        }

        var check = await Packages.ValidateAsync(path);
        Assert.False(check.CanInstall);
        Assert.NotEmpty(check.Problems);
        Assert.False((await Packages.InstallAsync(path, InstallTarget.CurrentGame)).Installed);
    }

    [Fact]
    public async Task The_mods_screen_lists_checks_and_renders_packages()
    {
        Package("custom-cup", """
            { "competitions": [ { "name": "Coppa dei Modder", "shortName": "Modder", "type": "Cup", "country": "ITA" } ] }
            """);
        Directory.CreateDirectory(Packages.ModsDirectory);
        File.Copy(Path.Combine(_db.TempFolder, "custom-cup.ebmpack"), Path.Combine(Packages.ModsDirectory, "custom-cup.ebmpack"));
        var reloads = 0;
        var screen = new Ebm.Desktop.ViewModels.Modding.ModsViewModel(
            Packages, _db.Services.GetRequiredService<IDatabaseExportService>(), _db.Services.GetRequiredService<IDatabaseValidator>(),
            new NoFiles(), () => { reloads++; return Task.CompletedTask; });
        await screen.RefreshAsync();
        screen.SelectedPackage = Assert.Single(screen.PackageRows);
        Assert.Equal("Ready", screen.SelectedPackage.Status);

        await screen.CheckPackageCommand.ExecuteAsync(null);
        Assert.False(screen.IsError, screen.Message);
        Assert.StartsWith("Valid", screen.Message, StringComparison.Ordinal);

        // Install as a new game asks for a second click before replacing the game.
        await screen.InstallAsNewGameCommand.ExecuteAsync(null);
        Assert.True(screen.ConfirmNewGame);
        Assert.Equal(0, reloads);

        await screen.ValidateDatabaseCommand.ExecuteAsync(null);
        Assert.Equal("Database validation", screen.IssuesTitle);

        var rendered = Players.PlayerScreensRenderTests.RunOnSta(() =>
        {
            var resources = (System.Windows.Application.Current ?? new System.Windows.Application()).Resources;
            foreach (var name in new[] { "Shell", "Editor", "Mods" })
            {
                resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = new Uri($"pack://application:,,,/Ebm.Desktop;component/Themes/{name}.xaml") });
            }

            return Players.PlayerScreensRenderTests.Render("mods", screen);
        });
        Assert.True(rendered > 0);
    }

    private sealed class NoFiles : Ebm.Desktop.Startup.IFilePicker
    {
        public IReadOnlyList<string>? PickImportFiles() => null;
    }

    private async Task AssertSameCareerAsync(CareerState expected)
    {
        var state = (await Careers.LoadAsync())!;
        Assert.Equal(expected.Career.ManagerName, state.Career.ManagerName);
        Assert.Equal(expected.Club.Id, state.Club.Id);
        Assert.Equal(expected.Today, state.Today);
        Assert.Equal(expected.Season.Id, state.Season.Id);
    }

    private Task<int> CountAsync<TEntity, TId>()
        where TEntity : Ebm.Domain.Common.Entity<TId>
        where TId : struct, Ebm.Domain.Common.IEntityId<TId> =>
        _db.ReadAsync(async s => (await s.Repo<TEntity, TId>().ListAsync()).Count);

    private string Package(string name, string dataset)
    {
        var path = Path.Combine(_db.TempFolder, name + ".ebmpack");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "data.json", dataset);
        Add(zip, "ebmpack.json", Manifest("data.json"));
        return path;
    }

    private static string Manifest(string file, string minGameVersion = "") =>
        $$"""{ "format": "ebm-package", "formatVersion": 1, "name": "Test", "author": "Tests", "version": "1", "files": [ "{{file}}" ], "minGameVersion": "{{minGameVersion}}" }""";

    private static void Add(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }
}
