using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ebm.Persistence;

/// <summary>
/// Creates the game database if it does not exist and applies pending migrations, in order.
/// Runs once when the host starts; a failure stops startup. At startup it also recovers a damaged game database from
/// the newest automatic backup and backs the game up before upgrading it (docs/SAVE_FILES.md §4).
/// </summary>
public sealed partial class DatabaseInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PersistenceOptions _options;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly TimeProvider _time;

    public DatabaseInitializer(IServiceScopeFactory scopeFactory, PersistenceOptions options, ILogger<DatabaseInitializer> logger, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <summary>Migrates the database to the latest schema version.</summary>
    /// <returns>The ID of the latest applied migration (the schema version).</returns>
    public async Task<string> InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_options.DatabasePath)!);

        using var scope = _scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<EbmDbContext>().Database;

        var pending = (await database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        if (pending.Count == 0)
        {
            LogUpToDate(_options.DatabasePath);
        }
        else
        {
            LogMigrating(_options.DatabasePath, pending.Count, string.Join(", ", pending));
            await database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        var version = (await database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).Last();
        LogReady(version);
        return version;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Recover();
        await BackUpBeforeUpgradeAsync(cancellationToken).ConfigureAwait(false);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lets SQLite refresh its query-planner statistics where they are stale (cheap; recommended at close).</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_options.DatabasePath))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        try
        {
            await scope.ServiceProvider.GetRequiredService<EbmDbContext>().Database.ExecuteSqlRawAsync("PRAGMA optimize", cancellationToken).ConfigureAwait(false);
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
        {
            LogOptimizeFailed(ex);
        }
    }

    /// <summary>
    /// A game database that fails the integrity check is kept aside as <c>ebm.db.damaged-&lt;timestamp&gt;</c> and replaced
    /// by the newest intact automatic backup (or, with none, by a new empty game).
    /// </summary>
    private void Recover()
    {
        var path = _options.DatabasePath;
        if (!File.Exists(path) || new FileInfo(path).Length == 0 || DatabaseFiles.IsIntact(path))
        {
            return;
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var damaged = $"{path}.damaged-{_time.GetLocalNow():yyyyMMdd-HHmmss}";
        File.Move(path, damaged, overwrite: true);
        foreach (var name in new[] { "-journal", "-wal", "-shm" })
        {
            File.Delete(path + name);
        }

        var backup = DatabaseFiles.AutoBackups(_options.SavesDirectory).FirstOrDefault(DatabaseFiles.IsIntact);
        if (backup is null)
        {
            LogDamagedNoBackup(path, damaged);
            return;
        }

        DatabaseFiles.Copy(backup, path);
        LogRecovered(path, damaged, backup);
    }

    private async Task BackUpBeforeUpgradeAsync(CancellationToken cancellationToken)
    {
        if (!DatabaseFiles.HasCareer(_options.DatabasePath))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var pending = await scope.ServiceProvider.GetRequiredService<EbmDbContext>().Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
        if (pending.Any())
        {
            LogBackedUp(DatabaseFiles.AutoBackup(_options.DatabasePath, _options.SavesDirectory, _time.GetLocalNow().DateTime)!);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database {Path} is up to date")]
    private partial void LogUpToDate(string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database {Path}: applying {Count} migration(s): {Migrations}")]
    private partial void LogMigrating(string path, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Error, Message = "Database {Path} is damaged; kept as {Damaged} and restored from {Backup}")]
    private partial void LogRecovered(string path, string damaged, string backup);

    [LoggerMessage(Level = LogLevel.Error, Message = "Database {Path} is damaged and there is no automatic backup; kept as {Damaged}, starting a new game")]
    private partial void LogDamagedNoBackup(string path, string damaged);

    [LoggerMessage(Level = LogLevel.Information, Message = "Game backed up to {Backup} before the database upgrade")]
    private partial void LogBackedUp(string backup);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PRAGMA optimize failed")]
    private partial void LogOptimizeFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database ready, schema version {SchemaVersion}")]
    private partial void LogReady(string schemaVersion);
}
