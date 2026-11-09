using Ebm.Hosting;
using Ebm.Persistence;
using Ebm.Tests.Integration.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ebm.Tests.Integration.Persistence;

public sealed class DatabaseInitializerTests
{
    private static readonly string[] ExpectedTables =
    [
        "Arenas", "Clubs", "CompetitionParticipants", "Competitions", "Contracts", "Countries", "Divisions",
        "Fixtures", "LeagueStandings", "Leagues", "Matches", "PlayerAttributes", "PlayerClubHistory",
        "PlayerMatchStatistics", "Players", "Seasons", "Staff",
    ];

    [Fact]
    public async Task Creates_database_from_scratch_with_all_tables()
    {
        await using var db = await TestDatabase.CreateAsync();

        Assert.True(File.Exists(db.DatabasePath));
        var tables = await db.QueryStringsAsync(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT IN ('__EFMigrationsHistory', '__EFMigrationsLock') ORDER BY name");
        Assert.Equal(ExpectedTables, tables);
    }

    [Fact]
    public async Task Records_applied_migrations_and_reports_schema_version()
    {
        await using var db = await TestDatabase.CreateAsync();

        var applied = await db.QueryStringsAsync("SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId");
        Assert.NotEmpty(applied);
        Assert.Equal(applied[^1], db.SchemaVersion);
        Assert.EndsWith("_InitialSchema", applied[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Running_again_is_a_no_op()
    {
        await using var db = await TestDatabase.CreateAsync();

        var version = await db.Initializer.InitializeAsync();

        Assert.Equal(db.SchemaVersion, version);
        Assert.Single(await db.QueryStringsAsync("SELECT MigrationId FROM __EFMigrationsHistory"), id => id == version);
    }

    [Fact]
    public async Task Step_02_database_with_fixtures_upgrades_in_place()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.ReadAsync(async s =>
        {
            await s.Services.GetRequiredService<EbmDbContext>().Database.MigrateAsync("20261109114155_InitialSchema");
            return 0;
        });
        await db.ExecuteSqlAsync("""
            PRAGMA foreign_keys = OFF;
            INSERT INTO Fixtures (Id, CompetitionId, SeasonId, Round, Date, HomeClubId, AwayClubId)
            VALUES ('11111111-1111-1111-1111-111111111111', 'c', 's', 3, '2026-10-01', 'h', 'a');
            """);

        var version = await db.Initializer.InitializeAsync();

        Assert.EndsWith("_FixtureStages", version, StringComparison.Ordinal);
        Assert.Equal(["RegularSeason|1"], await db.QueryStringsAsync("SELECT Stage || '|' || GameNumber FROM Fixtures"));
    }

    [Fact]
    public async Task Model_has_no_changes_missing_from_migrations()
    {
        await using var db = await TestDatabase.CreateAsync();

        var pending = await db.ReadAsync(s =>
            Task.FromResult(s.Services.GetRequiredService<EbmDbContext>().Database.HasPendingModelChanges()));

        Assert.False(pending, "The EF model changed without a migration. Run: dotnet ef migrations add <Name> --project src/Ebm.Persistence");
    }

    [Fact]
    public async Task Foreign_keys_are_enforced_by_the_connection()
    {
        await using var db = await TestDatabase.CreateAsync();

        var value = await db.QueryStringsAsync("SELECT CAST(foreign_keys AS TEXT) FROM pragma_foreign_keys");

        Assert.Equal(["1"], value);
    }

    [Fact]
    public async Task Host_start_creates_and_migrates_the_database_in_the_data_root()
    {
        using var data = new TempDataRoot();

        using (var host = EbmHost.CreateBuilder([data.HostArgument]).Build())
        {
            await host.StartAsync();
            Assert.True(File.Exists(host.Services.GetRequiredService<AppPaths>().DatabaseFile));
            await host.StopAsync();
        }

        SqliteConnection.ClearAllPools();
        Assert.Contains("Database ready, schema version", data.ReadAllLogs(), StringComparison.Ordinal);
    }
}
