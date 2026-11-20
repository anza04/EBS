using Ebm.DataImport.Modding;
using Ebm.Services.Import;
using Ebm.Services.Modding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ebm.DataImport;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the CSV/JSON import service (<see cref="IDataImportService"/>).</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="importLogDirectory">Folder that receives one log file per import.</param>
    /// <param name="modsDirectory">Folder of community packages; default <c>mods</c> in the data root (two levels above the import logs).</param>
    public static IServiceCollection AddDataImport(this IServiceCollection services, string importLogDirectory, string? modsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(importLogDirectory);
        services.TryAddSingleton(TimeProvider.System);
        var logs = Path.GetFullPath(importLogDirectory);
        var mods = modsDirectory is { Length: > 0 } ? Path.GetFullPath(modsDirectory) : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(logs)) ?? logs, "mods");
        services.AddSingleton(new DataImportOptions(logs, mods));
        services.AddSingleton<ImportLogWriter>();
        services.AddSingleton<IDataImportService, DataImportService>();
        services.AddSingleton<IDatabaseExportService, DatabaseExportService>();
        services.AddSingleton<IDatabaseValidator, DatabaseValidator>();
        services.AddSingleton<IModPackageService, ModPackageService>();
        return services;
    }
}
