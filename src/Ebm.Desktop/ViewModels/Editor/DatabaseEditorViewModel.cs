using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.Startup;
using Ebm.Desktop.ViewModels.Careers;
using Ebm.Desktop.ViewModels.Chemistry;
using Ebm.Desktop.ViewModels.Clubs;
using Ebm.Desktop.ViewModels.Contracts;
using Ebm.Desktop.ViewModels.Matches;
using Ebm.Desktop.ViewModels.Players;
using Ebm.Desktop.ViewModels.Saves;
using Ebm.Desktop.ViewModels.Scouting;
using Ebm.Desktop.ViewModels.Tactical;
using Ebm.Desktop.ViewModels.Finances;
using Ebm.Desktop.ViewModels.Medical;
using Ebm.Desktop.ViewModels.Training;
using Ebm.Desktop.ViewModels.Transfers;
using Ebm.Domain.Clubs;
using Ebm.Services.Careers;
using Ebm.Services.Chemistry;
using Ebm.Services.Clubs;
using Ebm.Services.Competitions;
using Ebm.Services.Contracts;
using Ebm.Services.Development;
using Ebm.Services.Editing;
using Ebm.Services.Import;
using Ebm.Services.Matches;
using Ebm.Services.Finances;
using Ebm.Services.Medical;
using Ebm.Services.Players;
using Ebm.Services.Saves;
using Ebm.Services.Scouting;
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
    private readonly ClubLockStore? _lockStore;

    [ObservableProperty]
    private string _counts = string.Empty;

    /// <summary>
    /// The locked club: preselected in every screen that works on one club, and remembered between sessions.
    /// <c>null</c> when nothing is locked.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClubLocked), nameof(IsClubUnlocked), nameof(LockedClubName))]
    [NotifyCanExecuteChangedFor(nameof(UnlockClubCommand))]
    private ClubId? _lockedClubId;

    /// <summary>The club picked in the header, locked by <see cref="LockClubCommand"/>.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LockClubCommand))]
    private ClubId? _clubToLock;

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
        ISaveGameService? saves = null,
        IScoutingService? scouting = null,
        IDevelopmentService? development = null,
        IMedicalService? medical = null,
        IFinanceService? finances = null,
        IChemistryService? chemistry = null,
        ClubLockStore? clubLock = null)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(filePicker);
        _editor = editor;
        _lockStore = clubLock;
        _lockedClubId = _clubToLock = clubLock?.Load();
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
        Scouting = scouting is null ? null : new ScoutingViewModel(scouting, Lookups, ReloadAsync);
        Training = development is null ? null : new TrainingViewModel(development, Lookups);
        Medical = medical is null ? null : new MedicalViewModel(medical, Lookups);
        Finances = finances is null ? null : new FinancesViewModel(finances, Lookups);
        DressingRoom = chemistry is null ? null : new DressingRoomViewModel(chemistry, Lookups);
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

    /// <summary>The scouting screen; <c>null</c> without a scouting service (editor-only tests).</summary>
    public ScoutingViewModel? Scouting { get; }

    /// <summary>The training screen; <c>null</c> without a development service (editor-only tests).</summary>
    public TrainingViewModel? Training { get; }

    public MedicalViewModel? Medical { get; }

    public FinancesViewModel? Finances { get; }

    public DressingRoomViewModel? DressingRoom { get; }

    public bool IsClubLocked => LockedClubId is not null;

    public bool IsClubUnlocked => LockedClubId is null;

    public string LockedClubName => LockedClubId is not { } id
        ? string.Empty
        : Lookups.Clubs.Count == 0 ? "…" : Lookups.ClubName(id) is { Length: > 0 } name ? name : "(club not in this database)";

    [RelayCommand(CanExecute = nameof(CanLockClub))]
    private void LockClub() => LockedClubId = ClubToLock;

    private bool CanLockClub() => ClubToLock is not null;

    [RelayCommand(CanExecute = nameof(IsClubLocked))]
    private void UnlockClub() => LockedClubId = null;

    partial void OnLockedClubIdChanged(ClubId? value)
    {
        _lockStore?.Save(value);
        if (value is { } id)
        {
            ClubToLock = id;
            ApplyLockedClub(onlyEmpty: false);
        }
    }

    /// <summary>
    /// Selects the locked club in every one-club screen. After a reload only the screens with no club chosen yet get it,
    /// so a club picked by hand in one tab stays picked; locking a club switches every tab to it.
    /// </summary>
    private void ApplyLockedClub(bool onlyEmpty)
    {
        if (LockedClubId is not { } id || Lookups.Clubs.All(c => c.Id != id))
        {
            return;
        }

        void Apply(ClubId? current, Action<ClubId?> select)
        {
            if (!onlyEmpty || current is null)
            {
                select(id);
            }
        }

        Apply(ClubProfile.ClubId, v => ClubProfile.ClubId = v);
        Apply(Squad.ClubId, v => Squad.ClubId = v);
        Apply(ClubContracts.ClubId, v => ClubContracts.ClubId = v);
        Apply(TransferMarket.ClubId, v => TransferMarket.ClubId = v);
        Apply(Tactics.ClubId, v => Tactics.ClubId = v);
        if (Scouting is not null)
        {
            Apply(Scouting.ClubId, v => Scouting.ClubId = v);
        }

        if (Training is not null)
        {
            Apply(Training.ClubId, v => Training.ClubId = v);
        }

        if (Medical is not null)
        {
            Apply(Medical.ClubId, v => Medical.ClubId = v);
        }

        if (Finances is not null)
        {
            Apply(Finances.ClubId, v => Finances.ClubId = v);
        }

        if (DressingRoom is not null)
        {
            Apply(DressingRoom.ClubId, v => DressingRoom.ClubId = v);
        }

        Career?.PreselectClub(id);
    }

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

        if (Scouting is not null)
        {
            await Scouting.RefreshAsync();
        }

        if (Training is not null)
        {
            await Training.RefreshAsync();
        }

        if (Medical is not null)
        {
            await Medical.RefreshAsync();
        }

        if (Finances is not null)
        {
            await Finances.RefreshAsync();
        }

        if (DressingRoom is not null)
        {
            await DressingRoom.RefreshAsync();
        }

        ApplyLockedClub(onlyEmpty: true);
        OnPropertyChanged(nameof(LockedClubName));

        Counts = $"{snapshot.Countries.Count} countries · {snapshot.Leagues.Count} leagues · {snapshot.Competitions.Count} competitions · " +
            $"{snapshot.Clubs.Count} clubs · {snapshot.Players.Count} players · {snapshot.PlayerContracts.Count} contracts";
    }
}
