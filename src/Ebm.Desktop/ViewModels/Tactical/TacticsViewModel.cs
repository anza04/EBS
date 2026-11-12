using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;
using Ebm.Domain.Tactics;
using Ebm.Services.Data;
using Ebm.Services.Tactics;

namespace Ebm.Desktop.ViewModels.Tactical;

/// <summary>One squad player in the tactic's assignment grid.</summary>
public sealed partial class AssignmentRowViewModel : ObservableObject
{
    [ObservableProperty]
    private TacticalRole _role;

    [ObservableProperty]
    private int _shotPriority = TacticLevel.Normal;

    [ObservableProperty]
    private string _minutesTarget = string.Empty;

    public AssignmentRowViewModel(TacticSquadPlayer player, TacticalAssignment? assignment)
    {
        ArgumentNullException.ThrowIfNull(player);
        Player = player;
        _role = assignment?.Role ?? player.SuggestedRole;
        _shotPriority = assignment?.ShotPriority ?? TacticLevel.Normal;
        _minutesTarget = assignment?.MinutesTarget?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    }

    public TacticSquadPlayer Player { get; }

    public string Name => Player.Player.FullName;

    public string Position => PlayerCatalog.DisplayName(Player.Player.Position);

    public string Squad => Player.RosterRole.ToString();

    public string Suggested => PlayerCatalog.DisplayName(Player.SuggestedRole);

    public TacticalAssignment ToAssignment()
    {
        int? minutes = string.IsNullOrWhiteSpace(MinutesTarget)
            ? null
            : int.TryParse(MinutesTarget, NumberStyles.Integer, CultureInfo.CurrentCulture, out var m)
                ? m
                : throw new DomainValidationException($"Minutes target of {Name} must be a whole number.");
        return new TacticalAssignment(Player.Player.Id, Role, ShotPriority, minutes);
    }
}

/// <summary>
/// The tactics screen (Step 09): a club's saved tactics, the selected one, systems, settings and player assignments.
/// Every change goes through <see cref="ITacticsService"/>.
/// </summary>
public sealed partial class TacticsViewModel : ObservableObject
{
    private readonly ITacticsService _tactics;
    private ClubTactics? _data;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private string _title = "Choose a club";

    [ObservableProperty]
    private IReadOnlyList<Tactic> _saved = [];

    [ObservableProperty]
    private Tactic? _selectedTactic;

    [ObservableProperty]
    private string _name = "Standard";

    [ObservableProperty]
    private OffensiveSystem _offensiveSystem;

    [ObservableProperty]
    private int _tempo = TacticLevel.Normal;

    [ObservableProperty]
    private int _shotSelection = TacticLevel.Normal;

    [ObservableProperty]
    private int _ballMovement = TacticLevel.Normal;

    [ObservableProperty]
    private int _spacing = TacticLevel.Normal;

    [ObservableProperty]
    private int _pickAndRollFrequency = TacticLevel.Normal;

    [ObservableProperty]
    private OffensiveFocus _focus;

    [ObservableProperty]
    private DefensiveSystem _defensiveSystem;

    [ObservableProperty]
    private int _intensity = TacticLevel.Normal;

    [ObservableProperty]
    private int _ballPressure = TacticLevel.Normal;

    [ObservableProperty]
    private int _helpDefense = TacticLevel.Normal;

    [ObservableProperty]
    private SwitchingPolicy _switching = SwitchingPolicy.SameSize;

    [ObservableProperty]
    private PickAndRollCoverage _coverage;

    [ObservableProperty]
    private ReboundingPriority _rebounding = ReboundingPriority.Balanced;

    [ObservableProperty]
    private IReadOnlyList<AssignmentRowViewModel> _assignments = [];

    [ObservableProperty]
    private string? _message;

    public TacticsViewModel(ITacticsService tactics, EditorLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(tactics);
        _tactics = tactics;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    public IReadOnlyList<OffensiveSystem> OffensiveSystems { get; } = Enum.GetValues<OffensiveSystem>();

    public IReadOnlyList<DefensiveSystem> DefensiveSystems { get; } = Enum.GetValues<DefensiveSystem>();

    public IReadOnlyList<OffensiveFocus> Focuses { get; } = Enum.GetValues<OffensiveFocus>();

    public IReadOnlyList<SwitchingPolicy> SwitchingPolicies { get; } = Enum.GetValues<SwitchingPolicy>();

    public IReadOnlyList<PickAndRollCoverage> Coverages { get; } = Enum.GetValues<PickAndRollCoverage>();

    public IReadOnlyList<ReboundingPriority> ReboundingPriorities { get; } = Enum.GetValues<ReboundingPriority>();

    public IReadOnlyList<TacticalRole> Roles { get; } = TacticalRoles.All;

    public IReadOnlyList<int> Levels { get; } = TacticLevel.All;

    /// <summary>Which tactic the club plays its next match with.</summary>
    public string SelectedText => _data?.Selected is { } t ? $"Next match: {t.Name}" : "No tactic selected: the club plays the standard tactic.";

    public async Task RefreshAsync()
    {
        var keep = SelectedTactic?.Id;
        _data = ClubId is { } id ? await _tactics.GetClubTacticsAsync(id) : null;
        Title = _data is null ? "Choose a club" : $"{_data.Club.Name} · tactics";
        Saved = _data?.Tactics ?? [];
        OnPropertyChanged(nameof(SelectedText));
        var show = Saved.FirstOrDefault(t => t.Id == keep) ?? _data?.Selected ?? Saved.FirstOrDefault();
        if (show is not null && show == SelectedTactic)
        {
            Fill(show);
        }
        else
        {
            SelectedTactic = show;
            if (show is null)
            {
                Fill(null);
            }
        }
    }

    async partial void OnClubIdChanged(ClubId? value)
    {
        Message = null;
        SelectedTactic = null;
        await RefreshAsync();
    }

    partial void OnSelectedTacticChanged(Tactic? value) => Fill(value);

    /// <summary>Clears the form for a new tactic.</summary>
    [RelayCommand]
    private void New()
    {
        SelectedTactic = null;
        Fill(null);
        Name = $"Tactic {Saved.Count + 1}";
    }

    [RelayCommand]
    private Task SaveAsync() => RunAsync(async () =>
    {
        var id = Required(ClubId);
        var draft = new TacticDraft(
            SelectedTactic?.Id,
            Name,
            OffensiveSystem,
            new OffensiveSettings { Tempo = Tempo, ShotSelection = ShotSelection, BallMovement = BallMovement, Spacing = Spacing, PickAndRollFrequency = PickAndRollFrequency, Focus = Focus },
            DefensiveSystem,
            new DefensiveSettings { Intensity = Intensity, BallPressure = BallPressure, HelpDefense = HelpDefense, Switching = Switching, PickAndRollCoverage = Coverage, Rebounding = Rebounding },
            [.. Assignments.Select(a => a.ToAssignment())]);
        var saved = await _tactics.SaveAsync(id, draft);
        await RefreshAsync();
        SelectedTactic = Saved.FirstOrDefault(t => t.Id == saved.Id);
        return $"Tactic '{saved.Name}' saved.";
    });

    /// <summary>Makes the shown tactic the one used in the next match.</summary>
    [RelayCommand]
    private Task UseForNextMatchAsync() => RunAsync(async () =>
    {
        var tactic = SelectedTactic ?? throw new DomainValidationException("Save the tactic first.");
        await _tactics.SelectAsync(tactic.Id);
        await RefreshAsync();
        return $"{tactic.Name} will be used in the next match.";
    });

    [RelayCommand]
    private Task DeleteAsync() => RunAsync(async () =>
    {
        var tactic = SelectedTactic ?? throw new DomainValidationException("Select a saved tactic.");
        await _tactics.DeleteAsync(tactic.Id);
        SelectedTactic = null;
        await RefreshAsync();
        return $"Tactic '{tactic.Name}' deleted.";
    });

    private void Fill(Tactic? tactic)
    {
        var offense = tactic?.Offense ?? OffensiveSettings.Default;
        var defense = tactic?.Defense ?? DefensiveSettings.Default;
        Name = tactic?.Name ?? (Saved.Count == 0 ? "Standard" : Name);
        OffensiveSystem = tactic?.OffensiveSystem ?? OffensiveSystem.MotionOffense;
        Tempo = offense.Tempo;
        ShotSelection = offense.ShotSelection;
        BallMovement = offense.BallMovement;
        Spacing = offense.Spacing;
        PickAndRollFrequency = offense.PickAndRollFrequency;
        Focus = offense.Focus;
        DefensiveSystem = tactic?.DefensiveSystem ?? DefensiveSystem.ManToMan;
        Intensity = defense.Intensity;
        BallPressure = defense.BallPressure;
        HelpDefense = defense.HelpDefense;
        Switching = defense.Switching;
        Coverage = defense.PickAndRollCoverage;
        Rebounding = defense.Rebounding;
        var stored = tactic?.Assignments.ToDictionary(a => a.PlayerId) ?? [];
        Assignments = [.. (_data?.Squad ?? []).Select(p => new AssignmentRowViewModel(p, stored.GetValueOrDefault(p.Player.Id)))];
    }

    private async Task RunAsync(Func<Task<string>> action)
    {
        try
        {
            Message = await action();
        }
        catch (DomainException ex)
        {
            Message = ex.Message;
        }
        catch (DataConstraintException ex)
        {
            Message = ex.Message;
        }
    }

    private static ClubId Required(ClubId? id) => id ?? throw new DomainValidationException("Choose a club.");
}
