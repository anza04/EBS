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
    }

    protected override void ClearForm()
    {
        Name = ShortName = FoundedYear = string.Empty;
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
            ParseOptionalInt(FoundedYear, "Founded year"));
        return (await Editor.SaveClubAsync(draft)).Value;
    }
}
