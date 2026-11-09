using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Desktop.Startup;
using Ebm.Desktop.ViewModels.Clubs;
using Ebm.Desktop.ViewModels.Players;
using Ebm.Services.Clubs;
using Ebm.Services.Editing;
using Ebm.Services.Import;
using Ebm.Services.Players;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>The database editor: one page per record type, the import page, the player profile and comparison screens and the club profile and squad screens.</summary>
public sealed partial class DatabaseEditorViewModel : ObservableObject
{
    private readonly IDatabaseEditor _editor;

    [ObservableProperty]
    private string _counts = string.Empty;

    public DatabaseEditorViewModel(IDatabaseEditor editor, IDataImportService importer, IFilePicker filePicker, IPlayerProfileService profiles, IClubService clubs)
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
        Profile = new PlayerProfileViewModel(profiles, Lookups);
        Comparison = new PlayerComparisonViewModel(profiles, Lookups);
        ClubProfile = new ClubProfileViewModel(clubs, Lookups);
        Squad = new SquadViewModel(clubs, Lookups, ReloadAsync);
    }

    public EditorLookups Lookups { get; } = new();

    public PlayersPageViewModel Players { get; }

    public ClubsPageViewModel Clubs { get; }

    public ContractsPageViewModel Contracts { get; }

    public LeaguesPageViewModel Leagues { get; }

    public CompetitionsPageViewModel Competitions { get; }

    public ImportViewModel Import { get; }

    public PlayerProfileViewModel Profile { get; }

    public PlayerComparisonViewModel Comparison { get; }

    public ClubProfileViewModel ClubProfile { get; }

    public SquadViewModel Squad { get; }

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
        await Profile.RefreshAsync();
        await Comparison.RefreshAsync();
        await ClubProfile.RefreshAsync();
        await Squad.RefreshAsync();
        Counts = $"{snapshot.Countries.Count} countries · {snapshot.Leagues.Count} leagues · {snapshot.Competitions.Count} competitions · " +
            $"{snapshot.Clubs.Count} clubs · {snapshot.Players.Count} players · {snapshot.PlayerContracts.Count} contracts";
    }
}
