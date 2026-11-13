using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions.Rules;
using Ebm.Domain.Matches;

namespace Ebm.Domain.Competitions.Engine;

/// <summary>Where a competition season stands.</summary>
public enum SeasonPhase
{
    /// <summary>No schedule yet: clubs can still be registered.</summary>
    Registration,
    RegularSeason,
    SecondPhase,
    PlayIn,
    Playoffs,
    Completed,
}

/// <summary>A scheduled game of a competition season; the scores are <c>null</c> until it is played.</summary>
public sealed record SeasonGame(FixtureStage Stage, int Round, int GameNumber, ClubId Home, ClubId Away, int? HomeScore = null, int? AwayScore = null)
{
    public bool IsPlayed => HomeScore is not null && AwayScore is not null;

    public ClubId? Winner => IsPlayed ? (HomeScore > AwayScore ? Home : Away) : null;

    public ClubId? Loser => IsPlayed ? (HomeScore > AwayScore ? Away : Home) : null;

    internal GameResult Result => new(Home, Away, HomeScore!.Value, AwayScore!.Value);
}

/// <summary>A game the season needs scheduled next.</summary>
public sealed record SeasonGameRequest(FixtureStage Stage, int Round, int GameNumber, ClubId Home, ClubId Away);

/// <summary>A ranked second-phase group.</summary>
public sealed record GroupTable(string Name, IReadOnlyList<StandingRow> Rows);

/// <summary>
/// One competition season derived from its rules, clubs and games: regular season, optional second phase
/// (groups from the first-phase table), optional play-in and playoffs. Pure and stateless: it ranks the tables,
/// reports the phase, the games to schedule next and the outcome. Documented in docs/COMPETITION_FORMATS.md.
/// </summary>
public sealed class CompetitionSeason
{
    private readonly CompetitionRules _rules;
    private readonly IReadOnlyDictionary<ClubId, string> _countries;
    private readonly List<SeasonGame> _games;
    private IReadOnlyList<StandingRow>? _firstPhase;
    private IReadOnlyList<GroupTable>? _groups;
    private IReadOnlyList<StandingRow>? _final;

    /// <param name="clubs">The entered clubs and their country codes (used by groups formed per country).</param>
    public CompetitionSeason(CompetitionRules rules, IReadOnlyDictionary<ClubId, string> clubs, IEnumerable<SeasonGame> games)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(clubs);
        ArgumentNullException.ThrowIfNull(games);
        _rules = rules;
        _countries = clubs;
        _games = [.. games];
        Clubs = [.. clubs.Keys.OrderBy(c => c.Value)];
    }

    public IReadOnlyList<ClubId> Clubs { get; }

    public SeasonPhase Phase
    {
        get
        {
            if (!Stage(FixtureStage.RegularSeason).Any())
            {
                return SeasonPhase.Registration;
            }

            if (!StageOver(FixtureStage.RegularSeason))
            {
                return SeasonPhase.RegularSeason;
            }

            if (_rules.SecondPhase is not null && !StageOver(FixtureStage.SecondPhase))
            {
                return SeasonPhase.SecondPhase;
            }

            if (_rules.Playoffs is not { } playoffs)
            {
                return SeasonPhase.Completed;
            }

            if (playoffs.PlayIn && PlayInSeeds() is null)
            {
                return SeasonPhase.PlayIn;
            }

            return Bracket()!.IsComplete ? SeasonPhase.Completed : SeasonPhase.Playoffs;
        }
    }

    /// <summary>The regular-season table.</summary>
    public IReadOnlyList<StandingRow> FirstPhaseTable() =>
        _firstPhase ??= StandingsCalculator.Calculate(Clubs, Played(FixtureStage.RegularSeason), _rules);

    /// <summary>The second-phase groups once the regular season is over (empty without a second phase).</summary>
    public IReadOnlyList<GroupTable> GroupTables()
    {
        if (_groups is not null)
        {
            return _groups;
        }

        if (_rules.SecondPhase is not { } phase || !StageOver(FixtureStage.RegularSeason))
        {
            return [];
        }

        var first = FirstPhaseTable();
        var regular = Played(FixtureStage.RegularSeason).ToList();
        var second = Played(FixtureStage.SecondPhase).ToList();
        var tables = new List<GroupTable>();
        foreach (var group in phase.Groups)
        {
            var members = Members(group, first, phase.ByCountry);
            var set = members.ToHashSet();
            var counted = phase.CarryOver switch
            {
                CarryOver.All => regular.Where(g => set.Contains(g.Home) || set.Contains(g.Away)),
                CarryOver.WithinGroup => regular.Where(g => set.Contains(g.Home) && set.Contains(g.Away)),
                _ => [],
            };
            tables.Add(new GroupTable(group.Name, StandingsCalculator.Calculate(
                members, counted.Concat(second.Where(g => set.Contains(g.Home) && set.Contains(g.Away))), _rules, countGamesAgainstOthers: true)));
        }

        return _groups = tables;
    }

    /// <summary>
    /// The ranking that decides the playoffs, promotion and relegation: the first-phase table, or with a second phase
    /// the groups in order followed by the clubs in no group (by first-phase position), renumbered.
    /// </summary>
    public IReadOnlyList<StandingRow> FinalTable()
    {
        if (_final is not null)
        {
            return _final;
        }

        var groups = GroupTables();
        if (groups.Count == 0)
        {
            return _final = FirstPhaseTable();
        }

        var grouped = groups.SelectMany(g => g.Rows).ToList();
        var inGroup = grouped.Select(r => r.ClubId).ToHashSet();
        return _final =
        [
            .. grouped.Concat(FirstPhaseTable().Where(r => !inGroup.Contains(r.ClubId)))
                .Select((r, i) => r with { Position = i + 1 }),
        ];
    }

    /// <summary>The playoff bracket once its seeds are known; <c>null</c> before that or without playoffs.</summary>
    public PlayoffBracket? Bracket()
    {
        if (_rules.Playoffs is not { } playoffs || !TableFinal)
        {
            return null;
        }

        var seeds = FinalTable().Skip(playoffs.FromPosition - 1).Take(playoffs.Teams).Select(r => r.ClubId).ToList();
        if (playoffs.PlayIn)
        {
            if (PlayInSeeds() is not { } playIn)
            {
                return null;
            }

            seeds[^2] = playIn.Seventh;
            seeds[^1] = playIn.Eighth;
        }

        return PlayoffBracket.Build(
            seeds,
            playoffs,
            Stage(FixtureStage.Playoffs).Select(g => new PlayoffGame(g.Round, g.GameNumber, g.Home, g.Away, g.HomeScore, g.AwayScore)));
    }

    /// <summary>Games to schedule now: the whole second phase, the next play-in round, or the next playoff games.</summary>
    public IReadOnlyList<SeasonGameRequest> NextGames()
    {
        var phase = Phase;
        if (phase == SeasonPhase.SecondPhase && !Stage(FixtureStage.SecondPhase).Any())
        {
            var rules = _rules.SecondPhase!;
            return
            [
                .. GroupTables().SelectMany(g => RoundRobinScheduler.Generate([.. g.Rows.Select(r => r.ClubId)], rules.Legs))
                    .Select(g => new SeasonGameRequest(FixtureStage.SecondPhase, g.Round, 1, g.Home, g.Away)),
            ];
        }

        if (phase == SeasonPhase.PlayIn)
        {
            return PlayInRequests();
        }

        if (phase == SeasonPhase.Playoffs)
        {
            return [.. Bracket()!.NextGames().Select(r => new SeasonGameRequest(FixtureStage.Playoffs, r.Round, r.GameNumber, r.Home, r.Away))];
        }

        return [];
    }

    /// <summary>Champion, promoted and relegated clubs of a completed season.</summary>
    public SeasonOutcome Outcome()
    {
        if (Phase != SeasonPhase.Completed)
        {
            throw new DomainValidationException("The season is not over yet.");
        }

        var table = FinalTable();
        _rules.ValidateForClubs(table.Count);
        var bracket = Bracket();
        var champion = bracket?.Champion ?? table[0].ClubId;
        List<ClubId> promoted = [.. table.Take(_rules.Promotion.Direct).Select(r => r.ClubId)];
        if (_rules.Promotion.PlayoffPromotions > 0)
        {
            promoted.AddRange(bracket!.TopPlaces(_rules.Promotion.PlayoffPromotions));
        }

        var relegated = table.TakeLast(_rules.Relegation.Direct).Select(r => r.ClubId).ToList();
        return new SeasonOutcome(champion, promoted, relegated);
    }

    /// <summary>Whether the final table can no longer change (every table game played).</summary>
    private bool TableFinal =>
        StageOver(FixtureStage.RegularSeason) && (_rules.SecondPhase is null || StageOver(FixtureStage.SecondPhase));

    private IEnumerable<SeasonGame> Stage(FixtureStage stage) => _games.Where(g => g.Stage == stage);

    private IEnumerable<GameResult> Played(FixtureStage stage) => Stage(stage).Where(g => g.IsPlayed).Select(g => g.Result);

    private bool StageOver(FixtureStage stage) => Stage(stage).Any() && Stage(stage).All(g => g.IsPlayed);

    private List<ClubId> Members(PhaseGroup group, IReadOnlyList<StandingRow> first, bool byCountry)
    {
        if (!byCountry)
        {
            return [.. first.Skip(group.From - 1).Take(group.To - group.From + 1).Select(r => r.ClubId)];
        }

        // Positions within each country, the countries' clubs interleaved by their overall first-phase position.
        return
        [
            .. first.GroupBy(r => _countries[r.ClubId])
                .SelectMany(country => country.Skip(group.From - 1).Take(group.To - group.From + 1))
                .OrderBy(r => r.Position)
                .Select(r => r.ClubId),
        ];
    }

    /// <summary>The four play-in clubs by table position (7, 8, 9, 10 for 8 playoff teams).</summary>
    private IReadOnlyList<ClubId> PlayInClubs()
    {
        var playoffs = _rules.Playoffs!;
        return [.. FinalTable().Skip(playoffs.LastPosition - 2).Take(4).Select(r => r.ClubId)];
    }

    /// <summary>The play-in winners (seeds N-1 and N), or <c>null</c> while the play-in is not finished.</summary>
    private (ClubId Seventh, ClubId Eighth)? PlayInSeeds()
    {
        if (!TableFinal)
        {
            return null;
        }

        var games = Stage(FixtureStage.PlayIn).ToList();
        var final = games.FirstOrDefault(g => g.Round == 2);
        var upper = games.FirstOrDefault(g => g.Round == 1 && PlayInClubs().Take(2).Contains(g.Home));
        return final?.Winner is { } eighth && upper?.Winner is { } seventh ? (seventh, eighth) : null;
    }

    private IReadOnlyList<SeasonGameRequest> PlayInRequests()
    {
        var clubs = PlayInClubs();
        var games = Stage(FixtureStage.PlayIn).ToList();
        if (games.Count == 0)
        {
            return
            [
                new(FixtureStage.PlayIn, 1, 1, clubs[0], clubs[1]),
                new(FixtureStage.PlayIn, 1, 1, clubs[2], clubs[3]),
            ];
        }

        if (games.Count == 2 && games.All(g => g.IsPlayed))
        {
            var upperLoser = games.Single(g => g.Home == clubs[0]).Loser!.Value;
            var lowerWinner = games.Single(g => g.Home == clubs[2]).Winner!.Value;
            var (home, away) = Position(upperLoser) < Position(lowerWinner) ? (upperLoser, lowerWinner) : (lowerWinner, upperLoser);
            return [new(FixtureStage.PlayIn, 2, 1, home, away)];
        }

        return [];
    }

    private int Position(ClubId club) => FinalTable().First(r => r.ClubId == club).Position;
}
