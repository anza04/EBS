using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Competitions.Rules;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;
using Ebm.Domain.Standings;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Competitions;

/// <summary>Implements <see cref="ICompetitionService"/> over the repositories, one DI scope per call.</summary>
internal sealed class CompetitionService : ICompetitionService
{
    private readonly IServiceScopeFactory _scopes;

    public CompetitionService(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        _scopes = scopes;
    }

    public async Task RegisterClubsAsync(CompetitionId competitionId, SeasonId seasonId, IReadOnlyList<ClubId> clubIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clubIds);
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        var competition = await RequireAsync<Competition, CompetitionId>(services, competitionId, "competition", cancellationToken).ConfigureAwait(false);
        await RequireAsync<Season, SeasonId>(services, seasonId, "season", cancellationToken).ConfigureAwait(false);
        var fixtures = await Repo<Fixture, FixtureId>(services)
            .ListAsync(f => f.CompetitionId == competitionId && f.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        if (fixtures.Count > 0)
        {
            throw new DomainValidationException($"Registration for {competition.Name} is closed: the schedule has been generated.");
        }

        var participants = Repo<CompetitionParticipant, CompetitionParticipantId>(services);
        var seasonEntries = await participants.ListAsync(p => p.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var leagueIds = (await Repo<Competition, CompetitionId>(services)
            .ListAsync(c => c.Type == CompetitionType.League, cancellationToken).ConfigureAwait(false))
            .Select(c => c.Id)
            .ToHashSet();
        var entered = seasonEntries.Where(p => p.CompetitionId == competitionId).Select(p => p.ClubId).ToHashSet();
        foreach (var clubId in clubIds)
        {
            var club = await RequireAsync<Club, ClubId>(services, clubId, "club", cancellationToken).ConfigureAwait(false);
            if (!entered.Add(clubId))
            {
                throw new DomainValidationException($"{club.Name} is already entered in {competition.Name}.");
            }

            var inAnotherLeague = seasonEntries.Any(p => p.ClubId == clubId && p.CompetitionId != competitionId && leagueIds.Contains(p.CompetitionId));
            CompetitionRegistration.Check(competition, club, entered.Count - 1, inAnotherLeague);
            participants.Add(new CompetitionParticipant(CompetitionParticipantId.New(), competitionId, seasonId, clubId));
        }

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CompetitionProgress> GenerateScheduleAsync(CompetitionId competitionId, SeasonId seasonId, DateOnly? firstRound = null, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        var data = await LoadAsync(services, competitionId, seasonId, cancellationToken).ConfigureAwait(false);
        var competition = data.Competition;
        if (competition.Type != CompetitionType.League)
        {
            throw new DomainValidationException($"{competition.Name} is a {competition.Type.ToString().ToLowerInvariant()}: only league competitions can be scheduled.");
        }

        if (data.Fixtures.Count > 0)
        {
            throw new DomainValidationException($"The {data.Season.Name} schedule of {competition.Name} already exists.");
        }

        data.Rules.ValidateForClubs(data.Clubs.Count);
        await CheckDivisionPlacesAsync(services, competition, data.Rules, cancellationToken).ConfigureAwait(false);

        var start = firstRound ?? data.Season.StartDate;
        var days = data.Rules.RegularSeason.DaysBetweenRounds;
        var games = RoundRobinScheduler.Generate(data.Clubs, data.Rules.RegularSeason.Legs);
        var lastDate = start.AddDays((games.Max(g => g.Round) - 1) * days);
        if (start < data.Season.StartDate || lastDate > data.Season.EndDate)
        {
            throw new DomainValidationException(
                $"The schedule ({start:yyyy-MM-dd} to {lastDate:yyyy-MM-dd}) does not fit in season {data.Season.Name}. Change the start date, the season or regularSeason.daysBetweenRounds.");
        }

        var fixtures = Repo<Fixture, FixtureId>(services);
        foreach (var game in games)
        {
            fixtures.Add(new Fixture(FixtureId.New(), competitionId, seasonId, game.Round, start.AddDays((game.Round - 1) * days), game.Home, game.Away));
        }

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CompetitionProgress(CompetitionPhase.RegularSeason, games.Count);
    }

    public async Task RecordResultAsync(FixtureId fixtureId, int homeScore, int awayScore, int overtimes = 0, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        await AddResultAsync(services, fixtureId, homeScore, awayScore, overtimes, cancellationToken).ConfigureAwait(false);
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds the result of a fixture and its standings update to the unit of work of <paramref name="services"/>,
    /// without saving, so a caller can store more (the box score) in the same transaction.
    /// </summary>
    internal static async Task<Match> AddResultAsync(IServiceProvider services, FixtureId fixtureId, int homeScore, int awayScore, int overtimes, CancellationToken cancellationToken, bool forfeit = false)
    {
        var fixture = await RequireAsync<Fixture, FixtureId>(services, fixtureId, "fixture", cancellationToken).ConfigureAwait(false);
        var matches = Repo<Match, MatchId>(services);
        if ((await matches.ListAsync(m => m.FixtureId == fixtureId, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            throw new DomainValidationException("This fixture already has a result.");
        }

        var match = forfeit
            ? Match.Forfeit(MatchId.New(), fixtureId, homeForfeits: homeScore < awayScore)
            : new Match(MatchId.New(), fixtureId, homeScore, awayScore, overtimes);
        matches.Add(match);
        if (fixture.Stage == FixtureStage.RegularSeason)
        {
            var competition = await RequireAsync<Competition, CompetitionId>(services, fixture.CompetitionId, "competition", cancellationToken).ConfigureAwait(false);
            var rules = competition.ParseRules();
            var standings = Repo<LeagueStanding, LeagueStandingId>(services);
            var rows = await standings.ListAsync(
                s => s.CompetitionId == fixture.CompetitionId && s.SeasonId == fixture.SeasonId && (s.ClubId == fixture.HomeClubId || s.ClubId == fixture.AwayClubId),
                cancellationToken).ConfigureAwait(false);

            LeagueStanding Row(ClubId club)
            {
                var row = rows.FirstOrDefault(r => r.ClubId == club);
                if (row is null)
                {
                    row = new LeagueStanding(LeagueStandingId.New(), fixture.CompetitionId, fixture.SeasonId, club);
                    standings.Add(row);
                }

                return row;
            }

            int Points(bool won) => won ? rules.PointsForWin : rules.PointsForLoss;
            Row(fixture.HomeClubId).RecordResult(homeScore, awayScore, Points(homeScore > awayScore));
            Row(fixture.AwayClubId).RecordResult(awayScore, homeScore, Points(awayScore > homeScore));
        }

        return match;
    }

    public async Task<CompetitionProgress> AdvanceAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        var data = await LoadAsync(services, competitionId, seasonId, cancellationToken).ConfigureAwait(false);
        var phase = data.Phase();
        if (phase != CompetitionPhase.Playoffs)
        {
            return new CompetitionProgress(phase, 0);
        }

        var playoffs = data.Rules.Playoffs!;
        var requests = data.Bracket()!.NextGames();
        var fixtures = Repo<Fixture, FixtureId>(services);
        var latest = data.Fixtures.Max(f => f.Date);
        foreach (var request in requests)
        {
            var previous = request.GameNumber == 1
                ? latest
                : data.Fixtures.Where(f => f.Stage == FixtureStage.Playoffs && f.Round == request.Round && IsPair(f, request.Home, request.Away)).Max(f => f.Date);
            fixtures.Add(new Fixture(
                FixtureId.New(),
                competitionId,
                seasonId,
                request.Round,
                previous.AddDays(playoffs.DaysBetweenGames),
                request.Home,
                request.Away,
                FixtureStage.Playoffs,
                request.GameNumber));
        }

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CompetitionProgress(CompetitionPhase.Playoffs, requests.Count);
    }

    public async Task<CompetitionPhase> GetPhaseAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        return (await LoadAsync(scope.ServiceProvider, competitionId, seasonId, cancellationToken).ConfigureAwait(false)).Phase();
    }

    public async Task<IReadOnlyList<StandingRow>> GetStandingsAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = await LoadAsync(scope.ServiceProvider, competitionId, seasonId, cancellationToken).ConfigureAwait(false);
        return data.Table();
    }

    public async Task<SeasonOutcome> GetOutcomeAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = await LoadAsync(scope.ServiceProvider, competitionId, seasonId, cancellationToken).ConfigureAwait(false);
        if (data.Phase() != CompetitionPhase.Completed)
        {
            throw new DomainValidationException($"The {data.Season.Name} season of {data.Competition.Name} is not finished yet.");
        }

        return SeasonOutcomeCalculator.Calculate(data.Rules, data.Table(), data.Bracket()?.Champion);
    }

    private static bool IsPair(Fixture fixture, ClubId a, ClubId b) =>
        (fixture.HomeClubId == a && fixture.AwayClubId == b) || (fixture.HomeClubId == b && fixture.AwayClubId == a);

    /// <summary>Promotion needs a division above, relegation one below, in the same league.</summary>
    private static async Task CheckDivisionPlacesAsync(IServiceProvider services, Competition competition, CompetitionRules rules, CancellationToken cancellationToken)
    {
        if (rules.Promotion.Places == 0 && rules.Relegation.Direct == 0)
        {
            return;
        }

        if (competition.DivisionId is not { } divisionId)
        {
            throw new DomainValidationException($"{competition.Name} has promotion or relegation places but no division.");
        }

        var divisions = Repo<Division, DivisionId>(services);
        var division = await RequireAsync(services, divisionId, "division", cancellationToken, divisions).ConfigureAwait(false);
        var levels = (await divisions.ListAsync(d => d.LeagueId == division.LeagueId, cancellationToken).ConfigureAwait(false))
            .Select(d => d.Level)
            .ToHashSet();
        if (rules.Promotion.Places > 0 && !levels.Contains(division.Level - 1))
        {
            throw new DomainValidationException($"{competition.Name} promotes clubs, but {division.Name} has no division above it.");
        }

        if (rules.Relegation.Direct > 0 && !levels.Contains(division.Level + 1))
        {
            throw new DomainValidationException($"{competition.Name} relegates clubs, but {division.Name} has no division below it.");
        }
    }

    private static async Task<SeasonData> LoadAsync(IServiceProvider services, CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken)
    {
        var competition = await RequireAsync<Competition, CompetitionId>(services, competitionId, "competition", cancellationToken).ConfigureAwait(false);
        var season = await RequireAsync<Season, SeasonId>(services, seasonId, "season", cancellationToken).ConfigureAwait(false);
        var clubs = (await Repo<CompetitionParticipant, CompetitionParticipantId>(services)
                .ListAsync(p => p.CompetitionId == competitionId && p.SeasonId == seasonId, cancellationToken).ConfigureAwait(false))
            .Select(p => p.ClubId)
            .OrderBy(c => c.Value)
            .ToList();
        var fixtures = await Repo<Fixture, FixtureId>(services)
            .ListAsync(f => f.CompetitionId == competitionId && f.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var fixtureIds = fixtures.Select(f => f.Id).ToList();
        var results = fixtureIds.Count == 0
            ? []
            : (await Repo<Match, MatchId>(services).ListAsync(m => fixtureIds.Contains(m.FixtureId), cancellationToken).ConfigureAwait(false))
                .ToDictionary(m => m.FixtureId);
        return new SeasonData(competition, season, competition.ParseRules(), clubs, fixtures, results);
    }

    private static Task<TEntity> RequireAsync<TEntity, TId>(IServiceProvider services, TId id, string what, CancellationToken cancellationToken)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        RequireAsync(services, id, what, cancellationToken, Repo<TEntity, TId>(services));

    private static async Task<TEntity> RequireAsync<TEntity, TId>(IServiceProvider services, TId id, string what, CancellationToken cancellationToken, IRepository<TEntity, TId> repository)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        await repository.FindAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new DomainValidationException($"The {what} {id} does not exist.");

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();

    /// <summary>Everything stored about one competition season, with the derived table, bracket and phase.</summary>
    private sealed record SeasonData(
        Competition Competition,
        Season Season,
        CompetitionRules Rules,
        IReadOnlyList<ClubId> Clubs,
        IReadOnlyList<Fixture> Fixtures,
        IReadOnlyDictionary<FixtureId, Match> Results)
    {
        private IEnumerable<Fixture> RegularSeason => Fixtures.Where(f => f.Stage == FixtureStage.RegularSeason);

        public IReadOnlyList<StandingRow> Table() => StandingsCalculator.Calculate(
            Clubs,
            RegularSeason
                .Where(f => Results.ContainsKey(f.Id))
                .Select(f => new GameResult(f.HomeClubId, f.AwayClubId, Results[f.Id].HomeScore, Results[f.Id].AwayScore)),
            Rules);

        /// <summary>The playoff bracket, once the regular season is over and the rules have playoffs.</summary>
        public PlayoffBracket? Bracket()
        {
            if (Rules.Playoffs is not { } playoffs || !RegularSeasonOver)
            {
                return null;
            }

            var seeds = Table().Skip(playoffs.FromPosition - 1).Take(playoffs.Teams).Select(r => r.ClubId).ToList();
            var games = Fixtures
                .Where(f => f.Stage == FixtureStage.Playoffs)
                .Select(f => Results.TryGetValue(f.Id, out var m)
                    ? new PlayoffGame(f.Round, f.GameNumber, f.HomeClubId, f.AwayClubId, m.HomeScore, m.AwayScore)
                    : new PlayoffGame(f.Round, f.GameNumber, f.HomeClubId, f.AwayClubId));
            return PlayoffBracket.Build(seeds, playoffs, games);
        }

        public CompetitionPhase Phase() =>
            !RegularSeason.Any() ? CompetitionPhase.Registration
            : !RegularSeasonOver ? CompetitionPhase.RegularSeason
            : Rules.Playoffs is null || Bracket()!.IsComplete ? CompetitionPhase.Completed
            : CompetitionPhase.Playoffs;

        private bool RegularSeasonOver => RegularSeason.Any() && RegularSeason.All(f => Results.ContainsKey(f.Id));
    }
}
