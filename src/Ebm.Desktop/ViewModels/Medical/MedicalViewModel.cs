using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Domain.Clubs;
using Ebm.Domain.Medical;
using Ebm.Domain.Players;
using Ebm.Services.Medical;

namespace Ebm.Desktop.ViewModels.Medical;

/// <summary>One squad player in the medical room.</summary>
public sealed class MedicalRowViewModel(MedicalPlayer row)
{
    public string Name => row.Player.FullName;

    public string Position => PlayerCatalog.DisplayName(row.Player.Position);

    public int Age => row.Age;

    public int Fitness => row.Player.Fitness;

    public string Status => PlayerCatalog.DisplayName(row.Player.InjuryStatus);

    public string Injury => row.Injury is { } i ? $"{i.Definition.Name} ({PlayerCatalog.DisplayName(i.Severity).ToLowerInvariant()}{(i.IsRecurrence ? ", recurrence" : string.Empty)})" : string.Empty;

    public string Phase => row.Injury is { } i ? PlayerCatalog.DisplayName(i.Phase) : string.Empty;

    public string Training => row.Injury is { } i ? i.Restriction switch
    {
        TrainingRestriction.NoTraining => "No training",
        TrainingRestriction.LightOnly => "Light work only",
        _ => "Full",
    } : "Full";

    public string Return => row.Injury is { } i ? MedicalViewModel.Date(i.EstimatedReturn) : string.Empty;

    /// <summary>Injury risk relative to an average player.</summary>
    public string Risk => row.Risk.ToString("0.00", CultureInfo.CurrentCulture);

    public int RecentInjuries => row.RecentInjuries;
}

/// <summary>One injury in the club's history.</summary>
public sealed class InjuryHistoryRowViewModel(InjuryRecord record)
{
    public string Date => MedicalViewModel.Date(record.Injury.OccurredOn);

    public string Player => record.Player;

    public string Injury => record.Injury.Definition.Name;

    public string Severity => PlayerCatalog.DisplayName(record.Injury.Severity);

    public string Source => PlayerCatalog.DisplayName(record.Injury.Source);

    public string Back => record.Injury.RecoveredOn is { } back ? MedicalViewModel.Date(back) : $"expected {MedicalViewModel.Date(record.Injury.EstimatedReturn)}";
}

/// <summary>
/// The medical room (Step 22, docs/INJURIES.md): the club's physio, every squad player's injury, recovery phase,
/// training restriction, expected return and injury risk, and the injuries of the last year. Read-only.
/// </summary>
public sealed partial class MedicalViewModel : ObservableObject
{
    private readonly IMedicalService _medical;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private string _title = "Choose a club";

    [ObservableProperty]
    private string _staffText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<MedicalRowViewModel> _players = [];

    [ObservableProperty]
    private IReadOnlyList<InjuryHistoryRowViewModel> _history = [];

    public MedicalViewModel(IMedicalService medical, EditorLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(medical);
        _medical = medical;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    internal static string Date(DateOnly date) => date.ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    public async Task RefreshAsync()
    {
        var data = ClubId is { } id ? await _medical.GetClubMedicalAsync(id) : null;
        Title = data is null ? "Choose a club" : $"{data.Club.Name} · medical";
        StaffText = data is null
            ? string.Empty
            : $"{(data.Physio is { } physio ? $"Physio {physio.FullName} ({physio.Ability})" : $"No physio under contract (ability {data.PhysioAbility} assumed)")} · " +
              $"{data.Players.Count(p => p.Injury is not null)} injured · {data.History.Count} injuries in the last year";
        Players = [.. (data?.Players ?? []).Select(p => new MedicalRowViewModel(p))];
        History = [.. (data?.History ?? []).Select(h => new InjuryHistoryRowViewModel(h))];
    }

    async partial void OnClubIdChanged(ClubId? value) => await RefreshAsync();
}
