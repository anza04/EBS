using Ebm.DataImport.Sources;
using Ebm.DataImport.Validation;
using Ebm.Services.Data;
using Ebm.Services.Import;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ebm.DataImport;

/// <summary>Reads import files, validates every record and writes the accepted ones in one unit of work.</summary>
internal sealed class DataImportService : IDataImportService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ImportLogWriter _logWriter;
    private readonly ILogger<DataImportService> _logger;

    public DataImportService(IServiceScopeFactory scopes, ImportLogWriter logWriter, ILogger<DataImportService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(logWriter);
        ArgumentNullException.ThrowIfNull(logger);
        _scopes = scopes;
        _logWriter = logWriter;
        _logger = logger;
    }

    public async Task<ImportReport> ImportAsync(IReadOnlyList<string> files, ImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(options);
        _logger.LogInformation("Import of {FileCount} file(s) started: {Files} ({Options})", files.Count, string.Join(", ", files), options);

        var records = new List<RawRecord>();
        var problems = new List<string>();
        foreach (var file in files)
        {
            await SourceReader.ReadAsync(file, records, problems, cancellationToken).ConfigureAwait(false);
        }

        using var scope = _scopes.CreateScope();
        var session = await ImportSession.LoadAsync(scope.ServiceProvider, options.Mode, cancellationToken).ConfigureAwait(false);

        // Stable sort: dependency order between record types, file order within a type.
        var results = records.OrderBy(r => r.Kind).Select(session.Process).ToList();
        var committed = false;
        var accepted = results.Any(r => r.Outcome != ImportOutcome.Rejected);
        var rejected = results.Any(r => r.Outcome == ImportOutcome.Rejected);
        if (options.DryRun)
        {
            problems.Add("Validation only (dry run): nothing was saved.");
        }
        else if (options.AllOrNothing && rejected)
        {
            problems.Add("Some records were rejected and 'all or nothing' is on: nothing was saved.");
        }
        else if (accepted)
        {
            try
            {
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                committed = true;
            }
            catch (DataConstraintException ex)
            {
                _logger.LogWarning(ex, "Import save rejected by the database");
                problems.Add($"{ex.Message} Nothing was saved.");
            }
        }

        var report = new ImportReport(results, problems, committed, null);
        report = report with { LogFile = _logWriter.TryWrite(files, options, report) };
        _logger.LogInformation("Import finished: {Summary} Log: {LogFile}", report.Summary, report.LogFile ?? "(not written)");
        return report;
    }
}
