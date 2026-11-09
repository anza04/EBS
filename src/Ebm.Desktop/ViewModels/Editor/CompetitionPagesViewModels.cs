using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Domain.Competitions;
using Ebm.Domain.Countries;
using Ebm.Services.Editing;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>Adds and edits leagues.</summary>
public sealed partial class LeaguesPageViewModel : EditorPageViewModel<League>
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _shortName = string.Empty;

    [ObservableProperty]
    private CountryId? _countryId;

    public LeaguesPageViewModel(IDatabaseEditor editor, EditorLookups lookups, Func<Task> reload)
        : base(editor, lookups, reload)
    {
    }

    protected override IEnumerable<League> ItemsFrom(EditorSnapshot snapshot) => snapshot.Leagues.OrderBy(l => l.Name);

    protected override Guid IdOf(League item) => item.Id.Value;

    protected override void LoadForm(League item)
    {
        Name = item.Name;
        ShortName = item.ShortName;
        CountryId = item.CountryId;
    }

    protected override void ClearForm()
    {
        Name = ShortName = string.Empty;
        CountryId = null;
    }

    protected override async Task<Guid> SaveFormAsync() =>
        (await Editor.SaveLeagueAsync(new LeagueDraft(IsNew ? null : SelectedItem?.Id, Name, ShortName, CountryId))).Value;
}

/// <summary>Adds and edits competitions and their rules.</summary>
public sealed partial class CompetitionsPageViewModel : EditorPageViewModel<Competition>
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _shortName = string.Empty;

    [ObservableProperty]
    private CompetitionType _type;

    [ObservableProperty]
    private CountryId? _countryId;

    [ObservableProperty]
    private DivisionId? _divisionId;

    [ObservableProperty]
    private string _rulesJson = Competition.EmptyRules;

    public CompetitionsPageViewModel(IDatabaseEditor editor, EditorLookups lookups, Func<Task> reload)
        : base(editor, lookups, reload)
    {
    }

    protected override IEnumerable<Competition> ItemsFrom(EditorSnapshot snapshot) => snapshot.Competitions.OrderBy(c => c.Name);

    protected override Guid IdOf(Competition item) => item.Id.Value;

    protected override void LoadForm(Competition item)
    {
        Name = item.Name;
        ShortName = item.ShortName;
        Type = item.Type;
        CountryId = item.CountryId;
        DivisionId = item.DivisionId;
        RulesJson = item.RulesJson;
    }

    protected override void ClearForm()
    {
        Name = ShortName = string.Empty;
        Type = CompetitionType.League;
        CountryId = null;
        DivisionId = null;
        RulesJson = Competition.EmptyRules;
    }

    protected override async Task<Guid> SaveFormAsync()
    {
        var draft = new CompetitionDraft(IsNew ? null : SelectedItem?.Id, Name, ShortName, Type, CountryId, DivisionId, RulesJson);
        return (await Editor.SaveCompetitionAsync(draft)).Value;
    }
}
