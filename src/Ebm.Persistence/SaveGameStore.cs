using System.Globalization;
using Ebm.Domain.Common;
using Ebm.Services.Careers;
using Ebm.Services.Saves;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Persistence;

/// <summary>
/// Implements <see cref="ISaveGameService"/> with SQLite online backups, so a save is a consistent copy even while the
/// game database is open, and loading overwrites the live file in place (no file is deleted under open handles).
/// </summary>
internal sealed class SaveGameStore(
    PersistenceOptions options,
    DatabaseInitializer initializer,
    IServiceScopeFactory scopes,
    ICareerService careers) : ISaveGameService
{
    public const string Extension = ".ebmsave";
    public const int NameMaxLength = 60;

    public string SavesDirectory => options.SavesDirectory;

    public Task<IReadOnlyList<SaveGameInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(SavesDirectory))
        {
            return Task.FromResult<IReadOnlyList<SaveGameInfo>>([]);
        }

        IReadOnlyList<SaveGameInfo> saves =
        [
            .. Directory.EnumerateFiles(SavesDirectory, "*" + Extension)
                .Select(Describe)
                .OrderByDescending(s => s.SavedAt),
        ];
        return Task.FromResult(saves);
    }

    public async Task<SaveGameInfo> SaveAsync(string name, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var path = PathOf(name);
        if (File.Exists(path) && !overwrite)
        {
            throw new DomainValidationException($"A save called '{name.Trim()}' already exists.");
        }

        Directory.CreateDirectory(SavesDirectory);
        var temporary = path + ".tmp";
        File.Delete(temporary);
        await using (var source = await OpenAsync(options.DatabasePath, cancellationToken).ConfigureAwait(false))
        await using (var target = await OpenAsync(temporary, cancellationToken).ConfigureAwait(false))
        {
            source.BackupDatabase(target);
        }

        SqliteConnection.ClearAllPools();
        File.Move(temporary, path, overwrite: true);
        return Describe(path);
    }

    public async Task LoadAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = PathOf(name);
        if (!File.Exists(path))
        {
            throw new DomainValidationException($"There is no save called '{name.Trim()}'.");
        }

        CheckCompatible(path);
        SqliteConnection.ClearAllPools();
        await using (var source = await OpenAsync(path, cancellationToken).ConfigureAwait(false))
        await using (var target = await OpenAsync(options.DatabasePath, cancellationToken).ConfigureAwait(false))
        {
            source.BackupDatabase(target);
        }

        SqliteConnection.ClearAllPools();
        await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await careers.LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = PathOf(name);
        if (!File.Exists(path))
        {
            throw new DomainValidationException($"There is no save called '{name.Trim()}'.");
        }

        SqliteConnection.ClearAllPools();
        File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task NewGameAsync(CancellationToken cancellationToken = default)
    {
        // An empty, fully migrated database copied over the live one.
        var empty = Path.Combine(Path.GetDirectoryName(options.DatabasePath)!, $"new-{Guid.NewGuid():N}.db");
        try
        {
            var context = new EbmDbContext(new DbContextOptionsBuilder<EbmDbContext>().UseSqlite(new PersistenceOptions(empty).ConnectionString).Options);
            await using (context.ConfigureAwait(false))
            {
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }

            SqliteConnection.ClearAllPools();
            await using (var source = await OpenAsync(empty, cancellationToken).ConfigureAwait(false))
            await using (var target = await OpenAsync(options.DatabasePath, cancellationToken).ConfigureAwait(false))
            {
                source.BackupDatabase(target);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(empty);
        }

        await careers.LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The save's file; the name may use letters, digits, spaces, '-' and '_'.</summary>
    private string PathOf(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength)
        {
            throw new DomainValidationException($"A save name needs 1 to {NameMaxLength} characters.");
        }

        if (trimmed.Any(c => !(char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')))
        {
            throw new DomainValidationException("A save name may contain letters, digits, spaces, '-' and '_' only.");
        }

        return Path.Combine(SavesDirectory, trimmed + Extension);
    }

    /// <summary>Refuses files that are not game databases or come from a newer version of the game.</summary>
    private void CheckCompatible(string path)
    {
        var known = KnownMigrations();
        var version = SchemaVersion(path) ?? throw new DomainValidationException("The file is not a European Basketball Manager save.");
        if (!known.Contains(version))
        {
            throw new DomainValidationException($"The save was made by a newer version of the game (schema {version}).");
        }
    }

    private IReadOnlySet<string> KnownMigrations()
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<EbmDbContext>().Database.GetMigrations().ToHashSet();
    }

    private static SaveGameInfo Describe(string path)
    {
        var file = new FileInfo(path);
        string? career = null;
        string? version = null;
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            version = SchemaVersion(connection);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT c.ManagerName, cl.Name, c.CurrentDate FROM Careers c JOIN Clubs cl ON cl.Id = c.ClubId LIMIT 1";
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                var date = DateOnly.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
                career = $"{reader.GetString(0)} · {reader.GetString(1)} · {date:yyyy-MM-dd}";
            }
        }
        catch (SqliteException)
        {
            // Not a game database, or one from before careers existed: listed without details.
        }

        return new SaveGameInfo(Path.GetFileNameWithoutExtension(path), path, file.LastWriteTime, file.Length, career, version ?? "?");
    }

    private static string? SchemaVersion(string path)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            return SchemaVersion(connection);
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    private static string? SchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MigrationId FROM \"{HistoryRepository.DefaultTableName}\" ORDER BY MigrationId DESC LIMIT 1";
        return command.ExecuteScalar() as string;
    }

    private static async Task<SqliteConnection> OpenAsync(string path, CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
