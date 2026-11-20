using Ebm.Services.Import;

namespace Ebm.Services.Modding;

/// <summary>
/// Writes the game database as an <c>ebm-dataset</c> JSON file in the import format (docs/IMPORT_FORMATS.md), so a
/// world can be edited outside the game and imported again (docs/MODDING.md).
/// </summary>
public interface IDatabaseExportService
{
    /// <summary>Exports the database to <paramref name="path"/> (overwritten) and returns the record counts.</summary>
    Task<ExportSummary> ExportAsync(string path, CancellationToken cancellationToken = default);
}

/// <param name="File">The written file.</param>
/// <param name="Counts">Records per section (<c>countries</c>, <c>players</c> …), in file order.</param>
public sealed record ExportSummary(string File, IReadOnlyList<(string Section, int Count)> Counts)
{
    public int Total => Counts.Sum(c => c.Count);

    public string Summary => string.Join(", ", Counts.Where(c => c.Count > 0).Select(c => $"{c.Count} {c.Section}"));
}

/// <summary>Checks the whole game database for data that breaks the game's rules (docs/MODDING.md §4).</summary>
public interface IDatabaseValidator
{
    Task<ValidationReport> ValidateAsync(CancellationToken cancellationToken = default);
}

public enum IssueSeverity
{
    /// <summary>The game may misbehave: fix it before playing.</summary>
    Error,

    /// <summary>Allowed, but probably not intended.</summary>
    Warning,
}

/// <param name="Area">What the issue is about: "Contracts", "Competitions", "Clubs" …</param>
/// <param name="Subject">The record concerned (a name).</param>
public sealed record ValidationIssue(IssueSeverity Severity, string Area, string Subject, string Message);

public sealed record ValidationReport(IReadOnlyList<ValidationIssue> Issues)
{
    public int Errors => Issues.Count(i => i.Severity == IssueSeverity.Error);

    public int Warnings => Issues.Count(i => i.Severity == IssueSeverity.Warning);

    public bool IsValid => Errors == 0;

    public string Summary => Issues.Count == 0 ? "The database is valid." : $"{Errors} error(s), {Warnings} warning(s).";
}

/// <summary>
/// Community database packages (<c>.ebmpack</c>): a zip with a manifest and import files. Packages are listed from the
/// mods folder, validated without writing anything and installed either as a new game or into the current game, which
/// is protected by an automatic save and restored if the install fails (docs/MODDING.md).
/// Problems the player can fix throw <see cref="Domain.Common.DomainException"/>.
/// </summary>
public interface IModPackageService
{
    /// <summary>The folder scanned for packages.</summary>
    string ModsDirectory { get; }

    /// <summary>Every <c>*.ebmpack</c> in the mods folder, by name; unreadable packages are listed with their problem.</summary>
    Task<IReadOnlyList<ModPackageInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks the manifest and runs the import as a dry run against the current game. Nothing is written.</summary>
    Task<PackageCheck> ValidateAsync(string packagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs a package. <see cref="InstallTarget.NewGame"/> replaces the live game with an empty database first;
    /// <see cref="InstallTarget.CurrentGame"/> saves the live game as a restore point, imports all or nothing and
    /// restores the restore point if the import or the database validation fails.
    /// </summary>
    Task<PackageInstallResult> InstallAsync(string packagePath, InstallTarget target, CancellationToken cancellationToken = default);

    /// <summary>Writes the current database as a package in the mods folder.</summary>
    Task<ModPackageInfo> CreateAsync(PackageManifest manifest, CancellationToken cancellationToken = default);
}

public enum InstallTarget
{
    /// <summary>Start over with only the package's data (existing saves are not touched).</summary>
    NewGame,

    /// <summary>Add the package's records to the current game, updating records with the same ID.</summary>
    CurrentGame,
}

/// <summary>The <c>ebmpack.json</c> manifest of a package (docs/MODDING.md §2).</summary>
/// <param name="Name">1–60 characters; also the file name of a created package.</param>
/// <param name="Files">Import files inside the package, imported together (dependency order is automatic).</param>
/// <param name="MinGameVersion">Oldest game version the package works with, e.g. <c>0.33.0</c>; empty = any.</param>
public sealed record PackageManifest(
    string Name,
    string Author,
    string Version,
    string Description,
    IReadOnlyList<string> Files,
    string MinGameVersion = "");

/// <param name="Manifest">The manifest, or <c>null</c> when it could not be read (see <paramref name="Problem"/>).</param>
public sealed record ModPackageInfo(string FilePath, PackageManifest? Manifest, string? Problem)
{
    public string FileName => Path.GetFileName(FilePath);
}

/// <param name="Problems">
/// Manifest, compatibility and file problems (unreadable or skipped files); the package cannot be installed while
/// there are any. Notes such as ignored unknown fields stay in <see cref="ImportReport.Problems"/>.
/// </param>
/// <param name="Import">The dry-run import, or <c>null</c> when the manifest problems prevented it.</param>
public sealed record PackageCheck(PackageManifest? Manifest, IReadOnlyList<string> Problems, ImportReport? Import)
{
    public bool CanInstall => Problems.Count == 0 && Import is { Rejected: 0 } && Import.Records.Count > 0;

    public string Summary =>
        Problems.Count > 0 ? string.Join(" ", Problems)
        : Import is null ? "Not checked."
        : Import.Records.Count == 0 ? "The package contains no records."
        : Import.Rejected > 0 ? $"{Import.Rejected} record(s) would be rejected; fix the package before installing it."
        : $"Valid: {Import.Added} new and {Import.Updated} updated record(s).";
}

/// <param name="Installed">Whether the package's records are now in the game.</param>
/// <param name="RestorePoint">The automatic save made before installing into the current game.</param>
/// <param name="Validation">Database validation after the install.</param>
public sealed record PackageInstallResult(bool Installed, string Message, ImportReport? Import, string? RestorePoint, ValidationReport? Validation);
