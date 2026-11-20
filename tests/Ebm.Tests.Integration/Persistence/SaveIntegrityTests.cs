using System.Reflection;
using Ebm.Services.Import;
using Ebm.Domain.Common;
using Ebm.Services.Careers;
using Ebm.Services.Saves;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.Persistence;

/// <summary>Step 34: verified saves, automatic backups and startup recovery (docs/SAVE_FILES.md §4).</summary>
public sealed class SaveIntegrityTests : IAsyncLifetime
{
    private const string Prefix = "Autobackup ";
    private TestDatabase _db = null!;

    private T Get<T>()
        where T : notnull => _db.Services.GetRequiredService<T>();

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateAsync();
        var data = typeof(SaveIntegrityTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "RepositoryDataPath").Value!;
        var report = await Get<IDataImportService>().ImportAsync(
            [Path.Combine(data, "sample", "italy-sample.json"), Path.Combine(data, "sample", "italy-squads.json")], new ImportOptions());
        Assert.True(report.Committed);
        var careers = Get<ICareerService>();
        await careers.StartAsync((await careers.GetClubOptionsAsync())[0].Club.Id, "Integrity");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Loading_keeps_the_replaced_game_as_an_automatic_backup_and_only_the_newest_five()
    {
        var saves = Get<ISaveGameService>();
        await saves.SaveAsync("Start");
        for (var i = 0; i < 7; i++)
        {
            await saves.LoadAsync("Start");
        }

        var all = await saves.ListAsync();
        Assert.Equal(5, all.Count(s => s.Name.StartsWith(Prefix, StringComparison.Ordinal)));
        Assert.All(all, s => Assert.NotNull(s.Career));
        Assert.Contains(all, s => s.Name == "Start");
    }

    [Fact]
    public async Task A_new_game_backs_up_the_career_and_an_empty_game_is_not_backed_up()
    {
        var saves = Get<ISaveGameService>();
        await saves.NewGameAsync();
        await saves.NewGameAsync(); // nothing to keep: no career

        var backup = Assert.Single(await saves.ListAsync());
        Assert.StartsWith(Prefix, backup.Name, StringComparison.Ordinal);
        await saves.LoadAsync(backup.Name);
        Assert.NotNull(await Get<ICareerService>().LoadAsync());
    }

    [Fact]
    public async Task A_damaged_save_is_refused_and_the_live_game_is_untouched()
    {
        var saves = Get<ISaveGameService>();
        var save = await saves.SaveAsync("Damaged");
        Damage(save.FilePath);

        await Assert.ThrowsAsync<DomainValidationException>(() => saves.LoadAsync("Damaged"));
        Assert.NotNull(await Get<ICareerService>().LoadAsync());
        Assert.DoesNotContain(await saves.ListAsync(), s => s.Name.StartsWith(Prefix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_damaged_game_database_is_restored_from_the_newest_backup_at_startup()
    {
        var saves = Get<ISaveGameService>();
        await saves.SaveAsync("Start");
        await saves.LoadAsync("Start"); // makes an automatic backup of the career
        Damage(_db.DatabasePath);

        await _db.Initializer.StartAsync(CancellationToken.None);

        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_db.DatabasePath)!, "*.damaged-*"));
        var career = await Get<ICareerService>().LoadAsync();
        Assert.Equal("Integrity", career!.Career.ManagerName);
    }

    /// <summary>Overwrites every page after the first (the schema stays readable, the data does not).</summary>
    private static void Damage(string path)
    {
        SqliteConnection.ClearAllPools();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        stream.Position = 4096;
        var garbage = new byte[stream.Length - 4096];
        Array.Fill(garbage, (byte)0xA5);
        stream.Write(garbage);
    }
}
