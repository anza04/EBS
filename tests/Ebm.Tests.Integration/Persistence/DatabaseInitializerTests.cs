using Ebm.Domain.Clubs;
using Ebm.Domain.Players;
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
    private const string LatestMigration = "_Contracts";

    private static readonly string[] ExpectedTables =
    [
        "Arenas", "Clubs", "CompetitionParticipants", "Competitions", "Contracts", "Countries", "Divisions",
        "Fixtures", "LeagueStandings", "Leagues", "Matches", "PlayerAttributes", "PlayerClubHistory",
        "PlayerMatchStatistics", "PlayerTendencies", "Players", "RosterSpots", "Seasons", "Staff",
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

        Assert.EndsWith(LatestMigration, version, StringComparison.Ordinal);
        Assert.Equal(["RegularSeason|1"], await db.QueryStringsAsync("SELECT Stage || '|' || GameNumber FROM Fixtures"));
    }

    [Fact]
    public async Task Step_04_players_upgrade_to_the_full_player_model()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.ReadAsync(async s =>
        {
            await s.Services.GetRequiredService<EbmDbContext>().Database.MigrateAsync("20261109124652_FixtureStages");
            return 0;
        });
        const string id = "22222222-2222-2222-2222-222222222222";
        await db.ExecuteSqlAsync($"""
            INSERT INTO Countries (Id, Code, Name) VALUES ('33333333-3333-3333-3333-333333333333', 'ITA', 'Italy');
            INSERT INTO Players (Id, FirstName, LastName, DateOfBirth, NationalityId, HeightCm, WeightKg, Position)
            VALUES ('{id}', 'Old', 'Save', '1999-04-01', '33333333-3333-3333-3333-333333333333', 204, 100, 'PowerForward');
            INSERT INTO PlayerAttributes (PlayerId, Shooting, InsideScoring, Passing, BallHandling, Rebounding, Defense, Athleticism, BasketballIq)
            VALUES ('{id}', 60, 81, 40, 35, 90, 70, 75, 66);
            """);

        Assert.EndsWith(LatestMigration, await db.Initializer.InitializeAsync(), StringComparison.Ordinal);
        var player = await db.ReadAsync(s => s.Repo<Player, PlayerId>().FindAsync(PlayerId.From(Guid.Parse(id))));

        Assert.NotNull(player);
        Assert.Equal((204, 1, 1, 64), (player.WingspanCm, player.Experience, player.Reputation, player.Potential)); // potential = old mean 64.6
        Assert.Equal((100, 50, InjuryStatus.Healthy), (player.Fitness, player.Morale, player.InjuryStatus));
        Assert.True(player.SecondaryPositions.IsEmpty);
        var a = player.Attributes;
        Assert.Equal((70, 60, 60, 60), (a.CloseShot, a.MidRange, a.ThreePoint, a.FreeThrow));
        Assert.Equal((81, 78, 81), (a.Layup, a.Dunk, a.PostMoves));
        Assert.Equal((40, 53, 35, 66), (a.Passing, a.CourtVision, a.BallHandling, a.DecisionMaking));
        Assert.Equal((70, 70, 70, 80, 68), (a.PerimeterDefense, a.InteriorDefense, a.Steal, a.Block, a.DefensivePositioning));
        Assert.Equal((90, 90), (a.OffensiveRebound, a.DefensiveRebound));
        Assert.Equal((75, 82, 75, 75, 75), (a.Speed, a.Strength, a.Vertical, a.Agility, a.Stamina));
        Assert.Equal((66, 66, 66, 66), (a.Concentration, a.Leadership, a.Consistency, a.WorkRate));
        Assert.Equal(PlayerTendencies.Suggest(a).ToDictionary(), player.Tendencies.ToDictionary());
        Assert.Empty(await db.QueryStringsAsync("SELECT name FROM sqlite_master WHERE name LIKE 'PlayerAttributes_%'"));
    }

    [Fact]
    public async Task Step_05_clubs_upgrade_with_defaults_and_the_arena_city()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.ReadAsync(async s =>
        {
            await s.Services.GetRequiredService<EbmDbContext>().Database.MigrateAsync("20261109125814_PlayerModel");
            return 0;
        });
        const string withArena = "44444444-4444-4444-4444-444444444444", withoutArena = "55555555-5555-5555-5555-555555555555";
        await db.ExecuteSqlAsync($"""
            INSERT INTO Countries (Id, Code, Name) VALUES ('33333333-3333-3333-3333-333333333333', 'ITA', 'Italy');
            INSERT INTO Arenas (Id, Name, City, CountryId, Capacity) VALUES ('66666666-6666-6666-6666-666666666666', 'Old Arena', 'Varese', '33333333-3333-3333-3333-333333333333', 5000);
            INSERT INTO Clubs (Id, Name, ShortName, CountryId, HomeArenaId) VALUES
              ('{withArena}', 'Old Varese', 'VAR', '33333333-3333-3333-3333-333333333333', '66666666-6666-6666-6666-666666666666'),
              ('{withoutArena}', 'Old Roma', 'ROM', '33333333-3333-3333-3333-333333333333', NULL);
            """);

        Assert.EndsWith(LatestMigration, await db.Initializer.InitializeAsync(), StringComparison.Ordinal);
        var clubs = await db.ReadAsync(s => s.Repo<Club, ClubId>().ListAsync());

        var varese = clubs.Single(c => c.Id.Value == Guid.Parse(withArena));
        Assert.Equal(("Varese", Club.DefaultReputation, 0L, 0L, Club.DefaultBoardExpectation), (varese.City, varese.Reputation, varese.Balance, varese.WageBudget, varese.BoardExpectation));
        Assert.Null(clubs.Single(c => c.Id.Value == Guid.Parse(withoutArena)).City);
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
