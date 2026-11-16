using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ebm.Desktop.ViewModels.Editor;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Matches;
using Ebm.Services.Careers;
using Ebm.Services.Competitions;
using Ebm.Services.Data;

namespace Ebm.Desktop.ViewModels.Careers;

/// <summary>A row of a standings table.</summary>
public sealed record StandingLine(int Position, string Club, int Played, int Won, int Lost, int PointsFor, int PointsAgainst, int Points, bool IsUser)
{
    public string Difference => (PointsFor - PointsAgainst).ToString("+0;-0;0", CultureInfo.InvariantCulture);

    public string Name => IsUser ? $"► {Club}" : Club;
}

/// <summary>A line of the calendar news (results and events of the days just processed).</summary>
public sealed record NewsLine(string Date, string Text);

/// <summary>
/// The career screen (Step 12): start a career, move through the calendar, play the club's matches (instantly here or
/// live in the match centre), follow the standings, review the season and start the next one.
/// </summary>
public sealed partial class CareerViewModel : ObservableObject
{
    private readonly ICareerService _careers;
    private readonly ICompetitionService _competitions;
    private readonly EditorLookups _lookups;
    private readonly Func<Task> _reloadAll;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCareer), nameof(NoCareer))]
    private CareerState? _state;

    [ObservableProperty]
    private IReadOnlyList<CareerClubOption> _clubOptions = [];

    [ObservableProperty]
    private CareerClubOption? _selectedClub;

    [ObservableProperty]
    private string _managerName = "Manager";

    [ObservableProperty]
    private IReadOnlyList<CareerCompetition> _competitionsList = [];

    [ObservableProperty]
    private CareerCompetition? _selectedCompetition;

    [ObservableProperty]
    private IReadOnlyList<StandingLine> _standings = [];

    [ObservableProperty]
    private IReadOnlyList<NewsLine> _news = [];

    /// <summary>The selected cup: byes, ties round by round, the winner and the cup's past winners.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCup))]
    private IReadOnlyList<NewsLine> _cupLines = [];

    public bool HasCup => CupLines.Count > 0;

    [ObservableProperty]
    private IReadOnlyList<ClubSeasonRecord> _history = [];

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _nextMatch = string.Empty;

    [ObservableProperty]
    private bool _assistantManages;

    private bool _refreshing;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isError;

    public CareerViewModel(ICareerService careers, ICompetitionService competitions, EditorLookups lookups, Func<Task> reloadAll)
    {
        ArgumentNullException.ThrowIfNull(careers);
        ArgumentNullException.ThrowIfNull(competitions);
        ArgumentNullException.ThrowIfNull(lookups);
        ArgumentNullException.ThrowIfNull(reloadAll);
        _careers = careers;
        _competitions = competitions;
        _lookups = lookups;
        _reloadAll = reloadAll;
    }

    public bool HasCareer => State is not null;

    public bool NoCareer => State is null;

    public async Task RefreshAsync()
    {
        State = await _careers.LoadAsync();
        if (State is null)
        {
            var keep = SelectedClub?.Club.Id;
            ClubOptions = await _careers.GetClubOptionsAsync();
            SelectedClub = ClubOptions.FirstOrDefault(o => o.Club.Id == keep) ?? ClubOptions.FirstOrDefault();
            Summary = ClubOptions.Count == 0 ? "Import a dataset with a season and league entries to start a career." : "Choose a club to manage.";
            NextMatch = string.Empty;
            CompetitionsList = [];
            Standings = [];
            History = [];
            return;
        }

        var state = State;
        _refreshing = true;
        AssistantManages = state.Career.AssistantManages;
        _refreshing = false;
        Summary = $"{state.Career.ManagerName} · {state.Club.Name} · Season {state.Season.Name} · {Format(state.Today)}"
            + (state.SeasonComplete ? " · Season complete" : string.Empty);
        NextMatch = state.NextFixture is { } next
            ? $"{(next.Fixture.Date <= state.Today ? "Today" : "Next match")}: {Format(next.Fixture.Date)} · {next.HomeClub} – {next.AwayClub}"
                + next.Fixture.Stage switch
                {
                    FixtureStage.Playoffs => $" (playoffs, round {next.Fixture.Round}, game {next.Fixture.GameNumber})",
                    FixtureStage.PlayIn => $" (play-in, round {next.Fixture.Round})",
                    FixtureStage.SecondPhase => $" (second phase, round {next.Fixture.Round})",
                    FixtureStage.CupGroup => $" (cup group stage, round {next.Fixture.Round})",
                    FixtureStage.Cup => $" (cup, round {next.Fixture.Round}{(next.Fixture.GameNumber == 2 ? ", second leg" : string.Empty)})",
                    _ => string.Empty,
                }
            : state.SeasonComplete ? "The season is over: review it and start the next one." : "No match scheduled.";
        var keepCompetition = SelectedCompetition?.Competition.Id;
        CompetitionsList = state.Competitions;
        SelectedCompetition = CompetitionsList.FirstOrDefault(c => c.Competition.Id == keepCompetition)
            ?? CompetitionsList.FirstOrDefault(c => c.UserClubEntered)
            ?? CompetitionsList.FirstOrDefault();
        await LoadStandingsAsync();
        History = await _careers.GetClubHistoryAsync(state.Club.Id);
    }

    async partial void OnAssistantManagesChanged(bool value)
    {
        if (_refreshing || State is null || State.Career.AssistantManages == value)
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _careers.SetAssistantAsync(value);
            await _reloadAll();
            return value
                ? "Your assistant manages the club and plays its matches until you take over again."
                : "You manage the club again.";
        });
    }

    async partial void OnSelectedCompetitionChanged(CareerCompetition? value) => await LoadStandingsAsync();

    private async Task LoadStandingsAsync()
    {
        if (State is not { } state || SelectedCompetition is not { } competition)
        {
            Standings = [];
            CupLines = [];
            return;
        }

        if (competition.Competition.Type == CompetitionType.Cup)
        {
            await LoadCupAsync(state, competition.Competition);
            return;
        }

        CupLines = [];
        var rows = await _competitions.GetStandingsAsync(competition.Competition.Id, state.Season.Id);
        Standings =
        [
            .. rows.Select(r => new StandingLine(r.Position, _lookups.ClubName(r.ClubId), r.Played, r.Won, r.Lost, r.PointsFor, r.PointsAgainst, r.Points, r.ClubId == state.Club.Id)),
        ];
    }

    /// <summary>A cup: its group tables as standings, its ties and past winners as lines.</summary>
    private async Task LoadCupAsync(CareerState state, Competition competition)
    {
        var cup = await _competitions.GetCupAsync(competition.Id, state.Season.Id);
        Standings =
        [
            .. cup.Groups.SelectMany(g => g.Rows.Select(r => new StandingLine(
                r.Position, $"{g.Name} · {_lookups.ClubName(r.ClubId)}", r.Played, r.Won, r.Lost, r.PointsFor, r.PointsAgainst, r.Points, r.ClubId == state.Club.Id))),
        ];
        string Name(ClubId id) => _lookups.ClubName(id);
        string Tie(CupTie t) => $"{Name(t.First)} – {Name(t.Second)}"
            + (t.Games.Any(g => g.IsPlayed) ? $": {string.Join(", ", t.Games.Where(g => g.IsPlayed).Select(g => $"{g.HomeScore}–{g.AwayScore}"))}" : string.Empty)
            + (t.Legs == 2 && t.IsDecided ? $" (aggregate {t.FirstPoints}–{t.SecondPoints})" : string.Empty)
            + (t.Winner is { } w ? $" → {Name(w)}" : string.Empty);
        var history = await _competitions.GetHistoryAsync(competition.Id);
        CupLines =
        [
            .. cup.Byes.Count > 0 ? [new NewsLine("Byes", string.Join(", ", cup.Byes.Select(Name)))] : Array.Empty<NewsLine>(),
            .. cup.Ties.Select(t => new NewsLine(t.RoundName, Tie(t))),
            new NewsLine("Winner", cup.Winner is { } winner ? Name(winner) : cup.Phase == CompetitionPhase.Registration ? "Not drawn yet" : "To be decided"),
            .. history.Where(h => h.Champion is not null).Select(h => new NewsLine(h.Season.Name, $"{Name(h.Champion!.Value)} (runner-up {Name(h.RunnerUp!.Value)})")),
        ];
    }

    [RelayCommand]
    private Task StartCareerAsync() => RunAsync(async () =>
    {
        if (SelectedClub is not { } option)
        {
            return "Choose a club first.";
        }

        await _careers.StartAsync(option.Club.Id, ManagerName);
        await _reloadAll();
        return $"Career started with {option.Club.Name}.";
    });

    [RelayCommand]
    private Task ContinueAsync() => RunAsync(async () =>
    {
        var report = await _careers.ContinueAsync();
        AddNews(report.Days);
        await _reloadAll();
        return report.StoppedBecause switch
        {
            CalendarStop.UserMatch => $"{report.Days.Count} day(s) played: your club plays today. Play the match here or live in the Matches tab.",
            CalendarStop.SeasonComplete => "The season is complete. Review the final standings, then start the next season.",
            _ => $"{report.Days.Count} day(s) played.",
        };
    });

    [RelayCommand]
    private Task AdvanceDayAsync() => RunAsync(async () =>
    {
        var day = await _careers.AdvanceDayAsync();
        AddNews([day]);
        await _reloadAll();
        return $"{Format(day.Date)} processed: {day.Results.Count} match(es).";
    });

    [RelayCommand]
    private Task PlayMatchAsync() => RunAsync(async () =>
    {
        var next = State?.NextFixture;
        var match = await _careers.PlayUserMatchAsync();
        await _reloadAll();
        return next is null ? "Match played." : $"Final: {next.HomeClub} {match.HomeScore}–{match.AwayScore} {next.AwayClub}{(match.IsForfeit ? " (forfeit)" : string.Empty)}.";
    });

    [RelayCommand]
    private Task ReviewSeasonAsync() => RunAsync(async () =>
    {
        var review = await _careers.GetSeasonReviewAsync();
        News =
        [
            .. review.Competitions.Select(c => new NewsLine(
                review.Season.Name,
                $"{c.Competition.Name}: champion {c.Champion}"
                + (c.Promoted.Count > 0 ? $"; promoted {string.Join(", ", c.Promoted)}" : string.Empty)
                + (c.Relegated.Count > 0 ? $"; relegated {string.Join(", ", c.Relegated)}" : string.Empty)
                + (c.UserPosition is { } p ? $"; your club finished {p}." : "."))),
            .. News,
        ];
        return $"Season {review.Season.Name} reviewed.";
    });

    [RelayCommand]
    private Task StartNextSeasonAsync() => RunAsync(async () =>
    {
        var state = await _careers.StartNextSeasonAsync();
        await _reloadAll();
        return $"Season {state.Season.Name} created: promotions and relegations applied, schedules generated. Continue through the offseason.";
    });

    private void AddNews(IEnumerable<GameDayReport> days)
    {
        var lines = new List<NewsLine>();
        foreach (var day in days)
        {
            var date = Format(day.Date);
            lines.AddRange(day.Results.Select(r => new NewsLine(date, $"{r.Competition}: {r.Home} {r.Score} {r.Away}")));
            if (day.DateChanges.Contracts.Expired > 0)
            {
                lines.Add(new NewsLine(date, $"{day.DateChanges.Contracts.Expired} contract(s) expired."));
            }

            var ai = day.Management;
            if (ai.Renewals + ai.Releases + ai.EmergencySignings > 0)
            {
                lines.Add(new NewsLine(date, $"AI clubs: {ai.Renewals} renewal(s), {ai.Releases} release(s), {ai.EmergencySignings} emergency signing(s)."));
            }

            if (day.TransferWindow is { } window)
            {
                lines.Add(new NewsLine(date, $"AI transfer window: {window.Clubs.Sum(c => c.Transactions)} move(s)."));
            }
        }

        lines.Reverse();
        News = [.. lines.Concat(News).Take(300)];
    }

    private static string Format(DateOnly date) => date.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);

    private async Task RunAsync(Func<Task<string?>> action)
    {
        try
        {
            Message = await action();
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
