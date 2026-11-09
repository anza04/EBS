using System.Globalization;

namespace Ebm.Hosting.ErrorHandling;

/// <summary>
/// Last-resort record of a failure that happens before the logging pipeline exists
/// (e.g. invalid configuration). Never throws.
/// </summary>
public static class StartupFailureLog
{
    /// <returns>The path of the written file, or <see langword="null"/> if nothing could be written.</returns>
    public static string? TryWrite(Exception exception, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        string[] candidates =
        [
            directory ?? Path.Combine(AppPaths.DefaultDataRoot, "logs"),
            Path.Combine(Path.GetTempPath(), AppPaths.DefaultFolderName),
        ];

        var stamp = DateTimeOffset.Now;
        var fileName = $"startup-failure-{stamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log";
        var content =
            $"{stamp.ToString("O", CultureInfo.InvariantCulture)} Startup failure{Environment.NewLine}{exception}{Environment.NewLine}";

        foreach (var candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(candidate);
                var path = Path.Combine(candidate, fileName);
                File.AppendAllText(path, content);
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Try the next location.
            }
        }

        return null;
    }
}
