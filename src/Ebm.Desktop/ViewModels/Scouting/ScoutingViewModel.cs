using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.ViewModels.Contracts;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.Players;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Scouting;
using Ebm.Domain.Staff;
using Ebm.Services.Scouting;

namespace Ebm.Desktop.ViewModels.Scouting;

/// <summary>One scout in the scouts grid.</summary>
public sealed record ScoutRowView(Scout Scout)
{
    public string Name => Scout.Name;

    public int Ability => Scout.Member.Ability;

    public string ContractEnd => Money.Date(Scout.Contract.EffectiveEndDate);

    public string Doing => Scout.Assignment is { } a ? $"{a.Target} until {Money.Date(a.DueDate)}" : "Available";
}

/// <summary>One assignment in the assignments grid.</summary>
public sealed record AssignmentRowView(AssignmentLine Line)
{
    public string Scout => Line.Scout;

    public string Target => Line.Target;

    public string Started => Money.Date(Line.Assignment.StartDate);

    public string Due => Money.Date(Line.DueDate);

    public string Status => Line.Status.ToString();

    public string Reports => Line.Status == AssignmentStatus.Completed ? Line.Reports.ToString(CultureInfo.CurrentCulture) : "—";
}

/// <summary>One report in the reports grid: estimates with their ranges.</summary>
public sealed record ReportRowView(ReportLine Line)
{
    public string Player => Line.Player.FullName;

    public string Position => PlayerCatalog.DisplayName(Line.Player.Position);

    public int Age => Line.Age;

    public string Club => Line.Club ?? "Free agent";

    public string Overall => Line.OverallText;

    public string Potential => Line.PotentialText;

    public int Accuracy => Line.Report.Accuracy;

    public string Value => Money.Euros(Line.Report.EstimatedValue);

    public string Salary => Money.Euros(Line.Report.EstimatedSalary);

    public string Date => Money.Date(Line.Report.Date);

    public string Scout => Line.Scout;
}

/// <summary>
/// The scouting screen (Step 20): the club's scouts, new and running assignments, and the player search over the
/// club's reports with each report's attribute ranges. Every action goes through <see cref="IScoutingService"/>.
/// </summary>
public sealed partial class ScoutingViewModel : ObservableObject
{
    private readonly IScoutingService _scouting;
    private readonly Func<Task> _reloadAll;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private string _title = "Choose a club";

    [ObservableProperty]
    private IReadOnlyList<ScoutRowView> _scouts = [];

    [ObservableProperty]
    private IReadOnlyList<Option<StaffMemberId>> _availableScouts = [];

    [ObservableProperty]
    private IReadOnlyList<AssignmentRowView> _assignments = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelAssignmentCommand))]
    private AssignmentRowView? _selectedAssignment;

    [ObservableProperty]
    private IReadOnlyList<ReportRowView> _reports = [];

    [ObservableProperty]
    private ReportRowView? _selectedReport;

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _reportDetails = [];

    [ObservableProperty]
    private StaffMemberId? _scoutId;

    [ObservableProperty]
    private ScoutingFocus _focus = ScoutingFocus.League;

    [ObservableProperty]
    private PlayerId? _targetPlayerId;

    [ObservableProperty]
    private CountryId? _targetCountryId;

    [ObservableProperty]
    private CompetitionId? _targetCompetitionId;

    [ObservableProperty]
    private Position? _targetPosition;

    [ObservableProperty]
    private string _maxAge = string.Empty;

    [ObservableProperty]
    private string _searchName = string.Empty;

    [ObservableProperty]
    private Position? _searchPosition;

    [ObservableProperty]
    private string _searchMaxAge = string.Empty;

    [ObservableProperty]
    private string _searchMinOverall = string.Empty;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    public ScoutingViewModel(IScoutingService scouting, EditorLookups lookups, Func<Task> reloadAll)
    {
        ArgumentNullException.ThrowIfNull(scouting);
        ArgumentNullException.ThrowIfNull(lookups);
        ArgumentNullException.ThrowIfNull(reloadAll);
        _scouting = scouting;
        _reloadAll = reloadAll;
        Lookups = lookups;
        PositionOptions = [new(null, "(any)"), .. Enum.GetValues<Position>().Select(p => new Option<Position>(p, PlayerCatalog.DisplayName(p)))];
    }

    public EditorLookups Lookups { get; }

    public IReadOnlyList<ScoutingFocus> Focuses { get; } = Enum.GetValues<ScoutingFocus>();

    public IReadOnlyList<Option<Position>> PositionOptions { get; }

    public string FocusHelp => Focus switch
    {
        ScoutingFocus.Player => $"Watches one player closely for {ScoutingRules.DurationDays(Focus)} days: the most accurate report.",
        ScoutingFocus.Youth => $"Watches young players (default age ≤ {ScoutingRules.YouthMaxAge}) for {ScoutingRules.DurationDays(Focus)} days; country optional.",
        _ => $"Reports on up to {ScoutingRules.ReportsPerAssignment} players after {ScoutingRules.DurationDays(Focus)} days, the most visible first. Position and age are optional filters.",
    };

    public async Task RefreshAsync()
    {
        if (ClubId is not { } id)
        {
            Title = "Choose a club";
            Scouts = [];
            AvailableScouts = [];
            Assignments = [];
            Reports = [];
            ReportDetails = [];
            return;
        }

        var scouts = await _scouting.GetScoutsAsync(id);
        Scouts = [.. scouts.Select(s => new ScoutRowView(s))];
        AvailableScouts = [.. scouts.Where(s => s.IsAvailable).Select(s => new Option<StaffMemberId>(s.Member.Id, $"{s.Name} (ability {s.Member.Ability})"))];
        if (ScoutId is not { } chosen || AvailableScouts.All(o => o.Id != chosen))
        {
            ScoutId = AvailableScouts.FirstOrDefault()?.Id;
        }

        Assignments = [.. (await _scouting.GetAssignmentsAsync(id)).Select(a => new AssignmentRowView(a))];
        Title = $"{Lookups.Clubs.FirstOrDefault(c => c.Id == id)?.Name} · {scouts.Count} scouts, {scouts.Count(s => !s.IsAvailable)} on assignment";
        await SearchAsync();
    }

    async partial void OnClubIdChanged(ClubId? value)
    {
        Message = null;
        await RefreshAsync();
    }

    partial void OnFocusChanged(ScoutingFocus value) => OnPropertyChanged(nameof(FocusHelp));

    partial void OnSelectedReportChanged(ReportRowView? value)
    {
        if (value?.Line.Report is not { } report)
        {
            ReportDetails = [];
            return;
        }

        ReportDetails =
        [
            new("Player", $"{value.Player}, {value.Position}, age {value.Age} ({value.Line.Nationality})"),
            new("Report", $"{value.Date} by {value.Scout}, accuracy {report.Accuracy} (± {report.Margin})"),
            new("Overall", value.Overall),
            new("Potential", value.Potential),
            new("Estimated value", value.Value),
            new("Salary expectation", $"{value.Salary} a year"),
            .. PlayerCatalog.Attributes.Select(a => new InfoLine(PlayerCatalog.DisplayName(a), Range(report.RangeOf(a)))),
        ];
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (ClubId is not { } id)
        {
            return;
        }

        try
        {
            var search = new ReportSearch(
                string.IsNullOrWhiteSpace(SearchName) ? null : SearchName,
                SearchPosition,
                ParseOptional(SearchMaxAge, "Maximum age"),
                ParseOptional(SearchMinOverall, "Minimum overall"));
            var keep = SelectedReport?.Line.Player.Id;
            Reports = [.. (await _scouting.SearchReportsAsync(id, search)).Select(r => new ReportRowView(r))];
            SelectedReport = Reports.FirstOrDefault(r => r.Line.Player.Id == keep);
        }
        catch (DomainException ex)
        {
            Report(ex.Message, error: true);
        }
    }

    [RelayCommand]
    private async Task AssignAsync()
    {
        await RunAsync(async () =>
        {
            var club = ClubId ?? throw new DomainValidationException("Choose a club.");
            var scout = ScoutId ?? throw new DomainValidationException("Choose an available scout.");
            var line = await _scouting.AssignAsync(club, new AssignmentRequest(
                scout,
                Focus,
                Focus == ScoutingFocus.Player ? TargetPlayerId : null,
                Focus is ScoutingFocus.Region or ScoutingFocus.Youth ? TargetCountryId : null,
                Focus == ScoutingFocus.League ? TargetCompetitionId : null,
                Focus == ScoutingFocus.Player ? null : TargetPosition,
                Focus == ScoutingFocus.Player ? null : ParseOptional(MaxAge, "Maximum age")));
            return $"{line.Scout} is scouting {line.Target}; reports on {Money.Date(line.DueDate)}.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanCancelAssignment))]
    private async Task CancelAssignmentAsync()
    {
        await RunAsync(async () =>
        {
            var row = SelectedAssignment ?? throw new DomainValidationException("Select an assignment.");
            await _scouting.CancelAsync(row.Line.Assignment.Id);
            return $"Cancelled: {row.Target}.";
        });
    }

    private bool CanCancelAssignment() => SelectedAssignment?.Line.Status == AssignmentStatus.Active;

    private static string Range((int Low, int High) range) => range.Low == range.High ? $"{range.Low}" : $"{range.Low}–{range.High}";

    private static int? ParseOptional(string? text, string field) =>
        string.IsNullOrWhiteSpace(text) ? null
        : int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) ? value
        : throw new DomainValidationException($"{field} must be a whole number.");

    private async Task RunAsync(Func<Task<string>> action)
    {
        try
        {
            var done = await action();
            await _reloadAll();
            Report(done, error: false);
        }
        catch (DomainException ex)
        {
            Report(ex.Message, error: true);
        }
    }

    private void Report(string message, bool error)
    {
        Message = message;
        IsError = error;
    }
}
