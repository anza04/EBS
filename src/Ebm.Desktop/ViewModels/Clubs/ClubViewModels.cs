using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.Players;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Services.Clubs;
using Ebm.Services.Data;

namespace Ebm.Desktop.ViewModels.Clubs;

internal static class Format
{
    public static string Euros(long value) => $"€ {value.ToString("N0", CultureInfo.CurrentCulture)}";

    public static string Number(int value) => value.ToString(CultureInfo.CurrentCulture);

    public static string Name<TEnum>(TEnum value)
        where TEnum : struct, Enum => PlayerCatalog.DisplayName(value);
}

/// <summary>The club profile screen: details, finances, board, competitions and a squad summary, read-only.</summary>
public sealed partial class ClubProfileViewModel : ObservableObject
{
    private readonly IClubService _clubs;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private string _title = "Choose a club";

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _details = [];

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _finances = [];

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _competitions = [];

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _squad = [];

    public ClubProfileViewModel(IClubService clubs, EditorLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(clubs);
        _clubs = clubs;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    public async Task RefreshAsync()
    {
        var profile = ClubId is { } id ? await _clubs.GetProfileAsync(id) : null;
        if (profile is null)
        {
            Title = "Choose a club";
            Details = Finances = Competitions = Squad = [];
            return;
        }

        var club = profile.Club;
        var roster = profile.Roster;
        Title = $"{club.Name} ({club.ShortName})";
        Details =
        [
            new("Country", profile.Country),
            new("City", profile.City ?? "unknown"),
            new("Arena", profile.Arena is { } arena ? $"{arena.Name} ({arena.Capacity.ToString("N0", CultureInfo.CurrentCulture)} seats)" : "none"),
            new("Founded", club.FoundedYear?.ToString(CultureInfo.CurrentCulture) ?? "unknown"),
            new("Reputation", Format.Number(club.Reputation)),
            new("Board expectation", Format.Name(club.BoardExpectation)),
        ];
        Finances =
        [
            new("Balance", Format.Euros(club.Balance)),
            new("Wage budget (a year)", Format.Euros(club.WageBudget)),
            new("Player wages (a year)", Format.Euros(profile.WageBill)),
            new(profile.WageBudgetLeft >= 0 ? "Budget left" : "Over budget by", Format.Euros(Math.Abs(profile.WageBudgetLeft))),
        ];
        Competitions = profile.Season is null
            ? [new("Season", "no current season")]
            : [new("Season", profile.Season.Name), .. profile.Competitions.Select(c => new InfoLine(c.Name, Format.Name(c.Type)))];
        var firstTeam = roster.FirstTeam.ToList();
        Squad =
        [
            new("First team", Format.Number(firstTeam.Count)),
            new("Youth squad", Format.Number(roster.Players.Count - firstTeam.Count)),
            new("Foreign players", Format.Number(firstTeam.Count(p => p.IsForeign))),
            new("Unavailable", Format.Number(roster.Players.Count(p => p.Availability == PlayerAvailability.Unavailable))),
            new("Average age (first team)", firstTeam.Count == 0 ? "-" : firstTeam.Average(p => p.Age).ToString("0.0", CultureInfo.CurrentCulture)),
            new("Starting five overall", roster.Starters.Any() ? roster.Starters.Average(p => p.RatingInRole).ToString("0", CultureInfo.CurrentCulture) : "-"),
            new("Roster", roster.IsValid ? "valid" : Problems(roster.Validation.Errors.Count() + roster.Registrations.Sum(r => r.Validation.Errors.Count())) + " (see Squad)"),
        ];
    }

    async partial void OnClubIdChanged(ClubId? value) => await RefreshAsync();

    internal static string Problems(int count) => count == 1 ? "1 problem" : $"{count} problems";
}

/// <summary>One player of the squad grid; role, lineup position and rotation order are editable.</summary>
public sealed partial class SquadRow : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStarter), nameof(IsRotation))]
    private RosterRole _role;

    [ObservableProperty]
    private Position? _lineupPosition;

    [ObservableProperty]
    private string _rotationOrder;

    public SquadRow(RosterPlayer player)
    {
        Player = player;
        _role = player.Role;
        _lineupPosition = player.LineupPosition;
        _rotationOrder = player.RotationOrder?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    }

    public RosterPlayer Player { get; }

    public string Name => Player.Player.FullName;

    public string Position => Format.Name(Player.Player.Position);

    public int Age => Player.Age;

    public string Nationality => Player.IsForeign ? $"{Player.Nationality} (foreign)" : Player.Nationality;

    public int Overall => Player.Evaluation.Overall;

    public string BestPosition => Format.Name(Player.Evaluation.BestPosition);

    public string Availability => Format.Name(Player.Availability);

    public string Contract => Player.HasContract ? "yes" : "none";

    public bool IsStarter => Role == RosterRole.Starter;

    public bool IsRotation => Role == RosterRole.Rotation;

    // A new starter starts at his primary position; leaving the starting five or the rotation clears the extra field.
    partial void OnRoleChanged(RosterRole value)
    {
        LineupPosition = value == RosterRole.Starter ? LineupPosition ?? Player.Player.Position : null;
        if (value != RosterRole.Rotation)
        {
            RotationOrder = string.Empty;
        }
    }
}

/// <summary>A lineup slot of the starting-five panel.</summary>
public sealed record LineupLine(string Position, string Player, string Rating, string Familiarity);

/// <summary>A roster problem; <see cref="Scope"/> is "Club" or a competition name.</summary>
public sealed record IssueLine(string Severity, string Scope, string Message, bool IsError);

/// <summary>
/// The squad screen: assigns roles (starting five, rotation, bench, inactive, youth), lineup positions and the
/// rotation order, validates the configuration live and saves it; also signs a player to the club.
/// </summary>
public sealed partial class SquadViewModel : ObservableObject
{
    private readonly IClubService _clubs;
    private readonly Func<Task> _reloadAll;
    private ClubRoster? _roster;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private string _summary = "Choose a club";

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private IReadOnlyList<LineupLine> _lineup = [];

    [ObservableProperty]
    private IReadOnlyList<IssueLine> _issues = [];

    [ObservableProperty]
    private PlayerId? _playerToAdd;

    [ObservableProperty]
    private RosterRole _roleToAdd = RosterRole.Bench;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    public SquadViewModel(IClubService clubs, EditorLookups lookups, Func<Task> reloadAll)
    {
        ArgumentNullException.ThrowIfNull(clubs);
        ArgumentNullException.ThrowIfNull(reloadAll);
        _clubs = clubs;
        _reloadAll = reloadAll;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    public IReadOnlyList<RosterRole> Roles { get; } = Enum.GetValues<RosterRole>();

    public IReadOnlyList<RosterRole> JoinRoles { get; } = [RosterRole.Bench, RosterRole.Inactive, RosterRole.Youth];

    public ObservableCollection<SquadRow> Rows { get; } = [];

    /// <summary>Reloads the squad from the database; unsaved changes are discarded.</summary>
    public async Task RefreshAsync()
    {
        Show(ClubId is { } id ? await _clubs.GetRosterAsync(id) : null);
    }

    async partial void OnClubIdChanged(ClubId? value)
    {
        Message = null;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RevertAsync()
    {
        await RefreshAsync();
        Report("Changes discarded.", error: false);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (ClubId is not { } id)
        {
            return;
        }

        try
        {
            Show(await _clubs.SaveRosterAsync(id, Assignments()));
            Report(IsValid ? "Saved. The roster is valid." : "Saved. The roster still has problems (listed below).", error: !IsValid);
        }
        catch (DomainException ex)
        {
            Report(ex.Message, error: true);
        }
        catch (DataConstraintException ex)
        {
            Report(ex.Message, error: true);
        }
    }

    [RelayCommand]
    private async Task AddPlayerAsync()
    {
        if (ClubId is not { } id || PlayerToAdd is not { } player)
        {
            Report("Choose a player to sign.", error: true);
            return;
        }

        try
        {
            await _clubs.AssignPlayerAsync(player, id, RoleToAdd);
            PlayerToAdd = null;
            await _reloadAll();
            Report("Player added to the squad.", error: false);
        }
        catch (DomainException ex)
        {
            Report(ex.Message, error: true);
        }
        catch (DataConstraintException ex)
        {
            Report(ex.Message, error: true);
        }
    }

    /// <summary>Rotation players first, in the entered order (blank orders last), then everyone else.</summary>
    private List<RosterAssignment> Assignments()
    {
        var rotation = Rows.Where(r => r.Role == RosterRole.Rotation)
            .Select((r, index) => (Row: r, Order: ParseOrder(r), Index: index))
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Index)
            .Select(x => x.Row);
        return [.. rotation.Concat(Rows.Where(r => r.Role != RosterRole.Rotation))
            .Select(r => new RosterAssignment(r.Player.Player.Id, r.Role, r.Role == RosterRole.Starter ? r.LineupPosition : null))];
    }

    private static int ParseOrder(SquadRow row) =>
        string.IsNullOrWhiteSpace(row.RotationOrder) ? int.MaxValue
        : int.TryParse(row.RotationOrder.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var order) ? order
        : throw new DomainValidationException($"The rotation order of {row.Name} must be a whole number.");

    private void Show(ClubRoster? roster)
    {
        _roster = roster;
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();
        foreach (var player in roster?.Players ?? [])
        {
            var row = new SquadRow(player);
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        Revalidate();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SquadRow.Role) or nameof(SquadRow.LineupPosition))
        {
            Revalidate();
        }
    }

    /// <summary>Checks the edited (unsaved) configuration with the same rules the service applies.</summary>
    private void Revalidate()
    {
        if (_roster is null)
        {
            Summary = "Choose a club";
            IsValid = false;
            Lineup = [];
            Issues = [];
            return;
        }

        var members = Rows.Select(r => r.Player.ToMember(r.Role, r.IsStarter ? r.LineupPosition : null)).ToList();
        var (club, registrations) = ClubRoster.Check(members, _roster.Registrations.Select(r => r.Competition));
        Issues =
        [
            .. club.Issues.Select(i => Line(i, "Club")),
            .. registrations.SelectMany(r => r.Validation.Issues.Select(i => Line(i, r.Competition.Name))),
        ];
        IsValid = club.IsValid && registrations.All(r => r.Validation.IsValid);
        var firstTeam = members.Count(m => m.Role.IsFirstTeam());
        var errors = Issues.Count(i => i.IsError);
        Summary = $"{firstTeam} first team · {members.Count - firstTeam} youth · {members.Count(m => m.Role.IsActive())} in the matchday squad · " +
            (IsValid ? "roster valid" : ClubProfileViewModel.Problems(errors));
        Lineup =
        [
            .. Enum.GetValues<Position>().Select(position =>
            {
                var starter = Rows.FirstOrDefault(r => r.IsStarter && r.LineupPosition == position);
                if (starter is null)
                {
                    return new LineupLine(Format.Name(position), "(empty)", string.Empty, string.Empty);
                }

                var at = starter.Player.Evaluation.At(position);
                return new LineupLine(Format.Name(position), starter.Name, Format.Number(at.EffectiveRating), Format.Name(at.Familiarity));
            }),
        ];
    }

    private static IssueLine Line(RosterIssue issue, string scope) =>
        new(issue.Severity == RosterIssueSeverity.Error ? "Error" : "Warning", scope, issue.Message, issue.Severity == RosterIssueSeverity.Error);

    private void Report(string message, bool error)
    {
        Message = message;
        IsError = error;
    }
}
