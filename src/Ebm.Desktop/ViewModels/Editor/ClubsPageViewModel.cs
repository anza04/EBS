using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Domain.Clubs;
using Ebm.Domain.Countries;
using Ebm.Services.Editing;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>Adds and edits clubs.</summary>
public sealed partial class ClubsPageViewModel : EditorPageViewModel<Club>
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _shortName = string.Empty;

    [ObservableProperty]
    private CountryId? _countryId;

    [ObservableProperty]
    private ArenaId? _homeArenaId;

    [ObservableProperty]
    private string _foundedYear = string.Empty;

    [ObservableProperty]
    private string _city = string.Empty;

    [ObservableProperty]
    private string _reputation = string.Empty;

    [ObservableProperty]
    private string _balance = string.Empty;

    [ObservableProperty]
    private string _wageBudget = string.Empty;

    [ObservableProperty]
    private BoardExpectation _boardExpectation = Club.DefaultBoardExpectation;

    public ClubsPageViewModel(IDatabaseEditor editor, EditorLookups lookups, Func<Task> reload)
        : base(editor, lookups, reload)
    {
    }

    protected override IEnumerable<Club> ItemsFrom(EditorSnapshot snapshot) => snapshot.Clubs.OrderBy(c => c.Name);

    protected override Guid IdOf(Club item) => item.Id.Value;

    protected override void LoadForm(Club item)
    {
        Name = item.Name;
        ShortName = item.ShortName;
        CountryId = item.CountryId;
        HomeArenaId = item.HomeArenaId;
        FoundedYear = item.FoundedYear?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        City = item.City ?? string.Empty;
        Reputation = item.Reputation.ToString(CultureInfo.CurrentCulture);
        Balance = item.Balance.ToString(CultureInfo.CurrentCulture);
        WageBudget = item.WageBudget.ToString(CultureInfo.CurrentCulture);
        BoardExpectation = item.BoardExpectation;
    }

    protected override void ClearForm()
    {
        Name = ShortName = FoundedYear = City = string.Empty;
        Reputation = Club.DefaultReputation.ToString(CultureInfo.CurrentCulture);
        Balance = WageBudget = "0";
        BoardExpectation = Club.DefaultBoardExpectation;
        CountryId = null;
        HomeArenaId = null;
    }

    protected override async Task<Guid> SaveFormAsync()
    {
        var draft = new ClubDraft(
            IsNew ? null : SelectedItem?.Id,
            Name,
            ShortName,
            Required(CountryId, "country"),
            HomeArenaId,
            ParseOptionalInt(FoundedYear, "Founded year"),
            City,
            ParseInt(Reputation, "Reputation"),
            ParseLong(Balance, "Balance"),
            ParseLong(WageBudget, "Wage budget"),
            BoardExpectation);
        return (await Editor.SaveClubAsync(draft)).Value;
    }
}
