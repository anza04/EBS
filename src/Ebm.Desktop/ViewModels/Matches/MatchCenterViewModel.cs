using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Desktop.ViewModels.Players;
using Ebm.Domain.Common;
using Ebm.Domain.Matches;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Ebm.Domain.Tactics;
using Ebm.Services.Competitions;
using Ebm.Services.Data;
using Ebm.Services.Matches;

namespace Ebm.Desktop.ViewModels.Matches;

/// <summary>Which side the user manages in a live match.</summary>
public enum ManagedSide
{
    None,
    Home,
    Away,
}

/// <summary>One fixture in the schedule grid.</summary>
public sealed record FixtureRow(FixtureLine Line)
{
    public string Date => Line.Fixture.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public string Round => Line.Fixture.Stage == FixtureStage.Playoffs ? $"PO {Line.Fixture.Round}-{Line.Fixture.GameNumber}" : $"{Line.Fixture.Round}";

    public string Home => Line.HomeClub;

    public string Away => Line.AwayClub;

    public string Result => Line.Match is { } m ? $"{m.HomeScore}–{m.AwayScore}{(m.Overtimes > 0 ? $" ({m.Overtimes} OT)" : string.Empty)}" : "—";
}

/// <summary>A player of a matchday squad in the preview.</summary>
public sealed record SheetRow(string Player, string Position, string Role, int Overall);

/// <summary>One play-by-play line, newest first.</summary>
public sealed record EventRow(string Time, string Score, string Team, string Text);

/// <summary>One box-score line (live or stored). Energy and plus-minus are known during a live match only.</summary>
public sealed record BoxRow(string Player, bool OnCourt, BoxScore Box, int? PlusMinus, double? Energy, bool FouledOut)
{
    public string Name => (OnCourt ? "● " : string.Empty) + Player + (FouledOut ? " (5 PF)" : string.Empty);

    public int Minutes => Box.Minutes;

    public int Points => Box.Points;

    public string FieldGoals => $"{Box.TwoPointMade + Box.ThreePointMade}-{Box.TwoPointAttempted + Box.ThreePointAttempted}";

    public string Threes => $"{Box.ThreePointMade}-{Box.ThreePointAttempted}";

    public string FreeThrows => $"{Box.FreeThrowMade}-{Box.FreeThrowAttempted}";

    public int Rebounds => Box.Rebounds;

    public int Assists => Box.Assists;

    public int Steals => Box.Steals;

    public int Blocks => Box.Blocks;

    public int Turnovers => Box.Turnovers;

    public int Fouls => Box.PersonalFouls;

    public string PlusMinusText => PlusMinus is { } pm ? pm.ToString("+0;-0;0", CultureInfo.InvariantCulture) : "—";

    public string EnergyText => Energy is { } e ? $"{e:0}" : "—";
}

/// <summary>A team-statistics comparison row.</summary>
public sealed record TeamStatRow(string Label, string Home, string Away);

/// <summary>
/// The match centre (Step 11): schedule, preview, live match (score, clock, play-by-play, substitutions, timeouts,
/// tactical changes, live box score) and stored match reports. Matches run on <see cref="IMatchService"/>.
/// </summary>
public sealed partial class MatchCenterViewModel : ObservableObject
{
    private readonly IMatchService _matches;
    private readonly ICompetitionService _competitions;
    private readonly Func<Task> _reloadAll;
    private MatchSession? _session;
    private MatchPreview? _preview;

    [ObservableProperty]
    private IReadOnlyList<CompetitionSeason> _competitionSeasons = [];

    [ObservableProperty]
    private CompetitionSeason? _selectedCompetition;

    [ObservableProperty]
    private IReadOnlyList<FixtureRow> _fixtures = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private FixtureRow? _selectedFixture;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanIntervene))]
    private ManagedSide _managed = ManagedSide.Home;

    [ObservableProperty]
    private string _title = "Choose a competition";

    [ObservableProperty]
    private IReadOnlyList<SheetRow> _homeSheet = [];

    [ObservableProperty]
    private IReadOnlyList<SheetRow> _awaySheet = [];

    [ObservableProperty]
    private string _homeTactics = string.Empty;

    [ObservableProperty]
    private string _awayTactics = string.Empty;

    [ObservableProperty]
    private string _homeName = "Home";

    [ObservableProperty]
    private string _awayName = "Away";

    [ObservableProperty]
    private string _scoreText = "–";

    [ObservableProperty]
    private string _clockText = string.Empty;

    [ObservableProperty]
    private string _homeInfo = string.Empty;

    [ObservableProperty]
    private string _awayInfo = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<EventRow> _playByPlay = [];

    [ObservableProperty]
    private IReadOnlyList<BoxRow> _homeBox = [];

    [ObservableProperty]
    private IReadOnlyList<BoxRow> _awayBox = [];

    [ObservableProperty]
    private IReadOnlyList<TeamStatRow> _teamStats = [];

    [ObservableProperty]
    private IReadOnlyList<InfoLine> _report = [];

    [ObservableProperty]
    private IReadOnlyList<Option<PlayerId>> _onCourt = [];

    [ObservableProperty]
    private IReadOnlyList<Option<PlayerId>> _bench = [];

    [ObservableProperty]
    private PlayerId? _subOut;

    [ObservableProperty]
    private PlayerId? _subIn;

    [ObservableProperty]
    private OffensiveSystem _liveOffense;

    [ObservableProperty]
    private DefensiveSystem _liveDefense;

    [ObservableProperty]
    private int _liveTempo = TacticLevel.Normal;

    [ObservableProperty]
    private int _liveIntensity = TacticLevel.Normal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLive), nameof(CanIntervene), nameof(CanStart), nameof(CanBrowse))]
    private bool _inProgress;

    [ObservableProperty]
    private string? _message;

    /// <summary>Shown view: 0 preview, 1 play-by-play, 2 box score, 3 team stats, 4 report.</summary>
    [ObservableProperty]
    private int _selectedView;

    [ObservableProperty]
    private bool _isError;

    public MatchCenterViewModel(IMatchService matches, ICompetitionService competitions, Func<Task> reloadAll)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(competitions);
        ArgumentNullException.ThrowIfNull(reloadAll);
        _matches = matches;
        _competitions = competitions;
        _reloadAll = reloadAll;
    }

    public IReadOnlyList<ManagedSide> Sides { get; } = Enum.GetValues<ManagedSide>();

    public IReadOnlyList<OffensiveSystem> OffensiveSystems { get; } = Enum.GetValues<OffensiveSystem>();

    public IReadOnlyList<DefensiveSystem> DefensiveSystems { get; } = Enum.GetValues<DefensiveSystem>();

    public IReadOnlyList<int> Levels { get; } = TacticLevel.All;

    public bool IsLive => InProgress;

    /// <summary>Other fixtures can be opened while no match is in progress.</summary>
    public bool CanBrowse => !InProgress;

    /// <summary>Interventions are possible in a live match where the user manages a side.</summary>
    public bool CanIntervene => InProgress && ManagedTeam is not null;

    public bool CanStart => !InProgress && SelectedFixture is { Line.Match: null };

    /// <summary>The current or last match started on this screen.</summary>
    public MatchSession? Session => _session;

    private TeamSide? ManagedTeam => Managed switch
    {
        ManagedSide.Home => TeamSide.Home,
        ManagedSide.Away => TeamSide.Away,
        _ => null,
    };

    public async Task RefreshAsync()
    {
        var keep = SelectedCompetition;
        CompetitionSeasons = await _matches.GetCompetitionSeasonsAsync();
        SelectedCompetition = keep is null
            ? CompetitionSeasons.FirstOrDefault()
            : CompetitionSeasons.FirstOrDefault(c => c.Competition.Id == keep.Competition.Id && c.Season.Id == keep.Season.Id);
        await LoadFixturesAsync();
    }

    async partial void OnSelectedCompetitionChanged(CompetitionSeason? value)
    {
        if (!InProgress)
        {
            await LoadFixturesAsync();
        }
    }

    async partial void OnSelectedFixtureChanged(FixtureRow? value)
    {
        if (!InProgress)
        {
            await ShowSelectedAsync();
        }
    }

    private async Task LoadFixturesAsync()
    {
        var keep = SelectedFixture?.Line.Fixture.Id;
        Fixtures = SelectedCompetition is { } c
            ? [.. (await _matches.GetFixturesAsync(c.Competition.Id, c.Season.Id)).Select(l => new FixtureRow(l))]
            : [];
        SelectedFixture = Fixtures.FirstOrDefault(f => f.Line.Fixture.Id == keep) ?? Fixtures.FirstOrDefault(f => f.Line.Match is null) ?? Fixtures.FirstOrDefault();
        if (SelectedFixture is null)
        {
            Title = SelectedCompetition is null ? "Choose a competition" : "No fixtures yet: create the schedule.";
        }
    }

    /// <summary>Shows the stored report of a played fixture, or the preview of an unplayed one.</summary>
    public async Task ShowSelectedAsync()
    {
        _preview = null;
        Message = null;
        IsError = false;
        Report = [];
        PlayByPlay = [];
        HomeBox = AwayBox = [];
        TeamStats = [];
        HomeSheet = AwaySheet = [];
        if (SelectedFixture is not { } row)
        {
            return;
        }

        if (row.Line.Match is { } match)
        {
            await ShowReportAsync(match.Id);
            return;
        }

        Title = $"Preview · {row.Home} – {row.Away} · {row.Date}";
        SelectedView = 0;
        (HomeName, AwayName, ScoreText, ClockText) = (row.Home, row.Away, "vs", "Not played yet");
        HomeTactics = AwayTactics = HomeInfo = AwayInfo = string.Empty;
        await RunAsync(async () =>
        {
            _preview = await _matches.GetPreviewAsync(row.Line.Fixture.Id) ?? throw new DomainException("The fixture no longer exists.");
            HomeName = _preview.Home.Name;
            AwayName = _preview.Away.Name;
            Title = $"Preview · {_preview.Competition} · {row.Date}";
            HomeSheet = Sheet(_preview.Home);
            AwaySheet = Sheet(_preview.Away);
            HomeTactics = TacticsText(_preview.Home.Tactics);
            AwayTactics = TacticsText(_preview.Away.Tactics);
            return null;
        });
    }

    private async Task ShowReportAsync(MatchId id)
    {
        if (await _matches.GetReportAsync(id) is not { } report)
        {
            return;
        }

        var m = report.Match;
        HomeName = report.Home.Name;
        AwayName = report.Away.Name;
        Title = $"Match report · {report.Competition} · {report.Fixture.Date:yyyy-MM-dd}";
        SelectedView = 2;
        ScoreText = $"{m.HomeScore} – {m.AwayScore}";
        ClockText = m.Overtimes > 0 ? $"Final ({m.Overtimes} OT)" : "Final";
        HomeBox = [.. report.HomeLines.Select(l => new BoxRow(l.Player, false, l.BoxScore, null, null, l.BoxScore.PersonalFouls >= 5))];
        AwayBox = [.. report.AwayLines.Select(l => new BoxRow(l.Player, false, l.BoxScore, null, null, l.BoxScore.PersonalFouls >= 5))];
        TeamStats = Compare(report.HomeStatistics, report.AwayStatistics);
        Report = ReportLines(HomeName, AwayName, m.HomeScore, m.AwayScore, m.Overtimes, report.HomeLines.Select(l => (l.Player, l.BoxScore)), report.AwayLines.Select(l => (l.Player, l.BoxScore)));
        HomeInfo = AwayInfo = string.Empty;
    }

    [RelayCommand]
    private Task CreateScheduleAsync() => RunAsync(async () =>
    {
        var c = SelectedCompetition ?? throw new DomainValidationException("Choose a competition.");
        var progress = await _competitions.GenerateScheduleAsync(c.Competition.Id, c.Season.Id);
        await LoadFixturesAsync();
        return $"{progress.FixturesCreated} fixtures scheduled.";
    });

    /// <summary>Simulates the selected fixture to the end at once and stores it.</summary>
    [RelayCommand]
    private Task PlayInstantlyAsync() => RunAsync(async () =>
    {
        var row = SelectedFixture ?? throw new DomainValidationException("Choose a fixture.");
        var report = await _matches.PlayAsync(row.Line.Fixture.Id);
        await AfterSaveAsync();
        return $"Final: {report.Home.Name} {report.Match.HomeScore}–{report.Match.AwayScore} {report.Away.Name}. Result saved.";
    });

    /// <summary>Starts the selected fixture possession by possession; the managed side takes the user's decisions.</summary>
    [RelayCommand]
    private Task StartMatchAsync() => RunAsync(async () =>
    {
        var row = SelectedFixture ?? throw new DomainValidationException("Choose a fixture.");
        _session = await _matches.StartAsync(row.Line.Fixture.Id, ManagedTeam is null ? SimulationMode.Commentary : SimulationMode.Live);
        var sim = _session.Simulation;
        if (ManagedTeam is { } side)
        {
            sim.SetAutomatic(side, false);
            var t = sim.Tactics(side);
            (LiveOffense, LiveDefense, LiveTempo, LiveIntensity) = (t.OffensiveSystem, t.DefensiveSystem, t.Offense.Tempo, t.Defense.Intensity);
        }

        InProgress = true;
        SelectedView = 1;
        Report = [];
        sim.Step();
        UpdateLive();
        return null;
    });

    [RelayCommand]
    private Task NextPossessionAsync() => AdvanceAsync(sim => sim.Step());

    [RelayCommand]
    private Task ToEndOfQuarterAsync() => AdvanceAsync(sim => sim.RunToEndOfPeriod());

    [RelayCommand]
    private Task ToEndOfMatchAsync() => AdvanceAsync(sim => sim.RunToEnd());

    [RelayCommand]
    private Task SubstituteAsync() => Intervene((sim, side) =>
    {
        sim.Substitute(side, SubOut ?? throw new DomainValidationException("Choose the player to take off."), SubIn ?? throw new DomainValidationException("Choose the player to bring on."));
        return "Substitution made.";
    });

    [RelayCommand]
    private Task TimeoutAsync() => Intervene((sim, side) =>
    {
        sim.CallTimeout(side);
        return "Timeout called.";
    });

    [RelayCommand]
    private Task ApplyTacticsAsync() => Intervene((sim, side) =>
    {
        var t = sim.Tactics(side);
        sim.ChangeTactics(side, t with
        {
            OffensiveSystem = LiveOffense,
            DefensiveSystem = LiveDefense,
            Offense = t.Offense with { Tempo = LiveTempo },
            Defense = t.Defense with { Intensity = LiveIntensity },
        });
        return "Tactics changed from the next possession.";
    });

    private Task Intervene(Func<MatchSimulation, TeamSide, string> action) => RunAsync(() =>
    {
        var sim = _session?.Simulation ?? throw new DomainValidationException("Start a match first.");
        var side = ManagedTeam ?? throw new DomainValidationException("Manage a side to make decisions.");
        var done = action(sim, side);
        UpdateLive();
        return Task.FromResult<string?>(done);
    });

    private Task AdvanceAsync(Action<MatchSimulation> advance) => RunAsync(async () =>
    {
        var session = _session ?? throw new DomainValidationException("Start a match first.");
        advance(session.Simulation);
        UpdateLive();
        if (!session.Simulation.IsFinished)
        {
            return null;
        }

        // The result is saved as soon as the match ends; the final live view (play-by-play, quarters) stays on screen.
        await _matches.SaveAsync(session);
        await _reloadAll();
        await LoadFixturesAsync();
        InProgress = false;
        UpdateLive();
        return $"Final: {HomeName} {session.Simulation.HomeScore}–{session.Simulation.AwayScore} {AwayName}. Result saved; the box score can be reopened from the schedule.";
    });

    private async Task AfterSaveAsync()
    {
        await _reloadAll();
        await LoadFixturesAsync();
        await ShowSelectedAsync();
    }

    /// <summary>Refreshes scoreboard, play-by-play, box scores, team stats and the substitution lists from the simulation.</summary>
    private void UpdateLive()
    {
        if (_session?.Simulation is not { } sim)
        {
            return;
        }

        HomeName = sim.TeamName(TeamSide.Home);
        AwayName = sim.TeamName(TeamSide.Away);
        Title = $"{(sim.IsFinished ? "Final" : "Live")} · {_session.Competition} · {_session.Fixture.Date:yyyy-MM-dd}";
        ScoreText = $"{sim.HomeScore} – {sim.AwayScore}";
        ClockText = sim.IsFinished
            ? sim.Overtimes > 0 ? $"Final ({sim.Overtimes} OT)" : "Final"
            : $"{MatchEvent.PeriodName(sim.Period)} {sim.SecondsLeft / 60:00}:{sim.SecondsLeft % 60:00}";
        HomeInfo = $"Team fouls {sim.TeamFouls(TeamSide.Home)} · timeouts {sim.TimeoutsLeft(TeamSide.Home)}";
        AwayInfo = $"Team fouls {sim.TeamFouls(TeamSide.Away)} · timeouts {sim.TimeoutsLeft(TeamSide.Away)}";
        PlayByPlay = [.. sim.Events.Reverse().Select(e => new EventRow(
            e.Time,
            $"{e.HomeScore}–{e.AwayScore}",
            e.Side switch { TeamSide.Home => HomeName, TeamSide.Away => AwayName, _ => string.Empty },
            e.Text))];
        HomeBox = Box(sim.Lines(TeamSide.Home));
        AwayBox = Box(sim.Lines(TeamSide.Away));
        var home = TeamStatistics.From(sim.Lines(TeamSide.Home).Select(l => l.ToBoxScore()));
        var away = TeamStatistics.From(sim.Lines(TeamSide.Away).Select(l => l.ToBoxScore()));
        TeamStats = Compare(home, away);
        Report = ReportLines(
            HomeName,
            AwayName,
            sim.HomeScore,
            sim.AwayScore,
            sim.Overtimes,
            sim.Lines(TeamSide.Home).Select(l => (l.Player.FullName, l.ToBoxScore())),
            sim.Lines(TeamSide.Away).Select(l => (l.Player.FullName, l.ToBoxScore())),
            Quarters(sim));
        if (ManagedTeam is { } side)
        {
            OnCourt = [.. sim.OnCourt(side).Select(l => new Option<PlayerId>(l.Player.Id, $"{l.Player.FullName} · {l.Energy:0}% · {l.PersonalFouls} PF"))];
            Bench = [.. sim.Lines(side).Where(l => !l.IsOnCourt && !l.FouledOut).Select(l => new Option<PlayerId>(l.Player.Id, $"{l.Player.FullName} · {l.Energy:0}% · {l.PersonalFouls} PF"))];
        }
        else
        {
            OnCourt = Bench = [];
        }
    }

    private static IReadOnlyList<BoxRow> Box(IReadOnlyList<PlayerLine> lines) =>
        [.. lines.OrderByDescending(l => l.IsStarter).ThenByDescending(l => l.SecondsPlayed)
            .Select(l => new BoxRow(l.Player.FullName, l.IsOnCourt, l.ToBoxScore(), l.PlusMinus, l.Energy, l.FouledOut))];

    private static IReadOnlyList<SheetRow> Sheet(TeamSheet sheet) =>
        [.. sheet.Players.OrderBy(p => p.StartingPosition is null).ThenBy(p => p.StartingPosition).ThenBy(p => p.Priority)
            .Select(p => new SheetRow(
                p.Player.FullName,
                PlayerCatalog.DisplayName(p.StartingPosition ?? p.Player.Position),
                p.StartingPosition is null ? $"Bench {p.Priority}" : "Starter",
                Ebm.Domain.Players.Ratings.PlayerRatingCalculator.Default.Evaluate(p.Player).Overall))];

    private static string TacticsText(TeamTactics t) =>
        $"{PlayerCatalog.DisplayName(t.OffensiveSystem)} · {PlayerCatalog.DisplayName(t.DefensiveSystem)} · tempo {t.Offense.Tempo} · intensity {t.Defense.Intensity}";

    private static IReadOnlyList<TeamStatRow> Compare(TeamStatistics h, TeamStatistics a)
    {
        static string Pct(int made, int attempted, double pct) => $"{made}/{attempted} ({pct:P0})";
        return
        [
            new("Points", $"{h.Points}", $"{a.Points}"),
            new("Field goals", Pct(h.FieldGoalsMade, h.FieldGoalsAttempted, h.FieldGoalPercentage), Pct(a.FieldGoalsMade, a.FieldGoalsAttempted, a.FieldGoalPercentage)),
            new("Two-pointers", Pct(h.TwoPointMade, h.TwoPointAttempted, h.TwoPointPercentage), Pct(a.TwoPointMade, a.TwoPointAttempted, a.TwoPointPercentage)),
            new("Three-pointers", Pct(h.ThreePointMade, h.ThreePointAttempted, h.ThreePointPercentage), Pct(a.ThreePointMade, a.ThreePointAttempted, a.ThreePointPercentage)),
            new("Free throws", Pct(h.FreeThrowMade, h.FreeThrowAttempted, h.FreeThrowPercentage), Pct(a.FreeThrowMade, a.FreeThrowAttempted, a.FreeThrowPercentage)),
            new("Rebounds (off.)", $"{h.Rebounds} ({h.OffensiveRebounds})", $"{a.Rebounds} ({a.OffensiveRebounds})"),
            new("Assists", $"{h.Assists}", $"{a.Assists}"),
            new("Steals", $"{h.Steals}", $"{a.Steals}"),
            new("Blocks", $"{h.Blocks}", $"{a.Blocks}"),
            new("Turnovers", $"{h.Turnovers}", $"{a.Turnovers}"),
            new("Fouls", $"{h.PersonalFouls}", $"{a.PersonalFouls}"),
        ];
    }

    /// <summary>Score at the end of each period, from the play-by-play.</summary>
    private static IReadOnlyList<InfoLine> Quarters(MatchSimulation sim)
    {
        var lines = new List<InfoLine>();
        var (home, away) = (0, 0);
        foreach (var end in sim.Events.Where(e => e.Kind == MatchEventKind.PeriodEnd && e.Side is null && e.Text.StartsWith("End of", StringComparison.Ordinal)))
        {
            lines.Add(new InfoLine($"{MatchEvent.PeriodName(end.Period)}", $"{end.HomeScore - home}–{end.AwayScore - away}"));
            (home, away) = (end.HomeScore, end.AwayScore);
        }

        return lines;
    }

    private static IReadOnlyList<InfoLine> ReportLines(
        string homeName,
        string awayName,
        int homeScore,
        int awayScore,
        int overtimes,
        IEnumerable<(string Player, BoxScore Box)> home,
        IEnumerable<(string Player, BoxScore Box)> away,
        IReadOnlyList<InfoLine>? quarters = null)
    {
        var h = home.ToList();
        var a = away.ToList();
        string Leader(IEnumerable<(string Player, BoxScore Box)> lines, Func<BoxScore, int> stat) =>
            lines.MaxBy(l => stat(l.Box)) is { Player: not null } best ? $"{best.Player} {stat(best.Box)}" : "—";
        var winner = homeScore > awayScore ? homeName : awayName;
        List<InfoLine> lines =
        [
            new("Result", $"{homeName} {homeScore}–{awayScore} {awayName}{(overtimes > 0 ? $" after {overtimes} overtime(s)" : string.Empty)}"),
            new("Winner", homeScore == awayScore ? "—" : $"{winner} by {Math.Abs(homeScore - awayScore)}"),
            new($"Top scorer {homeName}", Leader(h, b => b.Points)),
            new($"Top scorer {awayName}", Leader(a, b => b.Points)),
            new("Most rebounds", Leader(h.Concat(a), b => b.Rebounds)),
            new("Most assists", Leader(h.Concat(a), b => b.Assists)),
        ];
        lines.AddRange(quarters ?? []);
        return lines;
    }

    private async Task RunAsync(Func<Task<string?>> action)
    {
        try
        {
            var done = await action();
            Message = done;
            IsError = false;
        }
        catch (DomainException ex)
        {
            Message = ex.Message;
            IsError = true;
        }
        catch (DataConstraintException ex)
        {
            Message = ex.Message;
            IsError = true;
        }
    }
}
