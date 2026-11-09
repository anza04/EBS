using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Services.Editing;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>A contract with the names of its club and player, for display.</summary>
public sealed record ContractRow(Contract Contract, string Player, string Club)
{
    public string Status => Contract.Status.ToString();
}

/// <summary>
/// Adds player contracts and edits their terms. Lifecycle changes (signing with negotiation, renewal, termination,
/// options) are made on the club contracts screen.
/// </summary>
public sealed partial class ContractsPageViewModel : EditorPageViewModel<ContractRow>
{
    [ObservableProperty]
    private PlayerId? _playerId;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private DateTime? _endDate;

    [ObservableProperty]
    private string _annualSalary = string.Empty;

    [ObservableProperty]
    private PaymentFrequency _paymentFrequency = PaymentFrequency.Monthly;

    [ObservableProperty]
    private string _signingBonus = string.Empty;

    [ObservableProperty]
    private string _performanceBonuses = string.Empty;

    [ObservableProperty]
    private string _releaseClause = string.Empty;

    [ObservableProperty]
    private DateTime? _clubOptionUntil;

    [ObservableProperty]
    private DateTime? _playerOptionUntil;

    public IReadOnlyList<PaymentFrequency> Frequencies { get; } = Enum.GetValues<PaymentFrequency>();

    public ContractsPageViewModel(IDatabaseEditor editor, EditorLookups lookups, Func<Task> reload)
        : base(editor, lookups, reload)
    {
    }

    protected override IEnumerable<ContractRow> ItemsFrom(EditorSnapshot snapshot) =>
        snapshot.PlayerContracts
            .Select(c => new ContractRow(c, Lookups.PlayerName(c.PlayerId), Lookups.ClubName(c.ClubId)))
            .OrderBy(r => r.Player).ThenBy(r => r.Contract.StartDate);

    protected override Guid IdOf(ContractRow item) => item.Contract.Id.Value;

    protected override void LoadForm(ContractRow item)
    {
        var c = item.Contract;
        PlayerId = c.PlayerId;
        ClubId = c.ClubId;
        StartDate = c.StartDate.ToDateTime(TimeOnly.MinValue);
        EndDate = c.EndDate.ToDateTime(TimeOnly.MinValue);
        AnnualSalary = c.AnnualSalary.ToString(CultureInfo.CurrentCulture);
        PaymentFrequency = c.PaymentFrequency;
        SigningBonus = c.SigningBonus.ToString(CultureInfo.CurrentCulture);
        PerformanceBonuses = c.PerformanceBonuses.ToString();
        ReleaseClause = c.ReleaseClause?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        ClubOptionUntil = c.ClubOptionUntil?.ToDateTime(TimeOnly.MinValue);
        PlayerOptionUntil = c.PlayerOptionUntil?.ToDateTime(TimeOnly.MinValue);
    }

    protected override void ClearForm()
    {
        PlayerId = null;
        ClubId = null;
        StartDate = EndDate = null;
        AnnualSalary = SigningBonus = PerformanceBonuses = ReleaseClause = string.Empty;
        PaymentFrequency = PaymentFrequency.Monthly;
        ClubOptionUntil = PlayerOptionUntil = null;
    }

    protected override async Task<Guid> SaveFormAsync()
    {
        var draft = new ContractDraft(
            IsNew ? null : SelectedItem?.Contract.Id,
            Required(ClubId, "club"),
            Required(PlayerId, "player"),
            RequiredDate(StartDate, "Start date"),
            RequiredDate(EndDate, "End date"),
            ParseLong(AnnualSalary, "Annual salary"),
            PaymentFrequency,
            string.IsNullOrWhiteSpace(SigningBonus) ? 0 : ParseLong(SigningBonus, "Signing bonus"),
            Domain.Contracts.PerformanceBonuses.Parse(PerformanceBonuses),
            string.IsNullOrWhiteSpace(ReleaseClause) ? null : ParseLong(ReleaseClause, "Release clause"),
            ClubOptionUntil is { } club ? DateOnly.FromDateTime(club) : null,
            PlayerOptionUntil is { } player ? DateOnly.FromDateTime(player) : null);
        return (await Editor.SaveContractAsync(draft)).Value;
    }
}
