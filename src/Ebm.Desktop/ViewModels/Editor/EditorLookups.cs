using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Services.Editing;

namespace Ebm.Desktop.ViewModels.Editor;

/// <summary>An entry of a selection list. <see cref="Id"/> is <c>null</c> for the "(none)" entry of optional lists.</summary>
public sealed record Option<TId>(TId? Id, string Name)
    where TId : struct;

/// <summary>Selection lists shared by the editor tabs, rebuilt whenever the data is reloaded.</summary>
public sealed partial class EditorLookups : ObservableObject
{
    private const string None = "(none)";

    [ObservableProperty]
    private IReadOnlyList<Option<CountryId>> _countries = [];

    [ObservableProperty]
    private IReadOnlyList<Option<CountryId>> _optionalCountries = [];

    [ObservableProperty]
    private IReadOnlyList<Option<ArenaId>> _optionalArenas = [];

    [ObservableProperty]
    private IReadOnlyList<Option<ClubId>> _clubs = [];

    [ObservableProperty]
    private IReadOnlyList<Option<PlayerId>> _players = [];

    [ObservableProperty]
    private IReadOnlyList<Option<DivisionId>> _optionalDivisions = [];

    public IReadOnlyList<Position> Positions { get; } = Enum.GetValues<Position>();

    public IReadOnlyList<BoardExpectation> BoardExpectations { get; } = Enum.GetValues<BoardExpectation>();

    public IReadOnlyList<CompetitionType> CompetitionTypes { get; } = Enum.GetValues<CompetitionType>();

    public IReadOnlyList<Ebm.Domain.Common.DataStatus> DataStatuses { get; } = Enum.GetValues<Ebm.Domain.Common.DataStatus>();

    public string CountryName(CountryId? id) => Countries.FirstOrDefault(c => c.Id == id)?.Name ?? string.Empty;

    public string ClubName(ClubId id) => Clubs.FirstOrDefault(c => c.Id == id)?.Name ?? string.Empty;

    public string PlayerName(PlayerId? id) => Players.FirstOrDefault(p => p.Id == id)?.Name ?? string.Empty;

    public void Refresh(EditorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Countries = [.. snapshot.Countries.OrderBy(c => c.Name).Select(c => new Option<CountryId>(c.Id, $"{c.Name} ({c.Code})"))];
        OptionalCountries = [new(null, None), .. Countries];
        OptionalArenas = [new(null, None), .. snapshot.Arenas.OrderBy(a => a.Name).Select(a => new Option<ArenaId>(a.Id, $"{a.Name}, {a.City}"))];
        Clubs = [.. snapshot.Clubs.OrderBy(c => c.Name).Select(c => new Option<ClubId>(c.Id, c.Name))];
        Players = [.. snapshot.Players.OrderBy(p => p.LastName).ThenBy(p => p.FirstName).Select(p => new Option<PlayerId>(p.Id, $"{p.LastName}, {p.FirstName}"))];
        var leagues = snapshot.Leagues.ToDictionary(l => l.Id, l => l.ShortName);
        OptionalDivisions =
        [
            new(null, None),
            .. snapshot.Divisions.OrderBy(d => leagues.GetValueOrDefault(d.LeagueId)).ThenBy(d => d.Level)
                .Select(d => new Option<DivisionId>(d.Id, $"{leagues.GetValueOrDefault(d.LeagueId)} – {d.Name}")),
        ];
    }
}
