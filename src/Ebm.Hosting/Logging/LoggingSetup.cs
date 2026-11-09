using Ebm.Hosting.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Ebm.Hosting.Logging;

/// <summary>
/// Configures Serilog as the only logging provider. Application code logs through
/// <see cref="Microsoft.Extensions.Logging.ILogger{TCategoryName}"/> and never references Serilog directly.
/// </summary>
public static class LoggingSetup
{
    public const string LogFilePrefix = "ebm-";

    public const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public static Serilog.Core.Logger CreateLogger(LoggingOptions options, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);

        return new LoggerConfiguration()
            .MinimumLevel.Is(options.MinimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(paths.LogsDirectory, LogFilePrefix + ".log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Math.Max(1, options.RetainedFileCount),
                outputTemplate: OutputTemplate)
            .WriteTo.Debug(outputTemplate: OutputTemplate)
            .CreateLogger();
    }

    public static ILoggingBuilder UseEbmLogging(this ILoggingBuilder logging, LoggingOptions options, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(logging);

        logging.ClearProviders();
        // Serilog owns level filtering; let every event through the Microsoft pipeline.
        logging.SetMinimumLevel(LogLevel.Trace);
        logging.AddSerilog(CreateLogger(options, paths), dispose: true);

        return logging;
    }
}
