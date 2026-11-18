using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Competitions.Rules;
using Ebm.Domain.Countries;
using Ebm.Domain.Finances;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;
using Ebm.Domain.Standings;
using Ebm.Services.Data;
using Ebm.Services.Finances;
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

        if (competition.ParseRules().Cup?.Qualification is { } qualification)
        {
            throw new DomainValidationException($"{competition.Name} takes the top {qualification.Positions} clubs of its league table: clubs cannot be entered by hand.");
        }

        var participants = Repo<CompetitionParticipant, CompetitionParticipantId>(services);
        var seasonEntries = await participants.ListAsync(p => p.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var leagueIds = (await Repo<Competition, CompetitionId>(services)
            .ListAsync(c => c.Type == CompetitionType.League, cancellationToken).ConfigureAwait(false))
            .Select(c => c.Id)
            .ToHashSet();
        var entered = seasonEntries.Where(p => p.CompetitionId == competitionId).Select(p => p.ClubId).ToHashSet();
        var otherInternational = competition.ParseRules().Cup?.Entry is null
            ? []
            : (await EntryCompetitionsAsync(services, cancellationToken).ConfigureAwait(false)).Select(c => c.Id).Where(id => id != competitionId).ToHashSet();
        var countries = Repo<Country, CountryId>(services);
        foreach (var clubId in clubIds)
        {
            var club = await RequireAsync<Club, ClubId>(services, clubId, "club", cancellationToken).ConfigureAwait(false);
            var code = (await countries.FindAsync(club.CountryId, cancellationToken).ConfigureAwait(false))?.Code;
            if (!entered.Add(clubId))
            {
                throw new DomainValidationException($"{club.Name} is already entered in {competition.Name}.");
            }

            if (seasonEntries.Any(p => p.ClubId == clubId && otherInternational.Contains(p.CompetitionId)))
            {
                throw new DomainValidationException($"{club.Name} already plays another international competition this season.");
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
        if (competition.Type == CompetitionType.Cup && data.Cup is not null)
        {
            return await DrawAsync(services, data, required: true, cancellationToken).ConfigureAwait(false);
        }

        if (competition.Type != CompetitionType.League)
        {
            throw new DomainValidationException(competition.Type == CompetitionType.Cup
                ? $"{competition.Name} has no cup format yet: add a cup section to its rules (docs/CUP_FORMATS.md)."
                : $"{competition.Name} is a {competition.Type.ToString().ToLowerInvariant()}: only leagues and cups can be scheduled.");
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
        if (data.Cup is not null)
        {
            return await AdvanceCupAsync(services, data, cancellationToken).ConfigureAwait(false);
        }

        var requests = data.Season.NextGames();
        var phase = ToPhase(data.Season.Phase);
        if (requests.Count == 0)
        {
            return new CompetitionProgress(phase, 0);
        }

        // Like cup games, second-phase, play-in and playoff games move to a day on which neither club plays elsewhere.
        var latest = data.Fixtures.Max(f => f.Date);
        await AddFixturesAsync(services, data, requests, request => GameDate(data, request, latest), cancellationToken).ConfigureAwait(false);
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CompetitionProgress(phase, requests.Count);
    }

    public async Task<CompetitionPhase> GetPhaseAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        return ToPhase((await LoadAsync(scope.ServiceProvider, competitionId, seasonId, cancellationToken, readOnly: true).ConfigureAwait(false)).Phase);
    }

    public async Task<IReadOnlyList<StandingRow>> GetStandingsAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = await LoadAsync(scope.ServiceProvider, competitionId, seasonId, cancellationToken).ConfigureAwait(false);
        return data.Cup is null ? data.Season.FinalTable() : [];
    }

    public async Task<IReadOnlyList<GroupTable>> GetGroupTablesAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = await LoadAsync(scope.ServiceProvider, competitionId, seasonId, cancellationToken).ConfigureAwait(false);
        return data.Cup?.GroupTables() ?? data.Season.GroupTables();
    }

    public async Task<SeasonOutcome> GetOutcomeAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = await LoadAsync(scope.ServiceProvider, competitionId, seasonId, cancellationToken).ConfigureAwait(false);
        if (data.Phase != SeasonPhase.Completed)
        {
            throw new DomainValidationException($"The {data.SeasonEntity.Name} season of {data.Competition.Name} is not finished yet.");
        }

        return data.Cup?.Outcome() ?? data.Season.Outcome();
    }

    public async Task<CupView> GetCupAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var data = await LoadAsync(scope.ServiceProvider, competitionId, seasonId, cancellationToken).ConfigureAwait(false);
        var cup = data.Cup ?? throw new DomainValidationException($"{data.Competition.Name} is not a cup with a cup format.");
        return new CupView(ToPhase(cup.Phase), cup.Seeds, cup.GroupTables(), cup.Byes, cup.Ties(), cup.Winner, cup.RunnerUp);
    }

    public async Task<IReadOnlyList<CompetitionHistoryEntry>> GetHistoryAsync(CompetitionId competitionId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        await RequireAsync<Competition, CompetitionId>(services, competitionId, "competition", cancellationToken).ConfigureAwait(false);
        var seasonIds = (await Repo<CompetitionParticipant, CompetitionParticipantId>(services)
                .ListAsync(p => p.CompetitionId == competitionId, cancellationToken).ConfigureAwait(false))
            .Select(p => p.SeasonId)
            .ToHashSet();
        var seasons = (await Repo<Season, SeasonId>(services).ListAsync(x => seasonIds.Contains(x.Id), cancellationToken).ConfigureAwait(false))
            .OrderBy(x => x.StartDate);
        var history = new List<CompetitionHistoryEntry>();
        foreach (var season in seasons)
        {
            var data = await LoadAsync(services, competitionId, season.Id, cancellationToken).ConfigureAwait(false);
            var outcome = data.Phase == SeasonPhase.Completed ? data.Cup?.Outcome() ?? data.Season.Outcome() : null;
            history.Add(new CompetitionHistoryEntry(season, ToPhase(data.Phase), outcome?.Champion, outcome?.RunnerUp));
        }

        return history;
    }

    public async Task<int> QualifyAsync(SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var services = scope.ServiceProvider;
        var season = await RequireAsync<Season, SeasonId>(services, seasonId, "season", cancellationToken).ConfigureAwait(false);
        var international = await EntryCompetitionsAsync(services, cancellationToken).ConfigureAwait(false);
        if (international.Count == 0)
        {
            return 0;
        }

        var participants = Repo<CompetitionParticipant, CompetitionParticipantId>(services);
        var entries = await participants.ListAsync(p => p.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var previous = (await Repo<Season, SeasonId>(services).ListAsync(x => x.StartDate < season.StartDate, cancellationToken).ConfigureAwait(false))
            .OrderByDescending(x => x.StartDate).FirstOrDefault();
        var previousEntries = previous is null ? [] : await participants.ListAsync(p => p.SeasonId == previous.Id, cancellationToken).ConfigureAwait(false);
        var clubs = (await Repo<Club, ClubId>(services).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
        var codes = (await Repo<Country, CountryId>(services).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Code.ToUpperInvariant());
        string CountryOf(ClubId club) => clubs.TryGetValue(club, out var c) ? codes.GetValueOrDefault(c.CountryId, string.Empty) : string.Empty;
        int Reputation(ClubId club) => clubs.TryGetValue(club, out var c) ? c.Reputation : 0;
        var levels = (await Repo<Division, DivisionId>(services).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id, d => d.Level);
        var competitions = await Repo<Competition, CompetitionId>(services).ListAsync(null, cancellationToken).ConfigureAwait(false);
        var leagueLevel = competitions.Where(c => c.Type == CompetitionType.League && c.DivisionId is { } d && levels.ContainsKey(d))
            .ToDictionary(c => c.Id, c => levels[c.DivisionId!.Value]);

        // Rankings per country and level: last season's champion and final table, else this season's clubs by reputation;
        // a country with clubs in several leagues of one level ranks the stronger league (mean reputation) first.
        var ranked = new List<(string Country, int Level, double Strength, int Order, ClubId Club)>();
        foreach (var (leagueId, level) in leagueLevel.OrderBy(l => l.Key.Value))
        {
            List<ClubId> order;
            if (previous is not null && previousEntries.Any(p => p.CompetitionId == leagueId)
                && await LoadAsync(services, leagueId, previous.Id, cancellationToken).ConfigureAwait(false) is { Phase: SeasonPhase.Completed } last)
            {
                var champion = last.Season.Outcome().Champion;
                order = [champion, .. last.Season.FinalTable().Select(r => r.ClubId).Where(c => c != champion)];
            }
            else
            {
                order = [.. entries.Where(p => p.CompetitionId == leagueId).Select(p => p.ClubId).OrderByDescending(Reputation).ThenBy(c => c.Value)];
            }

            var strength = order.Count == 0 ? 0 : order.Average(c => (double)Reputation(c));
            ranked.AddRange(order.Select((club, i) => (CountryOf(club), level, strength, i, club)));
        }

        var rankings = ranked.GroupBy(r => (r.Country, r.Level)).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<ClubId>)[.. g.OrderByDescending(r => r.Strength).ThenBy(r => r.Order).Select(r => r.Club).Distinct()]);
        var playing = entries.Where(p => leagueLevel.ContainsKey(p.CompetitionId))
            .GroupBy(p => p.ClubId)
            .Select(g => new QualifyingClub(g.Key, CountryOf(g.Key), g.Min(p => leagueLevel[p.CompetitionId]), Reputation(g.Key)))
            .ToList();
        var winners = new Dictionary<string, ClubId>(StringComparer.OrdinalIgnoreCase);
        if (previous is not null)
        {
            foreach (var cup in competitions.Where(c => c.Type == CompetitionType.Cup && previousEntries.Any(p => p.CompetitionId == c.Id)).OrderBy(c => c.Id.Value))
            {
                var last = await LoadAsync(services, cup.Id, previous.Id, cancellationToken).ConfigureAwait(false);
                if (last.Cup is { Phase: SeasonPhase.Completed } played)
                {
                    winners.TryAdd(cup.ShortName, played.Winner!.Value);
                }
            }
        }

        var fixtures = await Repo<Fixture, FixtureId>(services).ListAsync(f => f.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var candidates = international.Select(c =>
        {
            var rules = c.ParseRules();
            List<ClubId> entered = [.. entries.Where(p => p.CompetitionId == c.Id).Select(p => p.ClubId)];
            var drawn = fixtures.Any(f => f.CompetitionId == c.Id);
            return new EntryCompetition(c.Id, c.ShortName, rules.Cup!.Entry!, drawn ? entered.Count : rules.Registration.MaxTeams, entered, rules.Registration.Countries);
        }).ToList();
        var selection = InternationalQualification.Select(candidates, new QualificationSources(rankings, winners, playing));
        var added = 0;
        foreach (var competition in international)
        {
            var count = entries.Count(p => p.CompetitionId == competition.Id);
            foreach (var clubId in selection[competition.Id])
            {
                CompetitionRegistration.Check(competition, clubs[clubId], count++, false, CountryOf(clubId));
                participants.Add(new CompetitionParticipant(CompetitionParticipantId.New(), competition.Id, seasonId, clubId));
                added++;
            }
        }

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return added;
    }

    public async Task<IReadOnlyList<CalendarConflict>> GetCalendarConflictsAsync(SeasonId seasonId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var fixtures = await Repo<Fixture, FixtureId>(scope.ServiceProvider).ListAsync(f => f.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        return
        [
            .. fixtures.SelectMany(f => new[] { (Club: f.HomeClubId, f.Date, Fixture: f), (Club: f.AwayClubId, f.Date, Fixture: f) })
                .GroupBy(x => (x.Club, x.Date))
                .Where(g => g.Count() > 1)
                .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.Club.Value)
                .Select(g => new CalendarConflict(g.Key.Club, g.Key.Date, [.. g.Select(x => x.Fixture.Id)])),
        ];
    }

    /// <summary>Cups that qualify their clubs by entry slots (international competitions).</summary>
    private static async Task<List<Competition>> EntryCompetitionsAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        [.. (await Repo<Competition, CompetitionId>(services).ListAsync(c => c.Type == CompetitionType.Cup, cancellationToken).ConfigureAwait(false))
            .Where(c => c.ParseRules().Cup?.Entry is not null)];

    /// <summary>
    /// The cup draw: seeds the clubs (qualified from the league table, or the entered clubs by division level, then
    /// reputation), stores the seeds and schedules the group stage or the first round. When the draw is not possible
    /// yet it throws if <paramref name="required"/>, else it does nothing.
    /// </summary>
    private static async Task<CompetitionProgress> DrawAsync(IServiceProvider services, SeasonData data, bool required, CancellationToken cancellationToken)
    {
        var competition = data.Competition;
        var season = data.SeasonEntity;
        var cup = data.Rules.Cup!;
        CompetitionProgress Fail(string message) => required
            ? throw new DomainValidationException(message)
            : new CompetitionProgress(CompetitionPhase.Registration, 0);

        if (data.Cup!.IsDrawn)
        {
            return Fail($"The {season.Name} draw of {competition.Name} has already been made.");
        }

        var participants = Repo<CompetitionParticipant, CompetitionParticipantId>(services);
        var entries = (await participants.ListAsync(p => p.CompetitionId == competition.Id && p.SeasonId == season.Id, cancellationToken).ConfigureAwait(false)).ToList();
        var start = season.StartDate.AddDays(cup.StartOffsetDays);
        List<ClubId> seeds;
        if (cup.Qualification is { } qualification)
        {
            var source = await QualifyingLeagueAsync(services, competition, qualification.Level, season.Id, cancellationToken).ConfigureAwait(false);
            if (source is null)
            {
                return Fail($"{competition.Name} takes its clubs from the level {qualification.Level} division of its country, which has no league with entries in {season.Name}.");
            }

            var league = await LoadAsync(services, source.Id, season.Id, cancellationToken).ConfigureAwait(false);
            if (!league.Season.RoundsPlayed(qualification.AfterRound))
            {
                return Fail($"{competition.Name} is drawn once round {qualification.AfterRound} of {source.Name} has been played.");
            }

            seeds = [.. league.Season.TableAfterRound(qualification.AfterRound).Take(qualification.Positions).Select(r => r.ClubId)];
            if (seeds.Count < qualification.Positions || entries.Any(e => !seeds.Contains(e.ClubId)))
            {
                return Fail($"{competition.Name} needs the top {qualification.Positions} clubs of {source.Name}, and only those.");
            }

            var lastQualifying = league.Fixtures.Where(f => f.Stage == FixtureStage.RegularSeason && f.Round <= qualification.AfterRound).Max(f => f.Date);
            start = lastQualifying >= start ? lastQualifying.AddDays(1) : start;
            var countries = Repo<Country, CountryId>(services);
            foreach (var clubId in seeds.Where(c => entries.All(e => e.ClubId != c)))
            {
                var club = await RequireAsync<Club, ClubId>(services, clubId, "club", cancellationToken).ConfigureAwait(false);
                var code = (await countries.FindAsync(club.CountryId, cancellationToken).ConfigureAwait(false))?.Code;
                CompetitionRegistration.Check(competition, club, entries.Count, false, code);
                var entry = new CompetitionParticipant(CompetitionParticipantId.New(), competition.Id, season.Id, clubId);
                participants.Add(entry);
                entries.Add(entry);
            }
        }
        else
        {
            seeds = await RankEntriesAsync(services, entries, season.Id, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            data.Rules.ValidateForClubs(seeds.Count);
        }
        catch (DomainValidationException) when (!required)
        {
            return Fail(string.Empty);
        }

        for (var i = 0; i < seeds.Count; i++)
        {
            entries.Single(e => e.ClubId == seeds[i]).SetSeed(i + 1);
        }

        var requests = new CupSeason(data.Rules, seeds, [], data.DrawSeed).NextGames();
        DateOnly DateOf(SeasonGameRequest request) =>
            request.Stage == FixtureStage.CupGroup ? start.AddDays((request.Round - 1) * cup.GroupStage!.DaysBetweenRounds) : start;
        if (requests.Max(DateOf) > season.EndDate)
        {
            return Fail($"The {competition.Name} games starting {start:yyyy-MM-dd} do not fit in season {season.Name}. Change cup.startOffsetDays.");
        }

        await AddFixturesAsync(services, data, requests, DateOf, cancellationToken).ConfigureAwait(false);
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CompetitionProgress(cup.GroupStage is null ? CompetitionPhase.Knockout : CompetitionPhase.GroupStage, requests.Count);
    }

    /// <summary>Draws the cup when possible, then schedules the next round (the round interval after the cup's last game) and second legs.</summary>
    private static async Task<CompetitionProgress> AdvanceCupAsync(IServiceProvider services, SeasonData data, CancellationToken cancellationToken)
    {
        var cup = data.Cup!;
        if (!cup.IsDrawn)
        {
            return await DrawAsync(services, data, required: false, cancellationToken).ConfigureAwait(false);
        }

        var requests = cup.NextGames();
        var phase = ToPhase(cup.Phase);
        if (phase == CompetitionPhase.Completed)
        {
            await PayRewardsAsync(services, data, cancellationToken).ConfigureAwait(false);
        }

        if (requests.Count == 0)
        {
            return new CompetitionProgress(phase, 0);
        }

        var rules = data.Rules.Cup!;
        var latest = data.Fixtures.Max(f => f.Date);
        DateOnly DateOf(SeasonGameRequest request) => request.GameNumber == 2
            ? data.Fixtures.Single(f => f.Stage == FixtureStage.Cup && f.Round == request.Round && f.GameNumber == 1 && IsPair(f, request.Home, request.Away)).Date.AddDays(rules.DaysBetweenLegs)
            : latest.AddDays(rules.Rounds[request.Round - 1].DaysAfter ?? rules.DaysBetweenRounds);
        await AddFixturesAsync(services, data, requests, DateOf, cancellationToken).ConfigureAwait(false);
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new CompetitionProgress(phase, requests.Count);
    }

    /// <summary>Pays the rewards of a completed cup (prize money to the balance, reputation) to every entry not yet paid.</summary>
    private static async Task PayRewardsAsync(IServiceProvider services, SeasonData data, CancellationToken cancellationToken)
    {
        if (data.Rules.Cup!.Rewards is null)
        {
            return;
        }

        var unpaid = await Repo<CompetitionParticipant, CompetitionParticipantId>(services)
            .ListAsync(p => p.CompetitionId == data.Competition.Id && p.SeasonId == data.SeasonEntity.Id && p.PrizeMoney == null, cancellationToken).ConfigureAwait(false);
        if (unpaid.Count == 0)
        {
            return;
        }

        var clubs = Repo<Club, ClubId>(services);
        var paidOn = data.Fixtures.Count > 0 ? data.Fixtures.Max(f => f.Date) : data.SeasonEntity.EndDate;
        foreach (var entry in unpaid)
        {
            var reward = data.Cup!.Reward(entry.ClubId);
            entry.RecordReward(reward.Prize, reward.Reputation);
            if (await clubs.FindAsync(entry.ClubId, cancellationToken).ConfigureAwait(false) is { } club)
            {
                Ledger.Book(services, club, paidOn, FinanceCategory.PrizeMoney, reward.Prize, $"{data.Competition.Name} {data.SeasonEntity.Name}");
                club.ChangeReputation(reward.Reputation);
            }
        }

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Adds the fixtures, each moved on to the first day from its planned date on which neither club has another game.</summary>
    private static async Task AddFixturesAsync(IServiceProvider services, SeasonData data, IReadOnlyList<SeasonGameRequest> requests, Func<SeasonGameRequest, DateOnly> dateOf, CancellationToken cancellationToken)
    {
        var seasonId = data.SeasonEntity.Id;
        var clubIds = requests.SelectMany(r => new[] { r.Home, r.Away }).Distinct().ToList();
        var fixtures = Repo<Fixture, FixtureId>(services);
        var busy = (await fixtures.ListAsync(f => f.SeasonId == seasonId && (clubIds.Contains(f.HomeClubId) || clubIds.Contains(f.AwayClubId)), cancellationToken).ConfigureAwait(false))
            .SelectMany(f => new[] { (f.HomeClubId, f.Date), (f.AwayClubId, f.Date) })
            .ToHashSet();
        foreach (var request in requests)
        {
            var date = dateOf(request);
            while (busy.Contains((request.Home, date)) || busy.Contains((request.Away, date)))
            {
                date = date.AddDays(1);
            }

            busy.Add((request.Home, date));
            busy.Add((request.Away, date));
            fixtures.Add(new Fixture(FixtureId.New(), data.Competition.Id, seasonId, request.Round, date, request.Home, request.Away, request.Stage, request.GameNumber));
        }
    }

    /// <summary>The league competition of the cup's country at division level <paramref name="level"/> with entries in the season.</summary>
    private static async Task<Competition?> QualifyingLeagueAsync(IServiceProvider services, Competition cup, int level, SeasonId seasonId, CancellationToken cancellationToken)
    {
        if (cup.CountryId is not { } countryId)
        {
            return null;
        }

        var divisions = (await Repo<Division, DivisionId>(services).ListAsync(d => d.Level == level, cancellationToken).ConfigureAwait(false)).Select(d => d.Id).ToList();
        var leagues = await Repo<Competition, CompetitionId>(services)
            .ListAsync(c => c.Type == CompetitionType.League && c.CountryId == countryId && c.DivisionId != null && divisions.Contains(c.DivisionId.Value), cancellationToken).ConfigureAwait(false);
        var entered = (await Repo<CompetitionParticipant, CompetitionParticipantId>(services).ListAsync(p => p.SeasonId == seasonId, cancellationToken).ConfigureAwait(false))
            .Select(p => p.CompetitionId)
            .ToHashSet();
        return leagues.Where(c => entered.Contains(c.Id)).OrderBy(c => c.Id.Value).FirstOrDefault();
    }

    /// <summary>Entered clubs best first: by the division level of their league this season (no league last), then reputation.</summary>
    private static async Task<List<ClubId>> RankEntriesAsync(IServiceProvider services, IReadOnlyList<CompetitionParticipant> entries, SeasonId seasonId, CancellationToken cancellationToken)
    {
        var ids = entries.Select(e => e.ClubId).ToList();
        var clubs = await Repo<Club, ClubId>(services).ListAsync(c => ids.Contains(c.Id), cancellationToken).ConfigureAwait(false);
        var leagueEntries = await Repo<CompetitionParticipant, CompetitionParticipantId>(services)
            .ListAsync(p => p.SeasonId == seasonId && ids.Contains(p.ClubId), cancellationToken).ConfigureAwait(false);
        var leagues = (await Repo<Competition, CompetitionId>(services).ListAsync(c => c.Type == CompetitionType.League && c.DivisionId != null, cancellationToken).ConfigureAwait(false))
            .ToDictionary(c => c.Id, c => c.DivisionId!.Value);
        var levels = (await Repo<Division, DivisionId>(services).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id, d => d.Level);
        int Level(ClubId club) => leagueEntries.Where(p => p.ClubId == club && leagues.ContainsKey(p.CompetitionId))
            .Select(p => levels.GetValueOrDefault(leagues[p.CompetitionId], int.MaxValue))
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        return [.. clubs.OrderBy(c => Level(c.Id)).ThenByDescending(c => c.Reputation).ThenBy(c => c.Id.Value).Select(c => c.Id)];
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

    /// <param name="readOnly">Fixtures and results read without change tracking (phase and table reads, several per game day).</param>
    private static async Task<SeasonData> LoadAsync(IServiceProvider services, CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken, bool readOnly = false)
    {
        var competition = await RequireAsync<Competition, CompetitionId>(services, competitionId, "competition", cancellationToken).ConfigureAwait(false);
        var season = await RequireAsync<Season, SeasonId>(services, seasonId, "season", cancellationToken).ConfigureAwait(false);
        var participants = await Repo<CompetitionParticipant, CompetitionParticipantId>(services)
            .ListAsync(p => p.CompetitionId == competitionId && p.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var clubIds = participants.Select(p => p.ClubId).ToList();
        var rules = competition.ParseRules();
        var countryOf = new Dictionary<ClubId, string>();
        if (rules.SecondPhase?.ByCountry == true)
        {
            var clubs = await Repo<Club, ClubId>(services).ListAsync(c => clubIds.Contains(c.Id), cancellationToken).ConfigureAwait(false);
            var codes = (await Repo<Country, CountryId>(services).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Code);
            countryOf = clubs.ToDictionary(c => c.Id, c => codes.GetValueOrDefault(c.CountryId, string.Empty));
        }

        var fixtureRepo = Repo<Fixture, FixtureId>(services);
        var matchRepo = Repo<Match, MatchId>(services);
        var fixtures = readOnly
            ? await fixtureRepo.ListUntrackedAsync(f => f.CompetitionId == competitionId && f.SeasonId == seasonId, cancellationToken).ConfigureAwait(false)
            : await fixtureRepo.ListAsync(f => f.CompetitionId == competitionId && f.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var fixtureIds = fixtures.Select(f => f.Id).ToList();
        var results = fixtureIds.Count == 0
            ? []
            : (readOnly
                ? await matchRepo.ListUntrackedAsync(m => fixtureIds.Contains(m.FixtureId), cancellationToken).ConfigureAwait(false)
                : await matchRepo.ListAsync(m => fixtureIds.Contains(m.FixtureId), cancellationToken).ConfigureAwait(false))
                .ToDictionary(m => m.FixtureId);
        var games = fixtures.Select(f => results.TryGetValue(f.Id, out var m)
            ? new SeasonGame(f.Stage, f.Round, f.GameNumber, f.HomeClubId, f.AwayClubId, m.HomeScore, m.AwayScore)
            : new SeasonGame(f.Stage, f.Round, f.GameNumber, f.HomeClubId, f.AwayClubId)).ToList();
        var entries = clubIds.ToDictionary(c => c, c => countryOf.GetValueOrDefault(c, string.Empty));
        var drawSeed = CupSeason.DrawSeed(competitionId.Value, seasonId.Value);
        var cup = competition.Type == CompetitionType.Cup && rules.Cup is not null
            ? new CupSeason(rules, [.. participants.Where(p => p.Seed is not null).OrderBy(p => p.Seed).Select(p => p.ClubId)], games, drawSeed)
            : null;
        return new SeasonData(competition, season, rules, new CompetitionSeason(rules, entries, games), fixtures, cup, drawSeed);
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
        IReadOnlyList<Fixture> Fixtures,
        CupSeason? Cup,
        int DrawSeed)
    {
        public IReadOnlyList<ClubId> Clubs => Season.Clubs;

        public SeasonPhase Phase => Cup?.Phase ?? Season.Phase;
    }
}
