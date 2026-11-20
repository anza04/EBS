using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.ViewModels.Contracts;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;
using Ebm.Domain.Staff;
using Ebm.Services.Clubs;

namespace Ebm.Desktop.ViewModels.Staff;

/// <summary>Attribute columns shared by the staff and market grids.</summary>
public abstract class StaffRowView(StaffMember member, int age)
{
    public StaffMember Member => member;

    public string Name => member.FullName;

    public string Role => PlayerCatalog.DisplayName(member.Role);

    public int Age => age;

    public int Ability => member.Ability;

    public int Tactical => member.TacticalKnowledge;

    public int Development => member.Development;

    public int Motivation => member.Motivation;

    public int Scouting => member.Scouting;

    public int Medical => member.MedicalExpertise;

    public int Reputation => member.Reputation;

    public int Experience => member.Experience;

    public string Style => StaffRules.CoachingRoles.Contains(member.Role) ? PlayerCatalog.DisplayName(member.Style) : string.Empty;

    public string Preferences => (member.PreferredOffense, member.PreferredDefense) switch
    {
        (null, null) => string.Empty,
        var (o, d) => string.Join(" / ", new[] { o is { } off ? PlayerCatalog.DisplayName(off) : null, d is { } def ? PlayerCatalog.DisplayName(def) : null }.OfType<string>()),
    };
}

public sealed class StaffMemberRowView(StaffLine line) : StaffRowView(line.Member, line.Age)
{
    public StaffLine Line => line;

    public string Salary => Money.Euros(line.Contract.AnnualSalary);

    public string Until => Money.Date(line.Contract.EffectiveEndDate);
}

public sealed class StaffCandidateRowView(StaffCandidate candidate) : StaffRowView(candidate.Member, candidate.Age)
{
    public StaffCandidate Candidate => candidate;

    public string Demand => Money.Euros(candidate.Demand);

    public string Joins => candidate.WillJoin ? "Yes" : "No (reputation)";
}

public sealed record StaffDutyRowView(StaffDuty Duty)
{
    public string Name => Duty.Duty;

    public string Lead => Duty.Lead?.FullName ?? $"(no {PlayerCatalog.DisplayName(Duty.Role).ToLowerInvariant()})";

    public int Rating => Duty.Rating;

    public string Influence => Duty.Influence;
}

/// <summary>
/// The staff screen (Step 28, docs/STAFF.md): the club's staff with attributes and contracts, the responsibilities and
/// the ratings the game uses, and the staff market with hiring and dismissal. Every action goes through <see cref="IStaffService"/>.
/// </summary>
public sealed partial class StaffViewModel : ObservableObject
{
    private readonly IStaffService _staff;
    private readonly Func<Task> _reloadAll;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private string _title = "Choose a club";

    [ObservableProperty]
    private IReadOnlyList<StaffMemberRowView> _members = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DismissCommand))]
    private StaffMemberRowView? _selectedMember;

    [ObservableProperty]
    private IReadOnlyList<StaffDutyRowView> _duties = [];

    [ObservableProperty]
    private IReadOnlyList<StaffCandidateRowView> _market = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(HireCommand))]
    private StaffCandidateRowView? _selectedCandidate;

    [ObservableProperty]
    private StaffRole? _marketRole;

    [ObservableProperty]
    private string _offerSalary = string.Empty;

    [ObservableProperty]
    private string _offerYears = "2";

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    public StaffViewModel(IStaffService staff, EditorLookups lookups, Func<Task> reloadAll)
    {
        ArgumentNullException.ThrowIfNull(staff);
        ArgumentNullException.ThrowIfNull(lookups);
        ArgumentNullException.ThrowIfNull(reloadAll);
        _staff = staff;
        _reloadAll = reloadAll;
        Lookups = lookups;
        RoleOptions = [new(null, "(all roles)"), .. Enum.GetValues<StaffRole>().Select(r => new Option<StaffRole>(r, PlayerCatalog.DisplayName(r)))];
    }

    public EditorLookups Lookups { get; }

    public IReadOnlyList<Option<StaffRole>> RoleOptions { get; }

    public async Task RefreshAsync()
    {
        if (ClubId is not { } id || await _staff.GetClubStaffAsync(id) is not { } data)
        {
            Title = "Choose a club";
            Members = [];
            Duties = [];
            Market = [];
            return;
        }

        var keepMember = SelectedMember?.Member.Id;
        var keepCandidate = SelectedCandidate?.Member.Id;
        Title = $"{data.Club.Name} · {data.Members.Count} staff · wages {Money.Euros(data.AnnualWages)} a year";
        Members = [.. data.Members.Select(l => new StaffMemberRowView(l))];
        SelectedMember = Members.FirstOrDefault(m => m.Member.Id == keepMember);
        Duties = [.. data.Duties.Select(d => new StaffDutyRowView(d))];
        Market = [.. (await _staff.GetMarketAsync(id, MarketRole)).Select(c => new StaffCandidateRowView(c))];
        SelectedCandidate = Market.FirstOrDefault(c => c.Member.Id == keepCandidate);
    }

    [RelayCommand(CanExecute = nameof(CanHire))]
    private async Task HireAsync()
    {
        await RunAsync(async () =>
        {
            var club = ClubId ?? throw new DomainValidationException("Choose a club.");
            var candidate = SelectedCandidate ?? throw new DomainValidationException("Select a staff member on the market.");
            var salary = string.IsNullOrWhiteSpace(OfferSalary) ? candidate.Candidate.Demand : Money.Parse(OfferSalary, "Salary");
            var years = (int)Money.Parse(OfferYears, "Years");
            var contract = await _staff.HireAsync(club, candidate.Member.Id, salary, years);
            return $"{candidate.Name} joins as {candidate.Role.ToLowerInvariant()} until {Money.Date(contract.EndDate)} for {Money.Euros(contract.AnnualSalary)} a year.";
        });
    }

    private bool CanHire() => SelectedCandidate is not null;

    [RelayCommand(CanExecute = nameof(CanDismiss))]
    private async Task DismissAsync()
    {
        await RunAsync(async () =>
        {
            var row = SelectedMember ?? throw new DomainValidationException("Select a staff member.");
            var paid = await _staff.DismissAsync(row.Line.Contract.Id);
            return $"{row.Name} dismissed; {Money.Euros(paid)} paid for the rest of his contract.";
        });
    }

    private bool CanDismiss() => SelectedMember is not null;

    async partial void OnClubIdChanged(ClubId? value)
    {
        Message = null;
        await RefreshAsync();
    }

    async partial void OnMarketRoleChanged(StaffRole? value) => await RefreshAsync();

    partial void OnSelectedCandidateChanged(StaffCandidateRowView? value) => OfferSalary = value is null ? string.Empty : value.Candidate.Demand.ToString(System.Globalization.CultureInfo.CurrentCulture);

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
