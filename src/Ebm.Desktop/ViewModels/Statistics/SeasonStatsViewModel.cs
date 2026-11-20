using CommunityToolkit.Mvvm.ComponentModel;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.Matches;
using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Matches;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Ebm.Domain.Seasons;
using Ebm.Services.Matches;

namespace Ebm.Desktop.ViewModels.Statistics;

/// <summary>A competition filter entry; <see cref="Id"/> is <c>null</c> for "All competitions".</summary>
public sealed record CompetitionOption(CompetitionId? Id, string Name);

/// <summary>
/// One player's season line: games played (matches with minutes), then per-game averages or totals. Numbers stay
/// numeric so the grid sorts them correctly; the view formats them.
/// </summary>
public sealed class PlayerSeasonRowViewModel
{
    public PlayerSeasonRowViewModel(string player, IReadOnlyList<BoxScore> games, bool perGame)
    {
        var played = games.Where(b => b.Minutes > 0).ToList();
        var totals = TeamStatistics.From(played);
        double divisor = perGame && played.Count > 0 ? played.Count : 1;
        double Value(int total) => total / divisor;

        Player = player;
        Games = played.Count;
        Minutes = Value(played.Sum(b => b.Minutes));
        Points = Value(totals.Points);
        Rebounds = Value(totals.Rebounds);
        OffensiveRebounds = Value(totals.OffensiveRebounds);
        Assists = Value(totals.Assists);
        Steals = Value(totals.Steals);
        Blocks = Value(totals.Blocks);
        Turnovers = Value(totals.Turnovers);
        Fouls = Value(totals.PersonalFouls);
        FieldGoals = $"{Value(totals.FieldGoalsMade):0.#}-{Value(totals.FieldGoalsAttempted):0.#}";
        FieldGoalPercentage = totals.FieldGoalPercentage;
        Threes = $"{Value(totals.ThreePointMade):0.#}-{Value(totals.ThreePointAttempted):0.#}";
        ThreePointPercentage = totals.ThreePointPercentage;
        FreeThrows = $"{Value(totals.FreeThrowMade):0.#}-{Value(totals.FreeThrowAttempted):0.#}";
        FreeThrowPercentage = totals.FreeThrowPercentage;

        // Advanced statistics (Step 29); on-court ratings only from box scores that carry them.
        var sum = AdvancedStatistics.Total(played);
        TrueShooting = AdvancedStatistics.TrueShooting(totals.Points, totals.FieldGoalsAttempted, totals.FreeThrowAttempted);
        EffectiveFieldGoal = AdvancedStatistics.EffectiveFieldGoal(totals.FieldGoalsMade, totals.ThreePointMade, totals.FieldGoalsAttempted);
        TurnoverRate = AdvancedStatistics.TurnoverRate(totals.Turnovers, totals.FieldGoalsAttempted, totals.FreeThrowAttempted);
        PlusMinus = Value(sum.PlusMinus);
        OffensiveRating = sum.OffensivePossessions == 0 ? null : 100.0 * sum.PointsFor / sum.OffensivePossessions;
        DefensiveRating = sum.DefensivePossessions == 0 ? null : 100.0 * sum.PointsAgainst / sum.DefensivePossessions;
        Shots = [.. played.SelectMany(b => b.Shots)];
    }

    public double TrueShooting { get; }

    public double EffectiveFieldGoal { get; }

    public double TurnoverRate { get; }

    public double PlusMinus { get; }

    public double? OffensiveRating { get; }

    public double? DefensiveRating { get; }

    public double? NetRating => OffensiveRating - DefensiveRating;

    /// <summary>Every field-goal attempt of the season lines (shot chart).</summary>
    public IReadOnlyList<ShotRecord> Shots { get; }

    public string Player { get; }

    public int Games { get; }

    public double Minutes { get; }

    public double Points { get; }

    public double Rebounds { get; }

    public double OffensiveRebounds { get; }

    public double Assists { get; }

    public double Steals { get; }

    public double Blocks { get; }

    public double Turnovers { get; }

    public double Fouls { get; }

    public string FieldGoals { get; }

    public double FieldGoalPercentage { get; }

    public string Threes { get; }

    public double ThreePointPercentage { get; }

    public string FreeThrows { get; }

    public double FreeThrowPercentage { get; }
}

/// <summary>
/// Season statistics of a club's players: one row per player who appeared for the club in the season, per game or
/// totals, across all competitions or one. Read-only; built from the stored box scores.
/// </summary>
public sealed partial class SeasonStatsViewModel : ObservableObject
{
    private static readonly CompetitionOption AllCompetitions = new(null, "All competitions");

    private readonly IMatchService _matches;
    private ClubSeasonStatistics? _data;
    private bool _loading;
    private bool _seasonPicked;

    [ObservableProperty]
    private ClubId? _clubId;

    [ObservableProperty]
    private SeasonId? _seasonId;

    [ObservableProperty]
    private IReadOnlyList<Season> _seasons = [];

    [ObservableProperty]
    private IReadOnlyList<CompetitionOption> _competitions = [AllCompetitions];

    [ObservableProperty]
    private CompetitionOption? _competition = AllCompetitions;

    [ObservableProperty]
    private bool _perGame = true;

    [ObservableProperty]
    private string _title = "Choose a club";

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<PlayerSeasonRowViewModel> _players = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedZones), nameof(ShotChartTitle))]
    private PlayerSeasonRowViewModel? _selectedPlayer;

    public IReadOnlyList<ShotZoneRow> SelectedZones => SelectedPlayer is { } p ? ShotZoneRow.From(p.Shots) : [];

    public string ShotChartTitle => SelectedPlayer is { } p ? $"Shot chart · {p.Player} ({p.Shots.Count} attempts)" : "Shot chart · select a player";

    public SeasonStatsViewModel(IMatchService matches, EditorLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(matches);
        _matches = matches;
        Lookups = lookups;
    }

    public EditorLookups Lookups { get; }

    /// <summary>
    /// Reloads the season picked by hand, or else the club's newest season with matches, so the screen follows the
    /// career into a new season.
    /// </summary>
    public async Task RefreshAsync()
    {
        _data = ClubId is { } id ? await _matches.GetClubSeasonStatisticsAsync(id, _seasonPicked ? SeasonId : null) : null;
        _loading = true;
        try
        {
            Seasons = _data?.Seasons ?? [];
            SeasonId = _data?.Season?.Id;
            var previous = Competition?.Id;
            Competitions = [AllCompetitions, .. (_data?.Competitions ?? []).Select(c => new CompetitionOption(c.Id, c.Name))];
            Competition = Competitions.FirstOrDefault(c => c.Id == previous) ?? AllCompetitions;
        }
        finally
        {
            _loading = false;
        }

        BuildRows();
    }

    private void BuildRows()
    {
        Title = _data is null ? "Choose a club" : $"{_data.Club.Name} · {_data.Season?.Name ?? "no season"} statistics";
        var lines = (_data?.Lines ?? []).Where(l => Competition?.Id is not { } c || l.CompetitionId == c).ToList();
        Players =
        [
            .. lines.GroupBy(l => l.PlayerId)
                .Select(g => new PlayerSeasonRowViewModel(g.First().Player, [.. g.Select(l => l.BoxScore)], PerGame))
                .Where(r => r.Games > 0)
                .OrderByDescending(r => r.Points).ThenBy(r => r.Player),
        ];
        var team = new PlayerSeasonRowViewModel(string.Empty, [.. lines.Select(l => l.BoxScore)], perGame: false);
        Summary = _data is null
            ? string.Empty
            : Players.Count == 0
                ? "No matches played yet in this season."
                : $"{Players.Count} players used · team totals {team.Points:0} pts, {team.Rebounds:0} reb, {team.Assists:0} ast · " +
                  $"FG {team.FieldGoalPercentage:P1}, 3P {team.ThreePointPercentage:P1}, FT {team.FreeThrowPercentage:P1}";
    }

    async partial void OnClubIdChanged(ClubId? value)
    {
        _seasonPicked = false; // a new club starts on its newest season
        await RefreshAsync();
    }

    async partial void OnSeasonIdChanged(SeasonId? value)
    {
        if (!_loading)
        {
            _seasonPicked = value is not null;
            await RefreshAsync();
        }
    }

    partial void OnCompetitionChanged(CompetitionOption? value)
    {
        if (!_loading)
        {
            BuildRows();
        }
    }

    partial void OnPerGameChanged(bool value) => BuildRows();
}
