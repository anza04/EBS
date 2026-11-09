using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ebm.Persistence;

/// <summary>
/// Creates the game database if it does not exist and applies pending migrations, in order.
/// Runs once when the host starts; a failure stops startup.
/// </summary>
public sealed partial class DatabaseInitializer : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PersistenceOptions _options;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IServiceScopeFactory scopeFactory, PersistenceOptions options, ILogger<DatabaseInitializer> logger)
    {
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

    public Task StartAsync(CancellationToken cancellationToken) => InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Database {Path} is up to date")]
    private partial void LogUpToDate(string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database {Path}: applying {Count} migration(s): {Migrations}")]
    private partial void LogMigrating(string path, int count, string migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database ready, schema version {SchemaVersion}")]
    private partial void LogReady(string schemaVersion);
}
