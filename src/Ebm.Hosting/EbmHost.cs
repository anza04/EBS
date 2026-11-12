using Ebm.DataImport;
using Ebm.Hosting.Configuration;
using Ebm.Hosting.ErrorHandling;
using Ebm.Hosting.Logging;
using Ebm.Persistence;
using Ebm.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ebm.Hosting;

/// <summary>
/// Composition root shared by the desktop application, tools and integration tests.
/// </summary>
public static class EbmHost
{
    /// <summary>
    /// Creates a host builder with configuration, logging, error handling and every layer registered.
    /// The caller adds its own front-end services (e.g. WPF windows) and then builds the host.
    /// </summary>
    /// <param name="args">Command-line arguments; <c>--Ebm:Section:Key=value</c> overrides configuration.</param>
    /// <param name="contentRoot">Folder containing appsettings.json; defaults to the application folder.</param>
    public static HostApplicationBuilder CreateBuilder(string[] args, string? contentRoot = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = contentRoot ?? AppContext.BaseDirectory,
            ApplicationName = ApplicationInfo.DefaultProductName,
        });

        var section = builder.Configuration.GetSection(EbmOptions.SectionName);
        var options = section.Get<EbmOptions>() ?? new EbmOptions();

        var paths = AppPaths.Resolve(options.Paths);
        paths.EnsureCreated();

        builder.Logging.UseEbmLogging(options.Logging, paths);

        builder.Services.Configure<EbmOptions>(section);
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton(ApplicationInfo.FromAssembly(typeof(EbmHost).Assembly));
        builder.Services.AddSingleton<ExceptionHandlingPolicy>();

        builder.Services
            .AddGameServices()
            .AddPersistence(paths.DatabaseFile, paths.SavesDirectory)
            .AddDataImport(paths.ImportLogsDirectory);

        return builder;
    }
}
