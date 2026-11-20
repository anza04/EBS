using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Ebm.Domain.Common;
using Ebm.Services.Import;
using Ebm.Services.Modding;
using Ebm.Services.Saves;
using Microsoft.Extensions.Logging;

namespace Ebm.DataImport.Modding;

/// <summary>Community database packages (docs/MODDING.md). Package files are read from the zip, never executed.</summary>
internal sealed class ModPackageService(
    DataImportOptions options,
    IDataImportService import,
    IDatabaseExportService export,
    IDatabaseValidator validator,
    ISaveGameService saves,
    TimeProvider time,
    ILogger<ModPackageService> logger) : IModPackageService
{
    public const string Extension = ".ebmpack";
    public const string ManifestFile = "ebmpack.json";
    public const string Format = "ebm-package";
    private const long MaxFileBytes = 200L * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public string ModsDirectory => options.ModsDirectory;

    /// <summary>Version of this game, compared with a package's <c>minGameVersion</c>.</summary>
    public static Version GameVersion => typeof(ModPackageService).Assembly.GetName().Version ?? new Version(0, 0);

    public Task<IReadOnlyList<ModPackageInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ModsDirectory);
        IReadOnlyList<ModPackageInfo> packages = Directory.EnumerateFiles(ModsDirectory, "*" + Extension)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                try
                {
                    using var zip = ZipFile.OpenRead(path);
                    var (manifest, problem) = ReadManifest(zip);
                    return new ModPackageInfo(path, manifest, problem);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    return new ModPackageInfo(path, null, $"The package cannot be opened ({ex.Message}).");
                }
            })
            .ToList();
        return Task.FromResult(packages);
    }

    public async Task<PackageCheck> ValidateAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        using var unpacked = Unpack(packagePath);
        if (unpacked.Problems.Count > 0)
        {
            return new PackageCheck(unpacked.Manifest, unpacked.Problems, null);
        }

        var report = await import.ImportAsync(unpacked.Files, new ImportOptions(ImportMode.AddOrUpdate, DryRun: true), cancellationToken).ConfigureAwait(false);
        return new PackageCheck(unpacked.Manifest, BlockingProblems(report), report);
    }

    public async Task<PackageInstallResult> InstallAsync(string packagePath, InstallTarget target, CancellationToken cancellationToken = default)
    {
        using var unpacked = Unpack(packagePath);
        if (unpacked.Problems.Count > 0)
        {
            return new PackageInstallResult(false, string.Join(" ", unpacked.Problems), null, null, null);
        }

        var manifest = unpacked.Manifest!;
        var before = await validator.ValidateAsync(cancellationToken).ConfigureAwait(false);

        // The live game is copied first, so whatever happens next it can be restored (docs/MODDING.md §5).
        var restorePoint = (await saves.SaveAsync(RestorePointName(manifest.Name), overwrite: true, cancellationToken).ConfigureAwait(false)).Name;
        if (target == InstallTarget.NewGame)
        {
            await saves.NewGameAsync(cancellationToken).ConfigureAwait(false);
            before = new ValidationReport([]);
        }

        var mode = target == InstallTarget.NewGame ? ImportMode.AddNew : ImportMode.AddOrUpdate;
        var report = await import.ImportAsync(unpacked.Files, new ImportOptions(mode, AllOrNothing: true), cancellationToken).ConfigureAwait(false);
        var blocking = BlockingProblems(report);
        if (!report.Committed || blocking.Count > 0)
        {
            string? kept = restorePoint;
            if (target == InstallTarget.CurrentGame && !report.Committed)
            {
                // Nothing was written: the restore point is not needed.
                await saves.DeleteAsync(restorePoint, cancellationToken).ConfigureAwait(false);
                kept = null;
            }
            else
            {
                await RestoreAsync(restorePoint, cancellationToken).ConfigureAwait(false);
            }

            var reason = blocking.Count > 0 ? string.Join(" ", blocking)
                : report.Rejected > 0 ? $"{report.Rejected} record(s) were rejected"
                : "the package contains no records";
            return new PackageInstallResult(false, $"{manifest.Name} was not installed: {reason}. The game is unchanged.", report, kept, null);
        }

        var after = await validator.ValidateAsync(cancellationToken).ConfigureAwait(false);
        var known = before.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(Key).ToHashSet(StringComparer.Ordinal);
        var newErrors = after.Issues.Where(i => i.Severity == IssueSeverity.Error && !known.Contains(Key(i))).ToList();
        if (target == InstallTarget.CurrentGame && newErrors.Count > 0)
        {
            await RestoreAsync(restorePoint, cancellationToken).ConfigureAwait(false);
            return new PackageInstallResult(false,
                $"{manifest.Name} was undone because it breaks the game: {newErrors[0].Subject}: {newErrors[0].Message} The game was restored from '{restorePoint}'.",
                report, restorePoint, after);
        }

        logger.LogInformation("Package {Package} {Version} installed ({Target}): {Summary}; restore point {RestorePoint}",
            manifest.Name, manifest.Version, target, report.Summary, restorePoint);
        var message = target == InstallTarget.NewGame
            ? $"{manifest.Name} {manifest.Version} installed as a new game: {report.Added} records. Start a career from the Career screen."
            : $"{manifest.Name} {manifest.Version} installed: {report.Added} new and {report.Updated} updated records. Your previous game is saved as '{restorePoint}'.";
        return new PackageInstallResult(true, message, report, restorePoint, after);
    }

    public async Task<ModPackageInfo> CreateAsync(PackageManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var name = manifest.Name.Trim();
        if (name.Length is 0 or > 60)
        {
            throw new DomainValidationException("Give the package a name of 1 to 60 characters.");
        }

        Directory.CreateDirectory(ModsDirectory);
        var path = Path.Combine(ModsDirectory, SafeFileName(name) + Extension);
        if (File.Exists(path))
        {
            throw new DomainValidationException($"A package file called '{Path.GetFileName(path)}' already exists in the mods folder. Choose another name or delete it.");
        }

        var dataset = Path.Combine(Path.GetTempPath(), $"ebm-export-{Guid.NewGuid():N}.json");
        try
        {
            await export.ExportAsync(dataset, cancellationToken).ConfigureAwait(false);
            var written = manifest with
            {
                Name = name,
                Author = manifest.Author.Trim(),
                Version = string.IsNullOrWhiteSpace(manifest.Version) ? "1.0" : manifest.Version.Trim(),
                Description = manifest.Description.Trim(),
                Files = ["dataset.json"],
                MinGameVersion = string.IsNullOrWhiteSpace(manifest.MinGameVersion) ? GameVersion.ToString(3) : manifest.MinGameVersion.Trim(),
            };
            var temporary = path + ".tmp";
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(dataset, "dataset.json", CompressionLevel.Optimal);
                var entry = zip.CreateEntry(ManifestFile);
                await using var stream = entry.Open();
                await JsonSerializer.SerializeAsync(stream, ManifestFileModel.From(written), Json, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path);
            logger.LogInformation("Package {Package} created: {File}", name, path);
            return new ModPackageInfo(path, written, null);
        }
        finally
        {
            File.Delete(dataset);
        }
    }

    /// <summary>Loads the restore point over the live game (which also reloads the career date).</summary>
    private async Task RestoreAsync(string restorePoint, CancellationToken cancellationToken)
    {
        await saves.LoadAsync(restorePoint, cancellationToken).ConfigureAwait(false);
        logger.LogWarning("Package install failed; game restored from {RestorePoint}", restorePoint);
    }

    /// <summary>File problems that make a package unusable; notes about ignored fields or sections are not blocking.</summary>
    private static List<string> BlockingProblems(ImportReport report) =>
        report.Problems.Where(p => !p.Contains("ignored", StringComparison.Ordinal) && !p.StartsWith("Validation only", StringComparison.Ordinal)).ToList();

    private static string Key(ValidationIssue issue) => $"{issue.Area}|{issue.Subject}|{issue.Message}";

    private string RestorePointName(string package)
    {
        var stamp = time.GetLocalNow().ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);
        var label = new string([.. package.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')]).Trim();
        label = label.Length > 25 ? label[..25].Trim() : label;
        return $"Before {(label.Length > 0 ? label : "package")} {stamp}";
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Select(c => invalid.Contains(c) ? '_' : c)]);
    }

    private static (PackageManifest? Manifest, string? Problem) ReadManifest(ZipArchive zip)
    {
        var entry = zip.GetEntry(ManifestFile);
        if (entry is null)
        {
            return (null, $"The package has no {ManifestFile} manifest.");
        }

        try
        {
            using var stream = entry.Open();
            var model = JsonSerializer.Deserialize<ManifestFileModel>(stream, Json);
            if (model is null)
            {
                return (null, $"{ManifestFile} is empty.");
            }

            if (!string.Equals(model.Format, Format, StringComparison.Ordinal))
            {
                return (null, $"{ManifestFile}: 'format' must be \"{Format}\".");
            }

            if (model.FormatVersion != 1)
            {
                return (null, $"{ManifestFile}: unsupported formatVersion {model.FormatVersion} (expected 1). Update the game.");
            }

            if (string.IsNullOrWhiteSpace(model.Name) || model.Name.Trim().Length > 60)
            {
                return (null, $"{ManifestFile}: 'name' must have 1 to 60 characters.");
            }

            if (model.Files is not { Count: > 0 })
            {
                return (null, $"{ManifestFile}: 'files' must list at least one .json or .csv file.");
            }

            return (model.ToManifest(), null);
        }
        catch (JsonException ex)
        {
            return (null, $"{ManifestFile} is not valid JSON ({ex.Message}).");
        }
    }

    private static Unpacked Unpack(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var unpacked = new Unpacked();
        if (!File.Exists(packagePath))
        {
            unpacked.Problems.Add($"The package '{packagePath}' does not exist.");
            return unpacked;
        }

        try
        {
            using var zip = ZipFile.OpenRead(packagePath);
            var (manifest, problem) = ReadManifest(zip);
            unpacked.Manifest = manifest;
            if (problem is not null)
            {
                unpacked.Problems.Add(problem);
                return unpacked;
            }

            if (manifest!.MinGameVersion is { Length: > 0 } minimum)
            {
                if (!Version.TryParse(minimum, out var required))
                {
                    unpacked.Problems.Add($"{ManifestFile}: minGameVersion '{minimum}' is not a version such as 0.33.0.");
                }
                else if (Normalise(required) > Normalise(GameVersion))
                {
                    unpacked.Problems.Add($"The package needs game version {minimum} or later (this game is {GameVersion.ToString(3)}).");
                }
            }

            Directory.CreateDirectory(unpacked.Folder);
            foreach (var file in manifest.Files)
            {
                // Only plain file names inside the package: no folders, no paths that leave the extraction folder.
                var name = file.Replace('\\', '/');
                var extension = Path.GetExtension(name).ToLowerInvariant();
                if (name.Contains("..", StringComparison.Ordinal) || name.StartsWith('/') || extension is not (".json" or ".csv"))
                {
                    unpacked.Problems.Add($"{ManifestFile}: '{file}' is not allowed. List .json or .csv files inside the package.");
                    continue;
                }

                var entry = zip.GetEntry(name);
                if (entry is null)
                {
                    unpacked.Problems.Add($"{ManifestFile} lists '{file}', which is not in the package.");
                    continue;
                }

                if (entry.Length > MaxFileBytes)
                {
                    unpacked.Problems.Add($"'{file}' is larger than {MaxFileBytes / 1024 / 1024} MB.");
                    continue;
                }

                // Keep the file name (CSV record types come from it) but put each file in its own folder.
                var target = Path.Combine(unpacked.Folder, unpacked.Files.Count.ToString(CultureInfo.InvariantCulture), Path.GetFileName(name));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target);
                unpacked.Files.Add(target);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            unpacked.Problems.Add($"The package cannot be opened ({ex.Message}).");
        }

        return unpacked;
    }

    private static Version Normalise(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private sealed class Unpacked : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"ebm-package-{Guid.NewGuid():N}");

        public PackageManifest? Manifest { get; set; }

        public List<string> Problems { get; } = [];

        public List<string> Files { get; } = [];

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Folder))
                {
                    Directory.Delete(Folder, recursive: true);
                }
            }
            catch (IOException)
            {
                // A temporary folder that cannot be deleted now is left to the system's temp cleanup.
            }
        }
    }

    /// <summary>The manifest as stored in the package (docs/MODDING.md §2).</summary>
    private sealed class ManifestFileModel
    {
        public string Format { get; set; } = string.Empty;

        public int FormatVersion { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Author { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string MinGameVersion { get; set; } = string.Empty;

        public List<string>? Files { get; set; }

        public static ManifestFileModel From(PackageManifest manifest) => new()
        {
            Format = ModPackageService.Format,
            FormatVersion = 1,
            Name = manifest.Name,
            Author = manifest.Author,
            Version = manifest.Version,
            Description = manifest.Description,
            MinGameVersion = manifest.MinGameVersion,
            Files = [.. manifest.Files],
        };

        public PackageManifest ToManifest() =>
            new(Name.Trim(), Author.Trim(), Version.Trim(), Description.Trim(), Files!, MinGameVersion.Trim());
    }
}
