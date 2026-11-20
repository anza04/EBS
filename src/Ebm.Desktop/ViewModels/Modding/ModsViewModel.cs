using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.Startup;
using Ebm.Domain.Common;
using Ebm.Services.Import;
using Ebm.Services.Modding;

namespace Ebm.Desktop.ViewModels.Modding;

/// <summary>
/// The Mods screen (Step 33, docs/MODDING.md): community packages in the mods folder (check, install as a new game or
/// into the current game), creating a package or a dataset file from the current game, and validating the database.
/// </summary>
public sealed partial class ModsViewModel : ObservableObject
{
    private readonly IModPackageService _packages;
    private readonly IDatabaseExportService _export;
    private readonly IDatabaseValidator _validator;
    private readonly IFilePicker _files;
    private readonly Func<Task> _reloadAll;

    [ObservableProperty]
    private IReadOnlyList<PackageRow> _packageRows = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckPackageCommand), nameof(InstallAsNewGameCommand), nameof(InstallIntoCurrentGameCommand))]
    private PackageRow? _selectedPackage;

    /// <summary>Rejected records of the last check or install, and the database issues of the last validation.</summary>
    [ObservableProperty]
    private IReadOnlyList<IssueRow> _issues = [];

    [ObservableProperty]
    private string _issuesTitle = "Problems";

    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private string _newAuthor = string.Empty;

    [ObservableProperty]
    private string _newVersion = "1.0";

    [ObservableProperty]
    private string _newDescription = string.Empty;

    /// <summary>Install as a new game replaces the game in progress: the button asks for a second click.</summary>
    [ObservableProperty]
    private bool _confirmNewGame;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private bool _isBusy;

    public ModsViewModel(IModPackageService packages, IDatabaseExportService export, IDatabaseValidator validator, IFilePicker files, Func<Task> reloadAll)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(reloadAll);
        _packages = packages;
        _export = export;
        _validator = validator;
        _files = files;
        _reloadAll = reloadAll;
    }

    public string Folder => _packages.ModsDirectory;

    public string NewGameButtonText => ConfirmNewGame ? "Click again to replace the game" : "Install as a new game";

    public async Task RefreshAsync()
    {
        var keep = SelectedPackage?.FilePath;
        PackageRows = [.. (await _packages.ListAsync()).Select(p => new PackageRow(p))];
        SelectedPackage = PackageRows.FirstOrDefault(p => p.FilePath == keep);
    }

    partial void OnSelectedPackageChanged(PackageRow? value) => ConfirmNewGame = false;

    partial void OnConfirmNewGameChanged(bool value) => OnPropertyChanged(nameof(NewGameButtonText));

    private bool HasUsablePackage => SelectedPackage is { Problem: null };

    [RelayCommand(CanExecute = nameof(HasUsablePackage))]
    private Task CheckPackageAsync() => RunAsync(async () =>
    {
        var check = await _packages.ValidateAsync(SelectedPackage!.FilePath);
        ShowImport(check.Import, check.Problems);
        return (check.Summary, !check.CanInstall);
    });

    [RelayCommand(CanExecute = nameof(HasUsablePackage))]
    private Task InstallAsNewGameAsync()
    {
        if (!ConfirmNewGame)
        {
            ConfirmNewGame = true;
            Message = "Installing as a new game replaces the game in progress (it is saved first as a restore point). Click again to go ahead.";
            IsError = false;
            return Task.CompletedTask;
        }

        ConfirmNewGame = false;
        return InstallAsync(InstallTarget.NewGame);
    }

    [RelayCommand(CanExecute = nameof(HasUsablePackage))]
    private Task InstallIntoCurrentGameAsync() => InstallAsync(InstallTarget.CurrentGame);

    [RelayCommand]
    private Task CreatePackageAsync() => RunAsync(async () =>
    {
        var info = await _packages.CreateAsync(new PackageManifest(NewName, NewAuthor, NewVersion, NewDescription, []));
        await RefreshAsync();
        SelectedPackage = PackageRows.FirstOrDefault(p => p.FilePath == info.FilePath);
        return ($"Package created: {info.FileName}. Share the file; others put it in their mods folder.", false);
    });

    [RelayCommand]
    private Task ExportDatasetAsync() => RunAsync(async () =>
    {
        if (_files.PickExportFile("ebm-database.json") is not { } path)
        {
            return ("Export cancelled.", false);
        }

        var summary = await _export.ExportAsync(path);
        return ($"Exported {summary.Summary} to {summary.File}. It can be edited and imported again (Import tab).", false);
    });

    [RelayCommand]
    private Task ValidateDatabaseAsync() => RunAsync(async () =>
    {
        var report = await _validator.ValidateAsync();
        IssuesTitle = "Database validation";
        Issues = [.. report.Issues.Select(i => new IssueRow(i.Severity == IssueSeverity.Error ? "Error" : "Warning", i.Area, i.Subject, i.Message))];
        return (report.Summary, !report.IsValid);
    });

    [RelayCommand]
    private Task AddPackageAsync() => RunAsync(async () =>
    {
        if (_files.PickPackageFiles() is not { Count: > 0 } chosen)
        {
            return ("No package added.", false);
        }

        Directory.CreateDirectory(Folder);
        foreach (var file in chosen)
        {
            File.Copy(file, Path.Combine(Folder, Path.GetFileName(file)), overwrite: true);
        }

        await RefreshAsync();
        return ($"{chosen.Count} package(s) added to the mods folder. Check one before installing it.", false);
    });

    [RelayCommand]
    private Task RefreshListAsync() => RunAsync(async () =>
    {
        await RefreshAsync();
        return ($"{PackageRows.Count} package(s) in the mods folder.", false);
    });

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Folder}\"") { UseShellExecute = true });
    }

    private Task InstallAsync(InstallTarget target) => RunAsync(async () =>
    {
        var result = await _packages.InstallAsync(SelectedPackage!.FilePath, target);
        ShowImport(result.Import, []);
        if (result.Installed && result.Validation is { Issues.Count: > 0 } validation)
        {
            IssuesTitle = "Database validation after the install";
            Issues = [.. validation.Issues.Select(i => new IssueRow(i.Severity == IssueSeverity.Error ? "Error" : "Warning", i.Area, i.Subject, i.Message))];
        }

        await _reloadAll();
        return (result.Message, !result.Installed);
    });

    private void ShowImport(ImportReport? report, IReadOnlyList<string> problems)
    {
        IssuesTitle = "Package check";
        Issues =
        [
            .. problems.Select(p => new IssueRow("Error", "Package", string.Empty, p)),
            .. (report?.Records ?? []).Where(r => r.Outcome == ImportOutcome.Rejected)
                .Select(r => new IssueRow("Rejected", r.Kind.ToString(), $"{r.Label} ({r.Location})", string.Join(" ", r.Messages))),
        ];
    }

    private async Task RunAsync(Func<Task<(string Text, bool Error)>> action)
    {
        IsBusy = true;
        try
        {
            (Message, IsError) = await action();
        }
        catch (DomainException ex)
        {
            (Message, IsError) = (ex.Message, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            (Message, IsError) = ($"A file could not be read or written: {ex.Message}", true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>A package in the mods folder.</summary>
public sealed record PackageRow(ModPackageInfo Info)
{
    public string FilePath => Info.FilePath;

    public string Name => Info.Manifest?.Name ?? Info.FileName;

    public string Author => Info.Manifest?.Author ?? string.Empty;

    public string Version => Info.Manifest?.Version ?? string.Empty;

    public string Description => Info.Manifest?.Description ?? string.Empty;

    public string? Problem => Info.Problem;

    public string Status => Problem ?? (Info.Manifest!.MinGameVersion is { Length: > 0 } min ? $"Needs game {min} or later" : "Ready");
}

public sealed record IssueRow(string Severity, string Area, string Subject, string Message);
