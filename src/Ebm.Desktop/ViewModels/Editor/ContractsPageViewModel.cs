using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Services.Editing;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>A contract with the names of its club and player, for display.</summary>
public sealed record ContractRow(Contract Contract, string Player, string Club);

/// <summary>Adds player contracts and edits their dates and salary.</summary>
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
    }

    protected override void ClearForm()
    {
        PlayerId = null;
        ClubId = null;
        StartDate = EndDate = null;
        AnnualSalary = string.Empty;
    }

    protected override async Task<Guid> SaveFormAsync()
    {
        var draft = new ContractDraft(
            IsNew ? null : SelectedItem?.Contract.Id,
            Required(ClubId, "club"),
            Required(PlayerId, "player"),
            RequiredDate(StartDate, "Start date"),
            RequiredDate(EndDate, "End date"),
            ParseInt(AnnualSalary, "Annual salary"));
        return (await Editor.SaveContractAsync(draft)).Value;
    }
}
