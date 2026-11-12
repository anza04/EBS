using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Desktop.Startup;
using Ebm.Desktop.ViewModels.Careers;
using Ebm.Desktop.ViewModels.Clubs;
using Ebm.Desktop.ViewModels.Contracts;
using Ebm.Desktop.ViewModels.Matches;
using Ebm.Desktop.ViewModels.Players;
using Ebm.Desktop.ViewModels.Saves;
using Ebm.Desktop.ViewModels.Tactical;
using Ebm.Desktop.ViewModels.Transfers;
using Ebm.Services.Careers;
using Ebm.Services.Clubs;
using Ebm.Services.Competitions;
using Ebm.Services.Contracts;
using Ebm.Services.Editing;
using Ebm.Services.Import;
using Ebm.Services.Matches;
using Ebm.Services.Players;
using Ebm.Services.Saves;
using Ebm.Services.Tactics;
using Ebm.Services.Transfers;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>
/// The database editor: one page per record type, the import page, the player profile and comparison screens, the
/// club profile and squad screens, the club contracts screen, the transfer market, the tactics screen, the match centre and the career screen.
/// </summary>
public sealed partial class DatabaseEditorViewModel : ObservableObject
{
    private readonly IDatabaseEditor _editor;

    [ObservableProperty]
    private string _counts = string.Empty;

    public DatabaseEditorViewModel(
        IDatabaseEditor editor,
        IDataImportService importer,
        IFilePicker filePicker,
        IPlayerProfileService profiles,
        IClubService clubs,
        IContractService contracts,
        ITransferService transfers,
        ITacticsService tactics,
        IMatchService matches,
        ICompetitionService competitions,
        ICareerService? careers = null,
        ISaveGameService? saves = null)
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
        Profile = new PlayerProfileViewModel(profiles, contracts, Lookups);
        Comparison = new PlayerComparisonViewModel(profiles, Lookups);
        ClubProfile = new ClubProfileViewModel(clubs, Lookups);
        Squad = new SquadViewModel(clubs, Lookups, ReloadAsync);
        ClubContracts = new ClubContractsViewModel(contracts, Lookups, ReloadAsync);
        TransferMarket = new TransferMarketViewModel(transfers, contracts, Lookups, ReloadAsync);
        Tactics = new TacticsViewModel(tactics, Lookups);
        Matches = new MatchCenterViewModel(matches, competitions, ReloadAsync);
        Career = careers is null ? null : new CareerViewModel(careers, competitions, Lookups, ReloadAsync);
        Saves = saves is null ? null : new SaveGamesViewModel(saves, ReloadAsync);
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

    public ClubContractsViewModel ClubContracts { get; }

    public TransferMarketViewModel TransferMarket { get; }

    public TacticsViewModel Tactics { get; }

    public MatchCenterViewModel Matches { get; }

    /// <summary>The career screen; <c>null</c> when no career service is available (editor-only tests).</summary>
    public CareerViewModel? Career { get; }

    /// <summary>The saved-games screen; <c>null</c> without a save service (editor-only tests).</summary>
    public SaveGamesViewModel? Saves { get; }

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
        await ClubContracts.RefreshAsync();
        await TransferMarket.RefreshAsync();
        await Tactics.RefreshAsync();
        await Matches.RefreshAsync();
        if (Career is not null)
        {
            await Career.RefreshAsync();
        }

        if (Saves is not null)
        {
            await Saves.RefreshAsync();
        }

        Counts = $"{snapshot.Countries.Count} countries · {snapshot.Leagues.Count} leagues · {snapshot.Competitions.Count} competitions · " +
            $"{snapshot.Clubs.Count} clubs · {snapshot.Players.Count} players · {snapshot.PlayerContracts.Count} contracts";
    }
}
