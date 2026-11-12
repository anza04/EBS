using Ebm.Hosting.Configuration;

namespace Ebm.Hosting;

/// <summary>
/// Resolved, absolute locations of the application's user-data folders.
/// </summary>
public sealed class AppPaths
{
    public const string DefaultFolderName = "EuropeanBasketballManager";

    public const string DatabaseFileName = "ebm.db";

    private AppPaths(string dataRoot)
    {
        DataRoot = dataRoot;
        LogsDirectory = Path.Combine(dataRoot, "logs");
        DatabasesDirectory = Path.Combine(dataRoot, "databases");
        DatabaseFile = Path.Combine(DatabasesDirectory, DatabaseFileName);
        ImportLogsDirectory = Path.Combine(LogsDirectory, "imports");
        SavesDirectory = Path.Combine(dataRoot, "saves");
    }

    public string DataRoot { get; }

    public string LogsDirectory { get; }

    public string DatabasesDirectory { get; }

    /// <summary>One log file per data import.</summary>
    public string ImportLogsDirectory { get; }

    /// <summary>Save files (copies of the game database).</summary>
    public string SavesDirectory { get; }

    /// <summary>SQLite game database.</summary>
    public string DatabaseFile { get; }

    public static string DefaultDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DefaultFolderName);

    public static AppPaths Resolve(PathsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var root = string.IsNullOrWhiteSpace(options.DataRoot)
            ? DefaultDataRoot
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.DataRoot.Trim()));

        return new AppPaths(root);
    }

    /// <summary>Creates any missing folders. Safe to call repeatedly.</summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(DatabasesDirectory);
        Directory.CreateDirectory(ImportLogsDirectory);
        Directory.CreateDirectory(SavesDirectory);
    }
}
