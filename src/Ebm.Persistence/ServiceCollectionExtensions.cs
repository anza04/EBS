using Ebm.Services.Data;
using Ebm.Services.Saves;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLite game database, the repository/unit-of-work implementations and the
    /// <see cref="DatabaseInitializer"/>, which migrates the database when the host starts.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="databasePath">Absolute path of the database file; its folder is created if missing.</param>
    /// <param name="savesDirectory">Folder of the save files; <c>saves</c> next to the database folder when omitted.</param>
    public static IServiceCollection AddPersistence(this IServiceCollection services, string databasePath, string? savesDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var options = savesDirectory is null
            ? new PersistenceOptions(Path.GetFullPath(databasePath))
            : new PersistenceOptions(Path.GetFullPath(databasePath), Path.GetFullPath(savesDirectory));
        services.AddSingleton(options);
        services.AddDbContext<EbmDbContext>(o => o.UseSqlite(options.ConnectionString));
        services.AddScoped(typeof(IRepository<,>), typeof(EfRepository<,>));
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<ISaveGameService, SaveGameStore>();
        services.AddHostedService(sp => sp.GetRequiredService<DatabaseInitializer>());

        return services;
    }
}
