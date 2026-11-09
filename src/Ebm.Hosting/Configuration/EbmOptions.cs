using Serilog.Events;

namespace Ebm.Hosting.Configuration;

/// <summary>
/// Root of the application configuration, bound from the <c>Ebm</c> section.
/// Sources (later wins): appsettings.json, appsettings.{Environment}.json, environment variables
/// (e.g. <c>Ebm__Paths__DataRoot</c>), command line (e.g. <c>--Ebm:Paths:DataRoot=C:\EbmData</c>).
/// </summary>
public sealed class EbmOptions
{
    public const string SectionName = "Ebm";

    public PathsOptions Paths { get; set; } = new();

    public LoggingOptions Logging { get; set; } = new();
}

public sealed class PathsOptions
{
    /// <summary>
    /// Folder holding all user data (logs, databases; save games in later steps).
    /// Empty means <c>%LOCALAPPDATA%\EuropeanBasketballManager</c>. Environment variables are expanded.
    /// </summary>
    public string? DataRoot { get; set; }
}

public sealed class LoggingOptions
{
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

    /// <summary>Number of daily log files kept before the oldest is deleted.</summary>
    public int RetainedFileCount { get; set; } = 14;
}
