using System.Globalization;
using System.Text;
using Ebm.Services.Import;
using Microsoft.Extensions.Logging;

namespace Ebm.DataImport;

/// <summary>Writes one plain-text log per import with the outcome of every record.</summary>
internal sealed class ImportLogWriter
{
    private readonly DataImportOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ImportLogWriter> _logger;

    public ImportLogWriter(DataImportOptions options, TimeProvider time, ILogger<ImportLogWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <returns>The log path, or <c>null</c> if it could not be written (the import itself is unaffected).</returns>
    public string? TryWrite(IReadOnlyList<string> files, ImportOptions options, ImportReport report)
    {
        var now = _time.GetLocalNow();
        var path = Path.Combine(_options.LogDirectory, $"import-{now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)}.log");
        var text = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Import {now:yyyy-MM-dd HH:mm:ss zzz}")
            .AppendLine(CultureInfo.InvariantCulture, $"Mode: {options.Mode}, dry run: {options.DryRun}, all or nothing: {options.AllOrNothing}")
            .AppendLine("Files:");
        foreach (var file in files)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {file}");
        }

        text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"Result: {report.Summary}");
        if (report.Problems.Count > 0)
        {
            text.AppendLine().AppendLine("File problems:");
            foreach (var problem in report.Problems)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"  {problem}");
            }
        }

        text.AppendLine().AppendLine("Records:");
        foreach (var r in report.Records)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {r.Outcome,-8} {r.Kind,-11} {r.Location}  {r.Label}  {r.Id}");
            foreach (var message in r.Messages)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"           - {message}");
            }
        }

        try
        {
            Directory.CreateDirectory(_options.LogDirectory);
            File.WriteAllText(path, text.ToString(), Encoding.UTF8);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Import log {Path} could not be written", path);
            return null;
        }
    }
}

/// <param name="LogDirectory">Folder that receives one log file per import.</param>
public sealed record DataImportOptions(string LogDirectory);
