using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Competitions.Rules;
using Ebm.Domain.Countries;
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
        var countries = Repo<Country, CountryId>(services);
        foreach (var clubId in clubIds)
        {
            var club = await RequireAsync<Club, ClubId>(services, clubId, "club", cancellationToken).ConfigureAwait(false);
            var code = (await countries.FindAsync(club.CountryId, cancellationToken).ConfigureAwait(false))?.Code;
            if (!entered.Add(clubId))
            {
                throw new DomainValidationException($"{club.Name} is already entered in {competition.Name}.");
            }

            var inAnotherLeague = seasonEntries.Any(p => p.ClubId == clubId && p.CompetitionId != competitionId && leagueIds.Contains(p.CompetitionId));
            CompetitionRegistration.Check(competition, club, entered.Count - 1, inAnotherLeague, code);
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
            throw new DomainValidationException($"The {data.SeasonEntity.Name} schedule of {competition.Name} already exists.");
        }

        data.Rules.ValidateForClubs(data.Clubs.Count);
        await CheckDivisionPlacesAsync(services, competition, data.Rules, cancellationToken).ConfigureAwait(false);

        var start = firstRound ?? data.SeasonEntity.StartDate.AddDays(data.Rules.RegularSeason.StartOffsetDays);
        var days = data.Rules.RegularSeason.DaysBetweenRounds;
        var games = RoundRobinScheduler.Generate(data.Clubs, data.Rules.RegularSeason.Legs);
        var lastDate = start.AddDays((games.Max(g => g.Round) - 1) * days);
        if (start < data.SeasonEntity.StartDate || lastDate > data.SeasonEntity.EndDate)
        {
            throw new DomainValidationException(
                $"The schedule ({start:yyyy-MM-dd} to {lastDate:yyyy-MM-dd}) does not fit in season {data.SeasonEntity.Name}. Change the start date, the season or regularSeason.daysBetweenRounds.");
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
        if (fixture.Stage is FixtureStage.RegularSeason or FixtureStage.SecondPhase)
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
        var requests = data.Season.NextGames();
        var phase = ToPhase(data.Season.Phase);
        if (requests.Count == 0)
        {
            return new CompetitionProgress(phase, 0);
        }

        var fixtures = Repo<Fixture, FixtureId>(services);
        var latest = data.Fixtures.Max(f => f.Date);
        foreach (var request in requests)
        {
            fixtures.Add(new Fixture(
                FixtureId.New(),
                competitionId,
                seasonId,
                request.Round,
                GameDate(data, request, latest),
                request.Home,
                request.Away,
                request.Stage,
                request.GameNumber));
        }

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CompetitionProgress(phase, requests.Count);
    }

    public async Task<CompetitionPhase> GetPhaseAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        return ToPhase((await LoadAsync(scope.ServiceProvider, competitionId, seasonId, cancellationToken).ConfigureAwait(false)).Season.Phase);
    }

    public async Task<IReadOnlyList<StandingRow>> GetStandingsAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = await LoadAsync(scope.ServiceProvider, competitionId, seasonId, cancellationToken).ConfigureAwait(false);
        return data.Season.FinalTable();
    }

    public async Task<IReadOnlyList<GroupTable>> GetGroupTablesAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = await LoadAsync(scope.ServiceProvider, competitionId, seasonId, cancellationToken).ConfigureAwait(false);
        return data.Season.GroupTables();
    }

    public async Task<SeasonOutcome> GetOutcomeAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = await LoadAsync(scope.ServiceProvider, competitionId, seasonId, cancellationToken).ConfigureAwait(false);
        if (data.Season.Phase != SeasonPhase.Completed)
        {
            throw new DomainValidationException($"The {data.SeasonEntity.Name} season of {data.Competition.Name} is not finished yet.");
        }

        return data.Season.Outcome();
    }

    private static CompetitionPhase ToPhase(SeasonPhase phase) => (CompetitionPhase)(int)phase;

    /// <summary>
    /// Second-phase round r comes r times its interval after the last game; a play-in round or a series opener the
    /// playoff interval after the last game; later games of a series that interval after the previous game.
    /// </summary>
    private static DateOnly GameDate(SeasonData data, SeasonGameRequest request, DateOnly latest)
    {
        var rules = data.Rules;
        if (request.Stage == FixtureStage.SecondPhase)
        {
            return latest.AddDays(request.Round * rules.SecondPhase!.DaysBetweenRounds);
        }

        var days = rules.Playoffs!.DaysBetweenGames;
        if (request.Stage == FixtureStage.Playoffs && request.GameNumber > 1)
        {
            return data.Fixtures.Where(f => f.Stage == FixtureStage.Playoffs && f.Round == request.Round && IsPair(f, request.Home, request.Away))
                .Max(f => f.Date).AddDays(days);
        }

        return latest.AddDays(days);
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
        var clubIds = (await Repo<CompetitionParticipant, CompetitionParticipantId>(services)
                .ListAsync(p => p.CompetitionId == competitionId && p.SeasonId == seasonId, cancellationToken).ConfigureAwait(false))
            .Select(p => p.ClubId)
            .ToList();
        var rules = competition.ParseRules();
        var countryOf = new Dictionary<ClubId, string>();
        if (rules.SecondPhase?.ByCountry == true)
        {
            var clubs = await Repo<Club, ClubId>(services).ListAsync(c => clubIds.Contains(c.Id), cancellationToken).ConfigureAwait(false);
            var codes = (await Repo<Country, CountryId>(services).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Code);
            countryOf = clubs.ToDictionary(c => c.Id, c => codes.GetValueOrDefault(c.CountryId, string.Empty));
        }

        var fixtures = await Repo<Fixture, FixtureId>(services)
            .ListAsync(f => f.CompetitionId == competitionId && f.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var fixtureIds = fixtures.Select(f => f.Id).ToList();
        var results = fixtureIds.Count == 0
            ? []
            : (await Repo<Match, MatchId>(services).ListAsync(m => fixtureIds.Contains(m.FixtureId), cancellationToken).ConfigureAwait(false))
                .ToDictionary(m => m.FixtureId);
        var games = fixtures.Select(f => results.TryGetValue(f.Id, out var m)
            ? new SeasonGame(f.Stage, f.Round, f.GameNumber, f.HomeClubId, f.AwayClubId, m.HomeScore, m.AwayScore)
            : new SeasonGame(f.Stage, f.Round, f.GameNumber, f.HomeClubId, f.AwayClubId));
        var entries = clubIds.ToDictionary(c => c, c => countryOf.GetValueOrDefault(c, string.Empty));
        return new SeasonData(competition, season, rules, new CompetitionSeason(rules, entries, games), fixtures);
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

    /// <summary>Everything stored about one competition season, with the engine view of it.</summary>
    private sealed record SeasonData(
        Competition Competition,
        Season SeasonEntity,
        CompetitionRules Rules,
        CompetitionSeason Season,
        IReadOnlyList<Fixture> Fixtures)
    {
        public IReadOnlyList<ClubId> Clubs => Season.Clubs;
    }
}
