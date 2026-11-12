using Ebm.Domain.Careers;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Management;
using Ebm.Domain.Matches;
using Ebm.Domain.Seasons;
using Ebm.Services.Competitions;
using Ebm.Services.Data;
using Ebm.Services.Management;
using Ebm.Services.Matches;
using Ebm.Services.Transfers;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Careers;

/// <summary>Implements <see cref="ICareerService"/> by driving the competition, match and transfer services day by day.</summary>
internal sealed class CareerService(
    IServiceScopeFactory scopes,
    ICompetitionService competitions,
    IMatchService matches,
    ITransferService transfers,
    IClubManagementService management,
    GameClock clock) : ICareerService
{
    /// <summary>A new career starts this many days before its season, so the squad can be prepared.</summary>
    public const int PreseasonDays = 7;

    /// <summary>The preseason transfer window opens this many days before a season starts.</summary>
    public const int PreseasonWindowDays = 14;

    public async Task<CareerState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var career = await FindCareerAsync(cancellationToken).ConfigureAwait(false);
        clock.Set(career?.CurrentDate);
        return career is null ? null : await StateAsync(career, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CareerClubOption>> GetClubOptionsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        if (await FirstLeagueSeasonAsync(s, cancellationToken).ConfigureAwait(false) is not { } season)
        {
            return [];
        }

        var leagues = await LeagueCompetitionsAsync(s, season.Id, cancellationToken).ConfigureAwait(false);
        var entries = await Repo<CompetitionParticipant, CompetitionParticipantId>(s).ListAsync(p => p.SeasonId == season.Id, cancellationToken).ConfigureAwait(false);
        var clubs = (await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
        return
        [
            .. entries.Where(e => leagues.ContainsKey(e.CompetitionId) && clubs.ContainsKey(e.ClubId))
                .Select(e => new CareerClubOption(clubs[e.ClubId], leagues[e.CompetitionId].Name, clubs[e.ClubId].Reputation))
                .OrderByDescending(o => o.Reputation).ThenBy(o => o.Club.Name),
        ];
    }

    public async Task<CareerState> StartAsync(ClubId clubId, string managerName, CancellationToken cancellationToken = default)
    {
        Season season;
        using (var scope = scopes.CreateScope())
        {
            var s = scope.ServiceProvider;
            if ((await Repo<Career, CareerId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                throw new DomainValidationException("This game database already has a career.");
            }

            season = await FirstLeagueSeasonAsync(s, cancellationToken).ConfigureAwait(false)
                ?? throw new DomainValidationException("No season has clubs entered in a league: import or create one first.");
        }

        if ((await GetClubOptionsAsync(cancellationToken).ConfigureAwait(false)).All(o => o.Club.Id != clubId))
        {
            throw new DomainValidationException($"The club does not play in a league of season {season.Name}.");
        }

        await ScheduleLeaguesAsync(season.Id, null, cancellationToken).ConfigureAwait(false);
        var career = new Career(CareerId.New(), managerName, clubId, season.Id, season.StartDate.AddDays(-PreseasonDays));
        using (var scope = scopes.CreateScope())
        {
            var s = scope.ServiceProvider;
            Repo<Career, CareerId>(s).Add(career);
            await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        clock.Set(career.CurrentDate);
        await transfers.ProcessTransferDatesAsync(cancellationToken).ConfigureAwait(false);
        await management.SetObjectivesAsync(season.Id, cancellationToken).ConfigureAwait(false);
        await management.PlanTacticsAsync([clubId], cancellationToken).ConfigureAwait(false);
        return await StateAsync(career, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GameDayReport> AdvanceDayAsync(CancellationToken cancellationToken = default)
    {
        var career = await RequireCareerAsync(cancellationToken).ConfigureAwait(false);
        var today = career.CurrentDate;
        List<(Fixture Fixture, string Competition)> due;
        Dictionary<ClubId, string> names;
        IReadOnlyList<Competition> leagues;
        bool windowOpens;
        using (var scope = scopes.CreateScope())
        {
            var s = scope.ServiceProvider;
            leagues = [.. (await LeagueCompetitionsAsync(s, career.SeasonId, cancellationToken).ConfigureAwait(false)).Values];
            var unplayed = await UnplayedFixturesAsync(s, career.SeasonId, cancellationToken).ConfigureAwait(false);
            if (unplayed.Any(f => f.Date <= today && IsClubFixture(f, career.ClubId)))
            {
                throw new DomainValidationException("Your club plays today: play the match first.");
            }

            var byId = leagues.ToDictionary(c => c.Id);
            due =
            [
                .. unplayed.Where(f => f.Date <= today && byId.ContainsKey(f.CompetitionId))
                    .OrderBy(f => f.Date).ThenBy(f => f.CompetitionId.Value).ThenBy(f => f.Round).ThenBy(f => f.GameNumber)
                    .Select(f => (f, byId[f.CompetitionId].Name)),
            ];
            names = (await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name);
            windowOpens = (await Repo<Season, SeasonId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false))
                .Any(x => x.EndDate == today || x.StartDate.AddDays(-PreseasonWindowDays) == today);
        }

        // AI coaches name their matchday squads; the user's club plays with the roles the user set.
        var lineups = await management.PrepareMatchdayAsync(
            [.. due.SelectMany(d => new[] { d.Fixture.HomeClubId, d.Fixture.AwayClubId }).Distinct().Where(c => c != career.ClubId)],
            cancellationToken).ConfigureAwait(false);
        var results = new List<PlayedFixture>();
        foreach (var (fixture, competition) in due)
        {
            var match = await matches.PlayOrForfeitAsync(fixture.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
            results.Add(new PlayedFixture(competition, names.GetValueOrDefault(fixture.HomeClubId, "?"), names.GetValueOrDefault(fixture.AwayClubId, "?"), match));
        }

        var scheduled = 0;
        foreach (var league in leagues)
        {
            scheduled += (await competitions.AdvanceAsync(league.Id, career.SeasonId, cancellationToken).ConfigureAwait(false)).FixturesCreated;
        }

        var (salaries, income) = await MoveCareerAsync(career.Id, today, cancellationToken).ConfigureAwait(false);
        var changes = await transfers.ProcessTransferDatesAsync(cancellationToken).ConfigureAwait(false);
        ClubId[] managed = [career.ClubId];

        // Transfer windows: the last day of a season, two weeks before a season, and any Monday an AI club is short of players.
        if (!windowOpens && today.DayOfWeek == DayOfWeek.Monday)
        {
            windowOpens = (await management.ClubsShortOfPlayersAsync(managed, cancellationToken).ConfigureAwait(false)).Count > 0;
        }

        TransferWindowReport? window = null;
        int releases = 0, planned = 0, renewals = 0, emergency = 0, created = 0;
        if (windowOpens)
        {
            created = await management.ReplenishFreeAgentsAsync(cancellationToken).ConfigureAwait(false);
            window = await transfers.RunAiTransferWindowAsync(managed, cancellationToken).ConfigureAwait(false);
            releases = await management.ReleaseSurplusAsync(managed, cancellationToken).ConfigureAwait(false);
            emergency = await management.FillSquadsAsync(managed, cancellationToken).ConfigureAwait(false);
            planned = await management.PlanTacticsAsync(managed, cancellationToken).ConfigureAwait(false);
        }

        if (today.AddDays(1).Day == 1)
        {
            renewals = await management.RenewContractsAsync(managed, cancellationToken).ConfigureAwait(false);
        }

        return new GameDayReport(today, results, scheduled, changes, window, new ManagementDay(lineups, planned, renewals, releases, emergency, created, salaries, income));
    }

    public async Task<CalendarReport> ContinueAsync(int maxDays = 366, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDays, 1);
        var state = await StateAsync(await RequireCareerAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        if (state.SeasonComplete)
        {
            throw new DomainValidationException($"The {state.Season.Name} season is complete: start the next season to continue.");
        }

        var days = new List<GameDayReport>();
        while (true)
        {
            if (state.MatchToday)
            {
                return new CalendarReport(days, CalendarStop.UserMatch, state);
            }

            if (state.SeasonComplete)
            {
                return new CalendarReport(days, CalendarStop.SeasonComplete, state);
            }

            if (days.Count == maxDays)
            {
                return new CalendarReport(days, CalendarStop.DayLimit, state);
            }

            days.Add(await AdvanceDayAsync(cancellationToken).ConfigureAwait(false));
            state = await StateAsync(await RequireCareerAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Match> PlayUserMatchAsync(CancellationToken cancellationToken = default)
    {
        var state = await StateAsync(await RequireCareerAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        if (!state.MatchToday)
        {
            throw new DomainValidationException("Your club does not play today.");
        }

        return await matches.PlayOrForfeitAsync(state.NextFixture!.Fixture.Id, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<SeasonReview> GetSeasonReviewAsync(CancellationToken cancellationToken = default)
    {
        var career = await RequireCareerAsync(cancellationToken).ConfigureAwait(false);
        return await ReviewAsync(career, career.SeasonId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CareerState> StartNextSeasonAsync(CancellationToken cancellationToken = default)
    {
        var career = await RequireCareerAsync(cancellationToken).ConfigureAwait(false);
        var state = await StateAsync(career, cancellationToken).ConfigureAwait(false);
        if (!state.SeasonComplete)
        {
            throw new DomainValidationException($"The {state.Season.Name} season is not finished yet.");
        }

        var current = state.Season;
        Season next;
        Dictionary<CompetitionId, List<ClubId>> nextEntries;
        Dictionary<CompetitionId, int> firstRoundOffsets = [];
        using (var scope = scopes.CreateScope())
        {
            var s = scope.ServiceProvider;
            var seasons = Repo<Season, SeasonId>(s);
            next = (await seasons.ListAsync(x => x.StartDate == current.StartDate.AddYears(1), cancellationToken).ConfigureAwait(false)).FirstOrDefault()
                ?? SeasonCalendar.Next(current, SeasonId.New());
            if (await seasons.FindAsync(next.Id, cancellationToken).ConfigureAwait(false) is null)
            {
                seasons.Add(next);
                await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            var entries = await Repo<CompetitionParticipant, CompetitionParticipantId>(s).ListAsync(p => p.SeasonId == current.Id, cancellationToken).ConfigureAwait(false);
            nextEntries = entries.GroupBy(e => e.CompetitionId).ToDictionary(g => g.Key, g => g.Select(e => e.ClubId).ToList());

            // Promotion and relegation between the divisions of each league.
            var leagues = await LeagueCompetitionsAsync(s, current.Id, cancellationToken).ConfigureAwait(false);
            var divisions = (await Repo<Division, DivisionId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id);
            foreach (var league in leagues.Values.Where(c => c.DivisionId is { } d && divisions.ContainsKey(d)).GroupBy(c => divisions[c.DivisionId!.Value].LeagueId))
            {
                var seasonsByDivision = new List<DivisionSeason>();
                var competitionAt = new Dictionary<int, CompetitionId>();
                foreach (var competition in league)
                {
                    var level = divisions[competition.DivisionId!.Value].Level;
                    var outcome = await competitions.GetOutcomeAsync(competition.Id, current.Id, cancellationToken).ConfigureAwait(false);
                    seasonsByDivision.Add(new DivisionSeason(level, nextEntries[competition.Id], outcome));
                    competitionAt[level] = competition.Id;
                }

                foreach (var (level, clubs) in SeasonTransition.NextEntries(seasonsByDivision))
                {
                    nextEntries[competitionAt[level]] = [.. clubs];
                }
            }

            var fixtures = await Repo<Fixture, FixtureId>(s).ListAsync(f => f.SeasonId == current.Id && f.Stage == FixtureStage.RegularSeason, cancellationToken).ConfigureAwait(false);
            foreach (var group in fixtures.GroupBy(f => f.CompetitionId))
            {
                firstRoundOffsets[group.Key] = group.Min(f => f.Date).DayNumber - current.StartDate.DayNumber;
            }
        }

        foreach (var (competitionId, clubs) in nextEntries.OrderBy(e => e.Key.Value))
        {
            var entered = await EntriesAsync(competitionId, next.Id, cancellationToken).ConfigureAwait(false);
            if (entered == 0)
            {
                await competitions.RegisterClubsAsync(competitionId, next.Id, clubs, cancellationToken).ConfigureAwait(false);
            }
        }

        await ScheduleLeaguesAsync(next.Id, id => next.StartDate.AddDays(firstRoundOffsets.GetValueOrDefault(id)), cancellationToken).ConfigureAwait(false);
        await management.SetObjectivesAsync(next.Id, cancellationToken).ConfigureAwait(false);
        using (var scope = scopes.CreateScope())
        {
            var s = scope.ServiceProvider;
            var stored = (await Repo<Career, CareerId>(s).FindAsync(career.Id, cancellationToken).ConfigureAwait(false))!;
            stored.StartSeason(next.Id);
            await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            career = stored;
        }

        return await StateAsync(career, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ClubSeasonRecord>> GetClubHistoryAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        List<(Season Season, Competition Competition)> seasons;
        using (var scope = scopes.CreateScope())
        {
            var s = scope.ServiceProvider;
            var entries = await Repo<CompetitionParticipant, CompetitionParticipantId>(s).ListAsync(p => p.ClubId == clubId, cancellationToken).ConfigureAwait(false);
            var all = (await Repo<Season, SeasonId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(x => x.Id);
            var leagues = (await Repo<Competition, CompetitionId>(s).ListAsync(c => c.Type == CompetitionType.League, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
            seasons =
            [
                .. entries.Where(e => leagues.ContainsKey(e.CompetitionId) && all.ContainsKey(e.SeasonId))
                    .Select(e => (all[e.SeasonId], leagues[e.CompetitionId]))
                    .OrderBy(e => e.Item1.StartDate),
            ];
        }

        var records = new List<ClubSeasonRecord>();
        foreach (var (season, competition) in seasons)
        {
            var phase = await competitions.GetPhaseAsync(competition.Id, season.Id, cancellationToken).ConfigureAwait(false);
            if (phase == CompetitionPhase.Registration)
            {
                continue;
            }

            var row = (await competitions.GetStandingsAsync(competition.Id, season.Id, cancellationToken).ConfigureAwait(false)).Single(r => r.ClubId == clubId);
            var outcome = phase == CompetitionPhase.Completed
                ? await competitions.GetOutcomeAsync(competition.Id, season.Id, cancellationToken).ConfigureAwait(false)
                : null;
            records.Add(new ClubSeasonRecord(
                season,
                competition,
                row.Position,
                row.Won,
                row.Lost,
                phase,
                outcome?.Champion == clubId,
                outcome?.Promoted.Contains(clubId) == true,
                outcome?.Relegated.Contains(clubId) == true));
        }

        return records;
    }

    private async Task<SeasonReview> ReviewAsync(Career career, SeasonId seasonId, CancellationToken cancellationToken)
    {
        Season season;
        IReadOnlyList<Competition> leagues;
        Dictionary<ClubId, Club> clubs;
        using (var scope = scopes.CreateScope())
        {
            var s = scope.ServiceProvider;
            season = (await Repo<Season, SeasonId>(s).FindAsync(seasonId, cancellationToken).ConfigureAwait(false))!;
            leagues = [.. (await LeagueCompetitionsAsync(s, seasonId, cancellationToken).ConfigureAwait(false)).Values.OrderBy(c => c.Name)];
            clubs = (await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
        }

        var reviews = new List<CompetitionReview>();
        foreach (var competition in leagues)
        {
            var outcome = await competitions.GetOutcomeAsync(competition.Id, seasonId, cancellationToken).ConfigureAwait(false);
            var table = await competitions.GetStandingsAsync(competition.Id, seasonId, cancellationToken).ConfigureAwait(false);
            string Name(ClubId id) => clubs.TryGetValue(id, out var club) ? club.Name : "?";
            var rules = competition.ParseRules();
            int[] playoffPlaces = rules.Playoffs is { } p ? [.. Enumerable.Range(p.FromPosition, p.Teams)] : [];
            var objectives = table.Where(r => clubs.ContainsKey(r.ClubId)).Select(r =>
            {
                var club = clubs[r.ClubId];
                var finish = new SeasonFinish(r.Position, table.Count, outcome.Champion == r.ClubId, outcome.Promoted.Contains(r.ClubId), outcome.Relegated.Contains(r.ClubId), playoffPlaces);
                return new ObjectiveResult(club, r.Position, club.BoardExpectation, SeasonObjectives.IsMet(club.BoardExpectation, finish));
            });
            reviews.Add(new CompetitionReview(
                competition,
                Name(outcome.Champion),
                [.. outcome.Promoted.Select(Name)],
                [.. outcome.Relegated.Select(Name)],
                table.FirstOrDefault(r => r.ClubId == career.ClubId)?.Position,
                [.. objectives]));
        }

        return new SeasonReview(season, reviews);
    }

    private async Task<CareerState> StateAsync(Career career, CancellationToken cancellationToken)
    {
        Club club;
        Season season;
        IReadOnlyList<Competition> leagues;
        HashSet<CompetitionId> entered;
        FixtureLine? next = null;
        using (var scope = scopes.CreateScope())
        {
            var s = scope.ServiceProvider;
            club = (await Repo<Club, ClubId>(s).FindAsync(career.ClubId, cancellationToken).ConfigureAwait(false))!;
            season = (await Repo<Season, SeasonId>(s).FindAsync(career.SeasonId, cancellationToken).ConfigureAwait(false))!;
            leagues = [.. (await LeagueCompetitionsAsync(s, career.SeasonId, cancellationToken).ConfigureAwait(false)).Values.OrderBy(c => c.Name)];
            entered = [.. (await Repo<CompetitionParticipant, CompetitionParticipantId>(s)
                    .ListAsync(p => p.SeasonId == career.SeasonId && p.ClubId == career.ClubId, cancellationToken).ConfigureAwait(false))
                .Select(p => p.CompetitionId)];
            var unplayed = await UnplayedFixturesAsync(s, career.SeasonId, cancellationToken).ConfigureAwait(false);
            if (unplayed.Where(f => IsClubFixture(f, career.ClubId)).OrderBy(f => f.Date).FirstOrDefault() is { } fixture)
            {
                var clubs = Repo<Club, ClubId>(s);
                var home = (await clubs.FindAsync(fixture.HomeClubId, cancellationToken).ConfigureAwait(false))?.Name ?? "?";
                var away = (await clubs.FindAsync(fixture.AwayClubId, cancellationToken).ConfigureAwait(false))?.Name ?? "?";
                next = new FixtureLine(fixture, home, away, null);
            }
        }

        var statuses = new List<CareerCompetition>();
        foreach (var competition in leagues)
        {
            var phase = await competitions.GetPhaseAsync(competition.Id, career.SeasonId, cancellationToken).ConfigureAwait(false);
            if (phase != CompetitionPhase.Registration)
            {
                statuses.Add(new CareerCompetition(competition, phase, entered.Contains(competition.Id)));
            }
        }

        return new CareerState(career, club, season, statuses, next);
    }

    /// <summary>Generates the regular season of every league competition of the season that has entries but no schedule.</summary>
    private async Task ScheduleLeaguesAsync(SeasonId seasonId, Func<CompetitionId, DateOnly>? firstRound, CancellationToken cancellationToken)
    {
        IReadOnlyList<Competition> leagues;
        using (var scope = scopes.CreateScope())
        {
            leagues = [.. (await LeagueCompetitionsAsync(scope.ServiceProvider, seasonId, cancellationToken).ConfigureAwait(false)).Values];
        }

        foreach (var competition in leagues.OrderBy(c => c.Id.Value))
        {
            if (await competitions.GetPhaseAsync(competition.Id, seasonId, cancellationToken).ConfigureAwait(false) == CompetitionPhase.Registration)
            {
                await competitions.GenerateScheduleAsync(competition.Id, seasonId, firstRound?.Invoke(competition.Id), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Closes the game day <paramref name="today"/>: books its salaries and income and moves the date on, in one transaction.</summary>
    private async Task<(long Salaries, long Income)> MoveCareerAsync(CareerId careerId, DateOnly today, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var career = (await Repo<Career, CareerId>(s).FindAsync(careerId, cancellationToken).ConfigureAwait(false))!;
        var money = await ClubManagementService.AddFinancesAsync(s, today, cancellationToken).ConfigureAwait(false);
        career.MoveTo(today.AddDays(1));
        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        clock.Set(career.CurrentDate);
        return money;
    }

    private async Task<int> EntriesAsync(CompetitionId competitionId, SeasonId seasonId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        return (await Repo<CompetitionParticipant, CompetitionParticipantId>(scope.ServiceProvider)
            .ListAsync(p => p.CompetitionId == competitionId && p.SeasonId == seasonId, cancellationToken).ConfigureAwait(false)).Count;
    }

    private async Task<Career?> FindCareerAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        return (await Repo<Career, CareerId>(scope.ServiceProvider).ListAsync(null, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
    }

    private async Task<Career> RequireCareerAsync(CancellationToken cancellationToken) =>
        await FindCareerAsync(cancellationToken).ConfigureAwait(false)
        ?? throw new DomainValidationException("No career has been started.");

    private static bool IsClubFixture(Fixture fixture, ClubId clubId) => fixture.HomeClubId == clubId || fixture.AwayClubId == clubId;

    /// <summary>The earliest season with clubs entered in a league competition whose leagues are not all finished.</summary>
    private static async Task<Season?> FirstLeagueSeasonAsync(IServiceProvider s, CancellationToken cancellationToken)
    {
        foreach (var season in (await Repo<Season, SeasonId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).OrderBy(x => x.StartDate))
        {
            var leagues = await LeagueCompetitionsAsync(s, season.Id, cancellationToken).ConfigureAwait(false);
            if (leagues.Count == 0)
            {
                continue;
            }

            var fixtures = await Repo<Fixture, FixtureId>(s).ListAsync(f => f.SeasonId == season.Id, cancellationToken).ConfigureAwait(false);
            if (fixtures.Count == 0 || (await UnplayedFixturesAsync(s, season.Id, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return season;
            }
        }

        return null;
    }

    /// <summary>League competitions with clubs entered in the season.</summary>
    private static async Task<IReadOnlyDictionary<CompetitionId, Competition>> LeagueCompetitionsAsync(IServiceProvider s, SeasonId seasonId, CancellationToken cancellationToken)
    {
        var ids = (await Repo<CompetitionParticipant, CompetitionParticipantId>(s).ListAsync(p => p.SeasonId == seasonId, cancellationToken).ConfigureAwait(false))
            .Select(p => p.CompetitionId)
            .ToHashSet();
        return (await Repo<Competition, CompetitionId>(s).ListAsync(c => c.Type == CompetitionType.League, cancellationToken).ConfigureAwait(false))
            .Where(c => ids.Contains(c.Id))
            .ToDictionary(c => c.Id);
    }

    private static async Task<List<Fixture>> UnplayedFixturesAsync(IServiceProvider s, SeasonId seasonId, CancellationToken cancellationToken)
    {
        var fixtures = await Repo<Fixture, FixtureId>(s).ListAsync(f => f.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var ids = fixtures.Select(f => f.Id).ToList();
        var played = (await Repo<Match, MatchId>(s).ListAsync(m => ids.Contains(m.FixtureId), cancellationToken).ConfigureAwait(false))
            .Select(m => m.FixtureId)
            .ToHashSet();
        return [.. fixtures.Where(f => !played.Contains(f.Id))];
    }

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();
}
