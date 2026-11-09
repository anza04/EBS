namespace Ebm.Services.Import;

/// <summary>Imports datasets (CSV or JSON files) into the game database.</summary>
public interface IDataImportService
{
    /// <summary>
    /// Reads, validates and imports the records of <paramref name="files"/>. Records are processed in dependency
    /// order (countries first, contracts last), so one import can contain a whole dataset spread over several files.
    /// </summary>
    /// <param name="files">CSV files named after their record type (e.g. <c>players.csv</c>) and/or JSON dataset files.</param>
    /// <param name="options">Import mode and commit behaviour.</param>
    /// <param name="cancellationToken">Cancels the import; nothing is written.</param>
    /// <returns>The outcome of every record. The report is also written to the import log.</returns>
    Task<ImportReport> ImportAsync(IReadOnlyList<string> files, ImportOptions options, CancellationToken cancellationToken = default);
}

/// <summary>What happens to a record whose ID already exists in the database.</summary>
public enum ImportMode
{
    /// <summary>Only new records are added; a record that already exists is rejected as a duplicate.</summary>
    AddNew,

    /// <summary>Explicit update: records whose ID exists replace the stored values; new records are added.</summary>
    AddOrUpdate,
}

/// <param name="Mode">Add new records only, or explicitly update existing ones.</param>
/// <param name="DryRun">Validate and report only; write nothing.</param>
/// <param name="AllOrNothing">Write nothing if any record is rejected.</param>
public sealed record ImportOptions(ImportMode Mode = ImportMode.AddNew, bool DryRun = false, bool AllOrNothing = false);

/// <summary>Record types an import can contain, in the order they are processed.</summary>
public enum ImportRecordKind
{
    Country,
    Arena,
    Club,
    League,
    Division,
    Competition,
    Season,
    Participant,
    Player,
    Contract,
}

public enum ImportOutcome
{
    Added,
    Updated,
    Rejected,
}

/// <summary>The result for one record of an import file.</summary>
/// <param name="Source">File name the record came from.</param>
/// <param name="Position">CSV line number or 1-based index in the JSON section.</param>
/// <param name="Kind">Record type.</param>
/// <param name="Id">ID of the record, when it could be read or was assigned.</param>
/// <param name="Label">Human-readable name of the record (e.g. the player's name).</param>
/// <param name="Outcome">Added, updated or rejected.</param>
/// <param name="Messages">Why the record was rejected, or notes such as an assigned ID.</param>
public sealed record ImportRecordResult(
    string Source,
    int Position,
    ImportRecordKind Kind,
    Guid? Id,
    string Label,
    ImportOutcome Outcome,
    IReadOnlyList<string> Messages)
{
    public string Location => $"{Source}:{Position}";
}

/// <summary>Outcome of an import.</summary>
/// <param name="Records">Per-record results in processing order.</param>
/// <param name="Problems">File-level problems: unreadable or malformed files, unknown record types or columns.</param>
/// <param name="Committed">Whether the accepted records were written to the database.</param>
/// <param name="LogFile">Path of the import log, or <c>null</c> when it could not be written.</param>
public sealed record ImportReport(
    IReadOnlyList<ImportRecordResult> Records,
    IReadOnlyList<string> Problems,
    bool Committed,
    string? LogFile)
{
    public int Added => Count(ImportOutcome.Added);

    public int Updated => Count(ImportOutcome.Updated);

    public int Rejected => Count(ImportOutcome.Rejected);

    public string Summary =>
        $"{Added} added, {Updated} updated, {Rejected} rejected, {Problems.Count} file problem(s); " +
        (Committed ? "changes saved." : "nothing saved.");

    private int Count(ImportOutcome outcome) => Records.Count(r => r.Outcome == outcome);
}
