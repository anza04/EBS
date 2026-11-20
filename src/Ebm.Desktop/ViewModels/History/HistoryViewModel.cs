using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.History;
using Ebm.Domain.Players;
using Ebm.Services.History;

namespace Ebm.Desktop.ViewModels.History;

/// <summary>An award for display.</summary>
public sealed class AwardRowViewModel(AwardLine line)
{
    public AwardLine Line => line;

    public string Season => line.Season;

    public string Competition => line.Competition;

    public string Award => line.Kind == AwardKind.AllLeagueTeam
        ? string.Create(CultureInfo.CurrentCulture, $"All-league team ({line.Rank})")
        : PlayerCatalog.DisplayName(line.Kind);

    public string Winner => line.Winner;

    public string Club => line.Club;

    public string Value => line.Kind switch
    {
        AwardKind.CoachOfTheSeason => string.Create(CultureInfo.CurrentCulture, $"{line.Value:+0;-0;0} places"),
        AwardKind.TopScorer => string.Create(CultureInfo.CurrentCulture, $"{line.Value:0.0} pts"),
        AwardKind.TopRebounder => string.Create(CultureInfo.CurrentCulture, $"{line.Value:0.0} reb"),
        AwardKind.TopAssists => string.Create(CultureInfo.CurrentCulture, $"{line.Value:0.0} ast"),
        _ => line.Value.ToString("0.0", CultureInfo.CurrentCulture),
    };
}

/// <summary>
/// The history screen (Step 32, docs/HISTORY.md): a competition's winners, awards, records and league evolution; the
/// hall of fame; a player's career (retired players included); a club's honours. Read-only.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService _history;

    [ObservableProperty]
    private CompetitionId? _competitionId;

    [ObservableProperty]
    private PlayerId? _playerId;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private IReadOnlyList<WinnerLine> _winners = [];

    [ObservableProperty]
    private IReadOnlyList<AwardRowViewModel> _awards = [];

    [ObservableProperty]
    private IReadOnlyList<RecordLine> _records = [];

    [ObservableProperty]
    private IReadOnlyList<LeagueEvolutionLine> _evolution = [];

    [ObservableProperty]
    private IReadOnlyList<HallOfFameLine> _hallOfFame = [];

    [ObservableProperty]
    private IReadOnlyList<CareerLine> _career = [];

    [ObservableProperty]
    private string _careerText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<AwardRowViewModel> _playerAwards = [];

    [ObservableProperty]
    private IReadOnlyList<Trophy> _trophies = [];

    [ObservableProperty]
    private string _clubText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<AwardRowViewModel> _clubAwards = [];

    public HistoryViewModel(IHistoryService history, EditorLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(history);
        _history = history;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    public async Task RefreshAsync()
    {
        HallOfFame = await _history.GetHallOfFameAsync();
        if (CompetitionId is { } competition)
        {
            Winners = await _history.GetWinnersAsync(competition);
            Awards = [.. (await _history.GetAwardsAsync(competition)).Select(a => new AwardRowViewModel(a))];
            Records = await _history.GetRecordsAsync(competition);
            Evolution = await _history.GetLeagueEvolutionAsync(competition);
        }
        else
        {
            (Winners, Awards, Records, Evolution) = ([], [], [], []);
        }

        if (PlayerId is { } player && await _history.GetPlayerCareerAsync(player) is { } career)
        {
            Career = [.. career.Seasons, career.Totals];
            PlayerAwards = [.. career.Awards.Select(a => new AwardRowViewModel(a))];
            var parts = new List<string> { career.Name };
            if (career.RetiredOn is { } retired)
            {
                parts.Add(string.Create(CultureInfo.CurrentCulture, $"retired {retired:d MMM yyyy}"));
            }

            if (career.HallOfFame is { } hall)
            {
                parts.Add(string.Create(CultureInfo.CurrentCulture, $"Hall of fame {hall.InductedOn:yyyy} (score {hall.Score})"));
            }

            if (career.CoachingRole is { } role)
            {
                parts.Add($"now coaching ({PlayerCatalog.DisplayName(role)})");
            }

            if (career.Academy is { } academy)
            {
                parts.Add($"{academy} academy");
            }

            parts.Add(string.Create(CultureInfo.CurrentCulture, $"{career.Titles} titles"));
            parts.Add("clubs: " + (career.Spells.Count == 0 ? "—" : string.Join(" → ", career.Spells.Select(s => s.Club))));
            CareerText = string.Join(" · ", parts);
        }
        else
        {
            (Career, PlayerAwards, CareerText) = ([], [], "Choose a player (retired players included).");
        }

        if (ClubId is { } club)
        {
            var honours = await _history.GetClubHonoursAsync(club);
            Trophies = honours.Trophies;
            ClubAwards = [.. honours.Awards.Select(a => new AwardRowViewModel(a))];
            ClubText = string.Create(CultureInfo.CurrentCulture, $"{honours.Trophies.Count} trophies · {honours.Awards.Count} awards · {honours.AcademyPlayers} academy players");
        }
        else
        {
            (Trophies, ClubAwards, ClubText) = ([], [], "Choose a club.");
        }
    }

    async partial void OnCompetitionIdChanged(CompetitionId? value) => await RefreshAsync();

    async partial void OnPlayerIdChanged(PlayerId? value) => await RefreshAsync();

    async partial void OnClubIdChanged(ClubId? value) => await RefreshAsync();
}
