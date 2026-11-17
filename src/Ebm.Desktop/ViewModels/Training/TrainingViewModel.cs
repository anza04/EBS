using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Development;
using Ebm.Domain.Players;
using Ebm.Services.Data;
using Ebm.Services.Development;

namespace Ebm.Desktop.ViewModels.Training;

/// <summary>One squad player on the training screen.</summary>
public sealed partial class TrainingRowViewModel : ObservableObject
{
    /// <summary>Choice shown for "no individual programme".</summary>
    public const string TeamOnly = "Team plan";

    [ObservableProperty]
    private string _focus;

    public TrainingRowViewModel(TrainingPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        Player = player;
        _focus = player.IndividualFocus?.ToString() ?? TeamOnly;
    }

    public TrainingPlayer Player { get; }

    public string Name => Player.Player.FullName;

    public string Position => PlayerCatalog.DisplayName(Player.Player.Position);

    public int Age => Player.Age;

    public int Overall => Player.Overall;

    public int Potential => Player.Potential;

    public int Fitness => Player.Player.Fitness;

    public int Experience => Player.Player.Experience;

    /// <summary>Expected overall change over a year with the saved plan.</summary>
    public string Outlook => Player.Outlook.ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture);

    public IndividualTraining? ToIndividual() =>
        Enum.TryParse<TrainingFocus>(Focus, out var focus) ? new IndividualTraining(Player.Player.Id, focus) : null;
}

/// <summary>
/// The training screen (Step 21): a club's team focus, intensity and individual programmes, its coaches and each
/// player's development outlook. Every change goes through <see cref="IDevelopmentService"/>.
/// </summary>
public sealed partial class TrainingViewModel : ObservableObject
{
    private readonly IDevelopmentService _development;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private string _title = "Choose a club";

    [ObservableProperty]
    private TrainingFocus _teamFocus;

    [ObservableProperty]
    private TrainingIntensity _intensity = TrainingIntensity.Normal;

    [ObservableProperty]
    private string _staffText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<TrainingRowViewModel> _players = [];

    [ObservableProperty]
    private string? _message;

    public TrainingViewModel(IDevelopmentService development, EditorLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(development);
        _development = development;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    public IReadOnlyList<TrainingFocus> Focuses { get; } = TrainingFocuses.All;

    public IReadOnlyList<TrainingIntensity> Intensities { get; } = Enum.GetValues<TrainingIntensity>();

    public IReadOnlyList<string> IndividualFocuses { get; } = [TrainingRowViewModel.TeamOnly, .. TrainingFocuses.All.Where(f => f != TrainingFocus.Balanced).Select(f => f.ToString())];

    public async Task RefreshAsync()
    {
        var data = ClubId is { } id ? await _development.GetClubTrainingAsync(id) : null;
        Title = data is null ? "Choose a club" : $"{data.Club.Name} · training{(data.IsSaved ? string.Empty : " (default plan)")}";
        TeamFocus = data?.Plan.TeamFocus ?? TrainingFocus.Balanced;
        Intensity = data?.Plan.Intensity ?? TrainingIntensity.Normal;
        StaffText = data is null
            ? string.Empty
            : $"Coaching {data.CoachAbility} · fitness coach {data.FitnessCoachAbility} · " +
              (data.Coaches.Count == 0 ? "no coaches under contract" : string.Join(", ", data.Coaches.Select(c => $"{c.FullName} ({PlayerCatalog.DisplayName(c.Role)}, {c.Ability})")));
        Players = [.. (data?.Players ?? []).Select(p => new TrainingRowViewModel(p))];
    }

    async partial void OnClubIdChanged(ClubId? value)
    {
        Message = null;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var id = ClubId ?? throw new DomainValidationException("Choose a club.");
            await _development.SavePlanAsync(id, new TrainingPlanDraft(TeamFocus, Intensity, [.. Players.Select(p => p.ToIndividual()).OfType<IndividualTraining>()]));
            await RefreshAsync();
            Message = "Training plan saved.";
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
}
