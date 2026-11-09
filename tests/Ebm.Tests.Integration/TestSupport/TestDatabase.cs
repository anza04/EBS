using Ebm.Domain.Common;
using Ebm.DataImport;
using Ebm.Persistence;
using Ebm.Services;
using Ebm.Services.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Tests.Integration.TestSupport;

/// <summary>A migrated game database in a temporary folder, plus helpers to work in fresh DI scopes.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly TempDataRoot _root = new();
    private readonly ServiceProvider _provider;

    private TestDatabase(TimeProvider? time)
    {
        DatabasePath = Path.Combine(_root.Path, "databases", "test.db");
        ImportLogDirectory = Path.Combine(_root.Path, "logs", "imports");
        var services = new ServiceCollection();
        if (time is not null)
        {
            services.AddSingleton(time);
        }

        _provider = services
            .AddLogging()
            .AddGameServices()
            .AddPersistence(DatabasePath)
            .AddDataImport(ImportLogDirectory)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public string DatabasePath { get; }

    public string ImportLogDirectory { get; }

    /// <summary>Root provider with game services, persistence and data import registered.</summary>
    public IServiceProvider Services => _provider;

    /// <summary>Temporary folder deleted with the database; tests may write import files here.</summary>
    public string TempFolder => _root.Path;

    public string SchemaVersion { get; private set; } = string.Empty;

    public DatabaseInitializer Initializer => _provider.GetRequiredService<DatabaseInitializer>();

    /// <param name="time">Clock for the game services; the system clock when omitted.</param>
    public static async Task<TestDatabase> CreateAsync(TimeProvider? time = null)
    {
        var database = new TestDatabase(time);
        database.SchemaVersion = await database.Initializer.InitializeAsync();
        return database;
    }

    /// <summary>Runs <paramref name="work"/> in a new scope and saves its changes.</summary>
    public async Task WriteAsync(Action<Scope> work)
    {
        using var scope = new Scope(_provider.CreateScope());
        work(scope);
        await scope.Services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>Runs a query in a new scope, so results come from the database rather than the change tracker.</summary>
    public async Task<T> ReadAsync<T>(Func<Scope, Task<T>> query)
    {
        using var scope = new Scope(_provider.CreateScope());
        return await query(scope);
    }

    /// <summary>Executes raw SQL, bypassing domain validation, to prove database-level constraints.</summary>
    public async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new SqliteConnection(new PersistenceOptions(DatabasePath).ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<string>> QueryStringsAsync(string sql)
    {
        await using var connection = new SqliteConnection(new PersistenceOptions(DatabasePath).ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _root.Dispose();
    }

    public sealed class Scope : IDisposable
    {
        private readonly IServiceScope _scope;

        internal Scope(IServiceScope scope) => _scope = scope;

        public IServiceProvider Services => _scope.ServiceProvider;

        public IRepository<TEntity, TId> Repo<TEntity, TId>()
            where TEntity : Entity<TId>
            where TId : struct, IEntityId<TId> =>
            Services.GetRequiredService<IRepository<TEntity, TId>>();

        public void Dispose() => _scope.Dispose();
    }
}
