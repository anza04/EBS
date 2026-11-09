using Ebm.Services.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ebm.DataImport;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the CSV/JSON import service (<see cref="IDataImportService"/>).</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="importLogDirectory">Folder that receives one log file per import.</param>
    public static IServiceCollection AddDataImport(this IServiceCollection services, string importLogDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(importLogDirectory);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(new DataImportOptions(Path.GetFullPath(importLogDirectory)));
        services.AddSingleton<ImportLogWriter>();
        services.AddSingleton<IDataImportService, DataImportService>();
        return services;
    }
}
