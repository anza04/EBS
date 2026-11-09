using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Desktop.Startup;
using Ebm.Services.Editing;
using Ebm.Services.Import;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>The database editor: one page per record type plus the import page, all fed from one snapshot.</summary>
public sealed partial class DatabaseEditorViewModel : ObservableObject
{
    private readonly IDatabaseEditor _editor;

    [ObservableProperty]
    private string _counts = string.Empty;

    public DatabaseEditorViewModel(IDatabaseEditor editor, IDataImportService importer, IFilePicker filePicker)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(filePicker);
        _editor = editor;
        Players = new PlayersPageViewModel(editor, Lookups, ReloadAsync);
        Clubs = new ClubsPageViewModel(editor, Lookups, ReloadAsync);
        Contracts = new ContractsPageViewModel(editor, Lookups, ReloadAsync);
        Leagues = new LeaguesPageViewModel(editor, Lookups, ReloadAsync);
        Competitions = new CompetitionsPageViewModel(editor, Lookups, ReloadAsync);
        Import = new ImportViewModel(importer, filePicker, ReloadAsync);
    }

    public EditorLookups Lookups { get; } = new();

    public PlayersPageViewModel Players { get; }

    public ClubsPageViewModel Clubs { get; }

    public ContractsPageViewModel Contracts { get; }

    public LeaguesPageViewModel Leagues { get; }

    public CompetitionsPageViewModel Competitions { get; }

    public ImportViewModel Import { get; }

    /// <summary>Reloads every page from the database.</summary>
    public async Task ReloadAsync()
    {
        var snapshot = await _editor.LoadAsync();
        Lookups.Refresh(snapshot);
        Players.Refresh(snapshot);
        Clubs.Refresh(snapshot);
        Contracts.Refresh(snapshot);
        Leagues.Refresh(snapshot);
        Competitions.Refresh(snapshot);
        Counts = $"{snapshot.Countries.Count} countries · {snapshot.Leagues.Count} leagues · {snapshot.Competitions.Count} competitions · " +
            $"{snapshot.Clubs.Count} clubs · {snapshot.Players.Count} players · {snapshot.PlayerContracts.Count} contracts";
    }
}
