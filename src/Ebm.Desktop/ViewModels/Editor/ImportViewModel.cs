using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.Startup;
using Ebm.Services.Import;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>One line of the import result list.</summary>
public sealed record ImportResultRow(string Outcome, string Kind, string Location, string Label, string Messages);

/// <summary>Imports CSV/JSON datasets and shows the per-record outcome.</summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly IDataImportService _importer;
    private readonly IFilePicker _filePicker;
    private readonly Func<Task> _reload;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private IReadOnlyList<string> _files = [];

    [ObservableProperty]
    private bool _updateExisting;

    [ObservableProperty]
    private bool _dryRun;

    [ObservableProperty]
    private bool _allOrNothing;

    [ObservableProperty]
    private string? _summary;

    [ObservableProperty]
    private string? _logFile;

    public ImportViewModel(IDataImportService importer, IFilePicker filePicker, Func<Task> reload)
    {
        _importer = importer;
        _filePicker = filePicker;
        _reload = reload;
    }

    public ObservableCollection<string> Problems { get; } = [];

    public ObservableCollection<ImportResultRow> Results { get; } = [];

    [RelayCommand]
    private void ChooseFiles()
    {
        if (_filePicker.PickImportFiles() is { Count: > 0 } picked)
        {
            Files = picked;
        }
    }

    private bool CanImport() => Files.Count > 0;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        var options = new ImportOptions(UpdateExisting ? ImportMode.AddOrUpdate : ImportMode.AddNew, DryRun, AllOrNothing);
        var report = await _importer.ImportAsync(Files, options);
        Problems.Clear();
        foreach (var problem in report.Problems)
        {
            Problems.Add(problem);
        }

        Results.Clear();
        foreach (var r in report.Records)
        {
            Results.Add(new ImportResultRow(r.Outcome.ToString(), r.Kind.ToString(), r.Location, r.Label, string.Join(" ", r.Messages)));
        }

        Summary = report.Summary;
        LogFile = report.LogFile;
        if (report.Committed)
        {
            await _reload();
        }
    }
}
