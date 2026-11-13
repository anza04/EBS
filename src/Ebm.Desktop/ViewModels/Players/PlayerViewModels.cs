using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Desktop.ViewModels.Contracts;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Domain.Common;
using Ebm.Domain.Players;
using Ebm.Services.Contracts;
using Ebm.Services.Players;

namespace Ebm.Desktop.ViewModels.Players;

/// <summary>A label and a value, for the read-only lists of the profile.</summary>
public sealed record InfoLine(string Label, string Value);

/// <summary>A titled list of lines (one attribute category, the tendencies, …).</summary>
public sealed record InfoGroup(string Title, IReadOnlyList<InfoLine> Lines);

/// <summary>One row of the position table.</summary>
public sealed record PositionLine(string Position, int Rating, int EffectiveRating, string Familiarity, string Suitability);

/// <summary>The player profile screen: every stored and calculated value of one player, read-only.</summary>
public sealed partial class PlayerProfileViewModel : ObservableObject
{
    private readonly IPlayerProfileService _profiles;
    private readonly IContractService _contracts;

    [ObservableProperty]
    private PlayerId? _playerId;

    [ObservableProperty]
    private IReadOnlyList<ContractRowView> _contractHistory = [];

    [ObservableProperty]
    private PlayerProfile? _profile;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _details = [];

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _ratings = [];

    [ObservableProperty]
    private IReadOnlyList<PositionLine> _positions = [];

    [ObservableProperty]
    private IReadOnlyList<InfoGroup> _groups = [];

    public PlayerProfileViewModel(IPlayerProfileService profiles, IContractService contracts, EditorLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(contracts);
        _profiles = profiles;
        _contracts = contracts;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    /// <summary>Reloads the shown player (after edits or imports).</summary>
    public async Task RefreshAsync()
    {
        var profile = PlayerId is { } id ? await _profiles.GetProfileAsync(id) : null;
        var contracts = PlayerId is { } pid ? await _contracts.GetPlayerContractsAsync(pid) : null;
        Show(profile, contracts);
    }

    // Fire-and-forget is safe: GetProfileAsync only reads, and unexpected errors reach the global handler.
    async partial void OnPlayerIdChanged(PlayerId? value) => await RefreshAsync();

    private void Show(PlayerProfile? profile, PlayerContracts? contracts)
    {
        Profile = profile;
        ContractHistory = contracts is null ? [] : [.. contracts.Contracts.Select(c => new ContractRowView(c))];
        if (profile is null)
        {
            Title = "Choose a player";
            Details = Ratings = [];
            Positions = [];
            Groups = [];
            return;
        }

        var p = profile.Player;
        var e = profile.Evaluation;
        var contract = profile.ActiveContract;
        Title = $"{p.FullName} · {PlayerCatalog.DisplayName(p.Position)}";
        Details =
        [
            new("Born", $"{Date(p.DateOfBirth)} (age {profile.Age})"),
            new("Nationality", profile.SecondNationality is { } second ? $"{profile.Nationality} (also {second})" : profile.Nationality),
            new("Height / weight", $"{p.HeightCm} cm / {p.WeightKg} kg"),
            new("Wingspan", $"{p.WingspanCm} cm"),
            new("Secondary positions", p.SecondaryPositions.IsEmpty ? "none" : string.Join(", ", p.SecondaryPositions.Positions.Select(PlayerCatalog.DisplayName))),
            new("Current club", profile.CurrentClub ?? "free agent"),
            new("Salary", contract is null ? "none" : $"€ {contract.AnnualSalary.ToString("N0", CultureInfo.CurrentCulture)} a year"),
            new("Contract status", ContractStatusText(contracts)),
            new("Market value", $"€ {profile.MarketValue.ToString("N0", CultureInfo.CurrentCulture)} (estimate)"),
            new("Club history", profile.History is { Count: > 0 } history
                ? string.Join("; ", history.Select(s => $"{s.Club} {s.Start.Year}–{(s.End is { } end ? end.Year.ToString(CultureInfo.CurrentCulture) : "now")}"))
                : "none recorded"),
            new("Data", $"profile {p.ProfileStatus}, attributes {p.AttributesStatus}{(contract is null ? string.Empty : $", contract {contract.TermsStatus}")}"),
            new("Source", $"{p.Source ?? "none"}{(contract?.Source is { } cs ? $" (contract: {cs})" : string.Empty)}"),
            new("Experience", Number(p.Experience)),
            new("Reputation", Number(p.Reputation)),
            new("Fitness", $"{p.Fitness}%"),
            new("Morale", Number(p.Morale)),
            new("Injury", p.InjuryStatus == InjuryStatus.Healthy ? "healthy"
                : $"{PlayerCatalog.DisplayName(p.InjuryStatus)}{(p.InjuryReturnDate is { } back ? $", back {Date(back)}" : string.Empty)}"),
        ];
        Ratings =
        [
            new("Overall", Number(e.Overall)),
            new("Potential", Number(e.PotentialRating)),
            new("Best position", PlayerCatalog.DisplayName(e.BestPosition)),
            .. PlayerCatalog.Categories.Select(c => new InfoLine(PlayerCatalog.DisplayName(c), Number(e.Categories[c]))),
        ];
        Positions =
        [
            .. e.Positions.Select(x => new PositionLine(
                PlayerCatalog.DisplayName(x.Position), x.Rating, x.EffectiveRating, PlayerCatalog.DisplayName(x.Familiarity), PlayerCatalog.DisplayName(x.Suitability))),
        ];
        Groups =
        [
            .. PlayerCatalog.Categories.Select(c => new InfoGroup(
                PlayerCatalog.DisplayName(c),
                [.. PlayerCatalog.AttributesOf(c).Select(a => new InfoLine(PlayerCatalog.DisplayName(a), Number(p.Attributes[a])))])),
            new InfoGroup("Tendencies", [.. PlayerCatalog.Tendencies.Select(t => new InfoLine(PlayerCatalog.DisplayName(t), Number(p.Tendencies[t])))]),
        ];
    }

    private static string ContractStatusText(PlayerContracts? contracts) => contracts switch
    {
        null => string.Empty,
        { IsFreeAgent: true } => "free agent",
        { Running: { } running, Upcoming: { } next } when next.Club != running.Club => $"under contract; joins {next.Club} on {Date(next.Contract.StartDate)}",
        { Running: { } running } => $"under contract until {Date(running.Contract.EffectiveEndDate)}",
        { Upcoming: { } next } => $"signed with {next.Club} from {Date(next.Contract.StartDate)}",
        _ => string.Empty,
    };

    private static string Date(DateOnly date) => date.ToString("d", CultureInfo.CurrentCulture);

    private static string Number(int value) => value.ToString(CultureInfo.CurrentCulture);
}

/// <summary>One row of the comparison table; <see cref="Better"/> names the player with the single best value.</summary>
public sealed record ComparisonLine(string Group, string Label, int A, int B, string Better);

/// <summary>The player comparison screen: two players side by side.</summary>
public sealed partial class PlayerComparisonViewModel : ObservableObject
{
    private readonly IPlayerProfileService _profiles;

    [ObservableProperty]
    private PlayerId? _playerA;

    [ObservableProperty]
    private PlayerId? _playerB;

    [ObservableProperty]
    private string _nameA = "Player A";

    [ObservableProperty]
    private string _nameB = "Player B";

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private IReadOnlyList<ComparisonLine> _rows = [];

    public PlayerComparisonViewModel(IPlayerProfileService profiles, EditorLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _profiles = profiles;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    /// <summary>Recalculates the comparison (after a selection change, edits or imports).</summary>
    public async Task RefreshAsync()
    {
        Rows = [];
        if (PlayerA is not { } a || PlayerB is not { } b)
        {
            Message = "Choose two players.";
            return;
        }

        try
        {
            var comparison = await _profiles.CompareAsync([a, b]);
            NameA = comparison.Players[0].Player.FullName;
            NameB = comparison.Players[1].Player.FullName;
            string[] names = [NameA, NameB];
            Rows = [.. comparison.Rows.Select(r => new ComparisonLine(r.Group, r.Label, r.Values[0], r.Values[1], r.LeaderIndex is { } i ? names[i] : string.Empty))];
            Message = null;
        }
        catch (DomainException ex)
        {
            Message = ex.Message;
        }
    }

    async partial void OnPlayerAChanged(PlayerId? value) => await RefreshAsync();

    async partial void OnPlayerBChanged(PlayerId? value) => await RefreshAsync();
}
