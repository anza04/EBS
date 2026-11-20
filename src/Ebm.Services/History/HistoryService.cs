using System.Globalization;
using Ebm.Domain.Careers;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.History;
using Ebm.Domain.Management;
using Ebm.Domain.Matches;
using Ebm.Domain.News;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;
using Ebm.Domain.Scouting;
using Ebm.Domain.Seasons;
using Ebm.Domain.Staff;
using Ebm.Services.Clubs;
using Ebm.Services.Competitions;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.History;

/// <summary>Implements <see cref="IHistoryService"/> over the repositories and the pure <see cref="HistoryRules"/>.</summary>
internal sealed class HistoryService(IServiceScopeFactory scopes, ICompetitionService competitions, PlayerRatingCalculator calculator) : IHistoryService
{
    /// <summary>Youth contracts run this many years.</summary>
    private const int YouthContractYears = 3;

    public async Task<HistorySeason> ProcessSeasonEndAsync(SeasonId seasonId, DateOnly date, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var season = await Repo<Season, SeasonId>(s).FindAsync(seasonId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainValidationException("Unknown season.");
        var entries = await Repo<CompetitionParticipant, CompetitionParticipantId>(s).ListAsync(p => p.SeasonId == seasonId, cancellationToken).ConfigureAwait(false);
        var ids = entries.Select(e => e.CompetitionId).Distinct().ToList();
        var leagues = (await Repo<Competition, CompetitionId>(s).ListAsync(c => ids.Contains(c.Id) && c.Type == CompetitionType.League, cancellationToken).ConfigureAwait(false))
            .OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        var clubs = (await Repo<Club, ClubId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id);
        var awards = Repo<SeasonAward, SeasonAwardId>(s);
        var snapshots = Repo<LeagueSeasonSnapshot, LeagueSeasonSnapshotId>(s);
        var (awarded, snapped) = (0, 0);
        Dictionary<ClubId, List<int>>? overalls = null;

        foreach (var league in leagues)
        {
            if (await competitions.GetPhaseAsync(league.Id, seasonId, cancellationToken).ConfigureAwait(false) != CompetitionPhase.Completed)
            {
                continue;
            }

            var leagueId = league.Id;
            var members = entries.Where(e => e.CompetitionId == leagueId && clubs.ContainsKey(e.ClubId)).Select(e => clubs[e.ClubId]).ToList();
            var table = await competitions.GetStandingsAsync(leagueId, seasonId, cancellationToken).ConfigureAwait(false);
            if ((await awards.ListUntrackedAsync(a => a.SeasonId == seasonId && a.CompetitionId == leagueId, cancellationToken).ConfigureAwait(false)).Count == 0)
            {
                var lines = await SeasonLinesAsync(s, leagueId, season, table, cancellationToken).ConfigureAwait(false);
                foreach (var pick in HistoryRules.Awards(lines))
                {
                    awards.Add(new SeasonAward(SeasonAwardId.New(), seasonId, leagueId, pick.Kind, pick.Rank, pick.Line.ClubId, pick.Line.PlayerId, null, pick.Value));
                    awarded++;
                }

                // Coach of the season: reputation rank (before the season's verdict moves it) against the final position.
                var ranks = members.OrderByDescending(c => c.Reputation).ThenBy(c => c.Name, StringComparer.Ordinal).Select((c, i) => (c.Id, Rank: i + 1)).ToDictionary(x => x.Id, x => x.Rank);
                if (HistoryRules.CoachOfTheSeason(table.Where(r => ranks.ContainsKey(r.ClubId)).Select(r => new ClubSeasonResult(r.ClubId, ranks[r.ClubId], r.Position))) is { } best)
                {
                    awards.Add(new SeasonAward(SeasonAwardId.New(), seasonId, leagueId, AwardKind.CoachOfTheSeason, 1, best.ClubId, null, await HeadCoachAsync(s, best.ClubId, date, cancellationToken).ConfigureAwait(false), best.ReputationRank - best.Position));
                    awarded++;
                }
            }

            if (members.Count > 0 && (await snapshots.ListUntrackedAsync(x => x.CompetitionId == leagueId && x.SeasonId == seasonId, cancellationToken).ConfigureAwait(false)).Count == 0)
            {
                overalls ??= await SquadOverallsAsync(s, date, cancellationToken).ConfigureAwait(false);
                var strength = members.Select(c => overalls.TryGetValue(c.Id, out var o) ? o.OrderDescending().Take(8).DefaultIfEmpty(0).Average() : 0).Average();
                var champion = (await competitions.GetOutcomeAsync(leagueId, seasonId, cancellationToken).ConfigureAwait(false)).Champion;
                snapshots.Add(new LeagueSeasonSnapshot(
                    LeagueSeasonSnapshotId.New(), leagueId, seasonId, members.Count, (int)Math.Round(members.Average(c => c.Reputation)), members.Max(c => c.Reputation), (int)Math.Round(strength), champion));
                snapped++;
            }
        }

        // Long-term evolution: every reputation drifts back towards the middle, once a season.
        var changes = Repo<ReputationChange, ReputationChangeId>(s);
        var done = (await changes.ListUntrackedAsync(c => c.SeasonId == seasonId && c.Cause == ReputationCause.LongTermDrift, cancellationToken).ConfigureAwait(false))
            .Select(c => c.ClubId).ToHashSet();
        var drifted = 0;
        foreach (var club in clubs.Values.Where(c => !done.Contains(c.Id)).OrderBy(c => c.Id.Value))
        {
            var before = club.Reputation;
            club.ChangeReputation(HistoryRules.Drift(before));
            if (club.Reputation != before)
            {
                changes.Add(new ReputationChange(ReputationChangeId.New(), $"reputation:{seasonId}:{club.Id}:{ReputationCause.LongTermDrift}", club.Id, date, ReputationCause.LongTermDrift, before, club.Reputation, seasonId, null));
                drifted++;
            }
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new HistorySeason(awarded, snapped, drifted);
    }

    public async Task<RetirementHistory> ProcessRetirementsAsync(IReadOnlyList<PlayerId> retired, DateOnly date, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(retired);
        if (retired.Count == 0)
        {
            return new([], []);
        }

        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var ids = retired.Distinct().ToList();
        var players = await Repo<Player, PlayerId>(s).ListUntrackedAsync(p => ids.Contains(p.Id) && p.RetiredOn != null, cancellationToken).ConfigureAwait(false);
        var hall = Repo<HallOfFameEntry, HallOfFameEntryId>(s);
        var staff = Repo<StaffMember, StaffMemberId>(s);
        var inducted = (await hall.ListUntrackedAsync(h => ids.Contains(h.PlayerId), cancellationToken).ConfigureAwait(false)).Select(h => h.PlayerId).ToHashSet();
        var coaching = (await staff.ListUntrackedAsync(m => m.FormerPlayerId != null && ids.Contains(m.FormerPlayerId.Value), cancellationToken).ConfigureAwait(false))
            .Select(m => m.FormerPlayerId!.Value).ToHashSet();
        var (newInductees, newCoaches) = (new List<PlayerId>(), new List<StaffMemberId>());
        foreach (var player in players.OrderBy(p => p.Id.Value))
        {
            var summary = (await CareerAsync(s, player, cancellationToken).ConfigureAwait(false)).Summary;
            var score = HistoryRules.HallOfFameScore(summary);
            var famous = inducted.Contains(player.Id);
            if (!famous && score >= HistoryRules.HallOfFameThreshold)
            {
                hall.Add(new HallOfFameEntry(HallOfFameEntryId.New(), player.Id, date, summary, score));
                newInductees.Add(player.Id);
                famous = true;
            }

            if (!coaching.Contains(player.Id) && HistoryRules.CoachingRole(player.Attributes, famous) is { } role)
            {
                // A new staff member without a contract: he is on the staff market (docs/STAFF.md §5).
                var coach = new StaffMember(StaffMemberId.New(), player.FirstName, player.LastName, player.DateOfBirth, player.NationalityId, role, HistoryRules.CoachProfile(player)) { FormerPlayerId = player.Id };
                staff.Add(coach);
                newCoaches.Add(coach.Id);
            }
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new(newInductees, newCoaches);
    }

    public async Task<YouthIntake> ProcessYouthIntakeAsync(DateOnly date, IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(managedClubs);
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var playerRepo = Repo<Player, PlayerId>(s);
        var players = await playerRepo.ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false);
        var clubs = (await Repo<Club, ClubId>(s).ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false)).OrderBy(c => c.Id.Value).ToList();
        if (players.Count == 0 || clubs.Count == 0)
        {
            return new(0, 0);
        }

        // Clubs whose intake of this date already happened (repeat-safe).
        var contracts = Repo<Contract, ContractId>(s);
        var academy = players.Where(p => p.YouthClubId is not null).ToDictionary(p => p.Id, p => p.YouthClubId!.Value);
        var academyIds = academy.Keys.ToList();
        var done = (await contracts.ListUntrackedAsync(c => c.StartDate == date && c.PlayerId != null && academyIds.Contains(c.PlayerId.Value), cancellationToken).ConfigureAwait(false))
            .Where(c => academy[c.PlayerId!.Value] == c.ClubId).Select(c => c.ClubId).ToHashSet();

        var taken = players.Select(DuplicateKeys.Of).ToHashSet();
        var pools = players.GroupBy(p => p.NationalityId)
            .ToDictionary(g => g.Key, g => new NamePool(g.Key, [.. g.Select(p => p.FirstName).Distinct()], [.. g.Select(p => p.LastName).Distinct()]));
        var generator = new PlayerGenerator((date.DayNumber * 31) + players.Count);
        var created = 0;
        foreach (var club in clubs.Where(c => !done.Contains(c.Id)))
        {
            var random = new Random(ScoutingRules.StableSeed(club.Id.Value, Guid.Empty, date.DayNumber));
            var names = pools.GetValueOrDefault(club.CountryId) ?? pools.Values.OrderByDescending(p => p.FirstNames.Count).First();
            for (var i = 0; i < HistoryRules.IntakeSize(club.Reputation); i++)
            {
                var (quality, potential) = HistoryRules.Prospect(club.Reputation, random);
                var player = generator.CreateYouth(names, generator.NextPosition(), date, (f, l, d) => taken.Contains(DuplicateKeys.Player(f, l, d)), club.Id, HistoryRules.YouthAge, quality, potential);
                taken.Add(DuplicateKeys.Of(player));
                playerRepo.Add(player);
                contracts.Add(Contract.ForPlayer(ContractId.New(), club.Id, player.Id, date, date.AddYears(YouthContractYears), ContractNegotiator.MinimumSalary));
                await ClubMembership.JoinAsync(s, player.Id, club.Id, date, RosterRole.Youth, cancellationToken).ConfigureAwait(false);
                created++;
            }
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // AI clubs promote youth players who are good enough, while the first team has room. Players at a club without a
        // roster spot count as first team (as in the squad screens).
        var promoted = 0;
        var members = await ClubMembership.CurrentClubsAsync(s, date, cancellationToken).ConfigureAwait(false);
        var spots = (await Repo<RosterSpot, RosterSpotId>(s).ListAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(x => x.PlayerId);
        var active = (await playerRepo.ListUntrackedAsync(p => p.RetiredOn == null, cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id);
        foreach (var squad in members.Where(m => !managedClubs.Contains(m.Value) && active.ContainsKey(m.Key)).GroupBy(m => m.Value, m => active[m.Key]).OrderBy(g => g.Key.Value))
        {
            bool IsYouth(Player p) => spots.TryGetValue(p.Id, out var spot) && spot.ClubId == squad.Key && spot.Role == RosterRole.Youth;
            var youth = squad.Where(IsYouth).Select(p => (p.Id, p.AgeOn(date), calculator.Evaluate(p).Overall)).ToList();
            if (youth.Count == 0)
            {
                continue;
            }

            var firstTeam = squad.Where(p => !IsYouth(p)).Select(p => calculator.Evaluate(p).Overall).ToList();
            foreach (var id in HistoryRules.Promotions(youth, firstTeam, SquadRules.Default.MaxPlayers))
            {
                spots[id].Assign(RosterRole.Inactive, null, null);
                promoted++;
            }
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new(created, promoted);
    }

    public async Task<PlayerCareer?> GetPlayerCareerAsync(PlayerId playerId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        if (await Repo<Player, PlayerId>(s).FindAsync(playerId, cancellationToken).ConfigureAwait(false) is not { } player)
        {
            return null;
        }

        var career = await CareerAsync(s, player, cancellationToken).ConfigureAwait(false);
        var names = await NamesAsync(s, cancellationToken).ConfigureAwait(false);
        var hall = (await Repo<HallOfFameEntry, HallOfFameEntryId>(s).ListUntrackedAsync(h => h.PlayerId == playerId, cancellationToken).ConfigureAwait(false)).SingleOrDefault();
        var spells = (await Repo<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>(s).ListUntrackedAsync(h => h.PlayerId == playerId, cancellationToken).ConfigureAwait(false))
            .OrderBy(h => h.StartDate).Select(h => new CareerSpell(names.Club(h.ClubId), h.StartDate, h.EndDate)).ToList();
        var coach = (await Repo<StaffMember, StaffMemberId>(s).ListUntrackedAsync(m => m.FormerPlayerId == playerId, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        var awards = await AwardLinesAsync(s, a => a.PlayerId == playerId, names, cancellationToken).ConfigureAwait(false);
        return new PlayerCareer(
            player.Id, player.FullName, player.RetiredOn, hall is null ? null : Hall(hall, player.FullName), career.Lines, career.Totals, awards, spells, career.Summary.Titles, coach?.Role,
            player.YouthClubId is { } academy ? names.Club(academy) : null);
    }

    public async Task<ClubHonours> GetClubHonoursAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var names = await NamesAsync(s, cancellationToken).ConfigureAwait(false);
        var trophies = new List<(DateOnly Start, Trophy Trophy)>();
        foreach (var competition in names.Competitions.Values.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            foreach (var entry in await competitions.GetHistoryAsync(competition.Id, cancellationToken).ConfigureAwait(false))
            {
                if (entry.Champion == clubId)
                {
                    trophies.Add((entry.Season.StartDate, new Trophy(entry.Season.Name, competition.Name)));
                }
            }
        }

        var awards = await AwardLinesAsync(s, a => a.ClubId == clubId, names, cancellationToken).ConfigureAwait(false);
        var academy = (await Repo<Player, PlayerId>(s).ListUntrackedAsync(p => p.YouthClubId == clubId, cancellationToken).ConfigureAwait(false)).Count;
        return new([.. trophies.OrderByDescending(t => t.Start).ThenBy(t => t.Trophy.Competition, StringComparer.Ordinal).Select(t => t.Trophy)], awards, academy);
    }

    public async Task<IReadOnlyList<AwardLine>> GetAwardsAsync(CompetitionId? competitionId = null, SeasonId? seasonId = null, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var names = await NamesAsync(s, cancellationToken).ConfigureAwait(false);
        return await AwardLinesAsync(s, a => (competitionId == null || a.CompetitionId == competitionId) && (seasonId == null || a.SeasonId == seasonId), names, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HallOfFameLine>> GetHallOfFameAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var entries = await Repo<HallOfFameEntry, HallOfFameEntryId>(s).ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false);
        var ids = entries.Select(e => e.PlayerId).ToList();
        var players = (await Repo<Player, PlayerId>(s).ListUntrackedAsync(p => ids.Contains(p.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id, p => p.FullName);
        return [.. entries.OrderByDescending(e => e.Score).ThenBy(e => e.InductedOn).Select(e => Hall(e, players.GetValueOrDefault(e.PlayerId) ?? "?"))];
    }

    public async Task<IReadOnlyList<WinnerLine>> GetWinnersAsync(CompetitionId competitionId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var names = await NamesAsync(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
        return
        [
            .. (await competitions.GetHistoryAsync(competitionId, cancellationToken).ConfigureAwait(false))
                .Where(h => h.Phase == CompetitionPhase.Completed && h.Champion is not null)
                .OrderByDescending(h => h.Season.StartDate)
                .Select(h => new WinnerLine(h.Season.Name, names.Club(h.Champion!.Value), h.RunnerUp is { } r ? names.Club(r) : "—")),
        ];
    }

    public async Task<IReadOnlyList<RecordLine>> GetRecordsAsync(CompetitionId competitionId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var names = await NamesAsync(s, cancellationToken).ConfigureAwait(false);
        var seasons = (await Repo<Season, SeasonId>(s).ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(x => x.Id);
        var fixtures = (await Repo<Fixture, FixtureId>(s).ListUntrackedAsync(f => f.CompetitionId == competitionId, cancellationToken).ConfigureAwait(false)).ToDictionary(f => f.Id);
        var fixtureIds = fixtures.Keys.ToList();
        var matches = (await Repo<Match, MatchId>(s).ListUntrackedAsync(m => fixtureIds.Contains(m.FixtureId) && !m.IsForfeit, cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.Id);
        var matchIds = matches.Keys.ToList();
        var stats = await Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>(s).ListUntrackedAsync(p => matchIds.Contains(p.MatchId), cancellationToken).ConfigureAwait(false);
        await names.LoadPlayersAsync(stats.Select(p => p.PlayerId), cancellationToken).ConfigureAwait(false);
        var records = new List<RecordLine>();
        string SeasonOf(MatchId match) => seasons.TryGetValue(fixtures[matches[match].FixtureId].SeasonId, out var x) ? x.Name : "?";
        string When(MatchId match) => fixtures[matches[match].FixtureId].Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
        string Line(int value, string unit, MatchId match) => string.Create(CultureInfo.InvariantCulture, $"{value} {unit} ({When(match)})");

        void GameRecord(string label, string unit, Func<BoxScore, int> value)
        {
            if (stats.OrderByDescending(p => value(p.BoxScore)).ThenBy(p => fixtures[matches[p.MatchId].FixtureId].Date).FirstOrDefault() is { } top && value(top.BoxScore) > 0)
            {
                records.Add(new RecordLine(label, names.Player(top.PlayerId), names.Club(top.ClubId), SeasonOf(top.MatchId), Line(value(top.BoxScore), unit, top.MatchId)));
            }
        }

        GameRecord("Most points in a game", "pts", b => b.Points);
        GameRecord("Most rebounds in a game", "reb", b => b.Rebounds);
        GameRecord("Most assists in a game", "ast", b => b.Assists);
        if (matches.Values.Select(m => (Match: m, Fixture: fixtures[m.FixtureId]))
            .SelectMany(x => new[] { (Club: x.Fixture.HomeClubId, Score: x.Match.HomeScore, x.Match.Id), (Club: x.Fixture.AwayClubId, Score: x.Match.AwayScore, x.Match.Id) })
            .OrderByDescending(x => x.Score).ThenBy(x => fixtures[matches[x.Id].FixtureId].Date).FirstOrDefault() is { Score: > 0 } team)
        {
            records.Add(new RecordLine("Most points by a team in a game", names.Club(team.Club), names.Club(team.Club), SeasonOf(team.Id), Line(team.Score, "pts", team.Id)));
        }

        // Best season scoring average (regular season, award eligibility).
        var best = stats.Where(p => fixtures[matches[p.MatchId].FixtureId].Stage == FixtureStage.RegularSeason)
            .GroupBy(p => fixtures[matches[p.MatchId].FixtureId].SeasonId)
            .SelectMany(season =>
            {
                var players = season.GroupBy(p => p.PlayerId).Select(g => (Player: g.Key, Club: g.GroupBy(p => p.ClubId).MaxBy(c => c.Count())!.Key, Games: g.Count(), Points: g.Sum(p => p.BoxScore.Points))).ToList();
                var min = Math.Max(1, (int)Math.Ceiling(players.Max(p => p.Games) * HistoryRules.MinGameShare));
                return players.Where(p => p.Games >= min).Select(p => (Season: season.Key, p.Player, p.Club, Average: (double)p.Points / p.Games));
            })
            .OrderByDescending(x => x.Average).FirstOrDefault();
        if (best.Average > 0)
        {
            records.Add(new RecordLine("Best scoring average in a season", names.Player(best.Player), names.Club(best.Club), seasons.TryGetValue(best.Season, out var x) ? x.Name : "?", string.Create(CultureInfo.InvariantCulture, $"{best.Average:0.0} pts a game")));
        }

        // Best regular-season record and most titles, from the completed seasons.
        var history = await competitions.GetHistoryAsync(competitionId, cancellationToken).ConfigureAwait(false);
        (string Club, string Season, int Won, int Played)? record = null;
        foreach (var entry in history.Where(h => h.Phase == CompetitionPhase.Completed))
        {
            if ((await competitions.GetStandingsAsync(competitionId, entry.Season.Id, cancellationToken).ConfigureAwait(false))
                .Where(r => r.Played > 0).OrderByDescending(r => (double)r.Won / r.Played).ThenByDescending(r => r.Won).FirstOrDefault() is { } row
                && (record is null || (double)row.Won / row.Played > (double)record.Value.Won / record.Value.Played))
            {
                record = (names.Club(row.ClubId), entry.Season.Name, row.Won, row.Played);
            }
        }

        if (record is { } r)
        {
            records.Add(new RecordLine("Best regular-season record", r.Club, r.Club, r.Season, string.Create(CultureInfo.InvariantCulture, $"{r.Won}–{r.Played - r.Won}")));
        }

        if (history.Where(h => h.Champion is not null).GroupBy(h => h.Champion!.Value).OrderByDescending(g => g.Count()).ThenBy(g => g.Min(h => h.Season.StartDate)).FirstOrDefault() is { } titles)
        {
            records.Add(new RecordLine("Most titles", names.Club(titles.Key), names.Club(titles.Key), string.Join(", ", titles.OrderBy(h => h.Season.StartDate).Select(h => h.Season.Name)), titles.Count().ToString(CultureInfo.InvariantCulture)));
        }

        return records;
    }

    public async Task<IReadOnlyList<LeagueEvolutionLine>> GetLeagueEvolutionAsync(CompetitionId competitionId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var names = await NamesAsync(s, cancellationToken).ConfigureAwait(false);
        var seasons = (await Repo<Season, SeasonId>(s).ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(x => x.Id);
        var snapshots = await Repo<LeagueSeasonSnapshot, LeagueSeasonSnapshotId>(s).ListUntrackedAsync(x => x.CompetitionId == competitionId, cancellationToken).ConfigureAwait(false);
        return
        [
            .. snapshots.OrderBy(x => seasons.TryGetValue(x.SeasonId, out var season) ? season.StartDate : DateOnly.MaxValue)
                .Select(x => new LeagueEvolutionLine(seasons.TryGetValue(x.SeasonId, out var season) ? season.Name : "?", x.Clubs, x.MeanReputation, x.TopReputation, x.MeanOverall, names.Club(x.Champion))),
        ];
    }

    private static HallOfFameLine Hall(HallOfFameEntry e, string name) => new(e.PlayerId, name, e.InductedOn, e.Score, e.Seasons, e.Games, e.Points, e.Titles, e.Awards);

    /// <summary>The players' regular-season lines of a league season, with their club's share of wins.</summary>
    private static async Task<IReadOnlyList<PlayerSeasonLine>> SeasonLinesAsync(IServiceProvider s, CompetitionId competitionId, Season season, IReadOnlyList<Domain.Competitions.Engine.StandingRow> table, CancellationToken cancellationToken)
    {
        var seasonId = season.Id;
        var fixtureIds = (await Repo<Fixture, FixtureId>(s).ListUntrackedAsync(f => f.CompetitionId == competitionId && f.SeasonId == seasonId && f.Stage == FixtureStage.RegularSeason, cancellationToken).ConfigureAwait(false))
            .Select(f => f.Id).ToList();
        var matchIds = (await Repo<Match, MatchId>(s).ListUntrackedAsync(m => fixtureIds.Contains(m.FixtureId) && !m.IsForfeit, cancellationToken).ConfigureAwait(false)).Select(m => m.Id).ToList();
        var stats = await Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>(s).ListUntrackedAsync(p => matchIds.Contains(p.MatchId), cancellationToken).ConfigureAwait(false);
        var playerIds = stats.Select(p => p.PlayerId).Distinct().ToList();
        var players = (await Repo<Player, PlayerId>(s).ListUntrackedAsync(p => playerIds.Contains(p.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id);
        var wins = table.ToDictionary(r => r.ClubId, r => r.Played == 0 ? 0 : (double)r.Won / r.Played);
        return
        [
            .. stats.Where(p => players.ContainsKey(p.PlayerId)).GroupBy(p => p.PlayerId).Select(g =>
            {
                var club = g.GroupBy(p => p.ClubId).OrderByDescending(c => c.Count()).ThenBy(c => c.Key.Value).First().Key;
                var b = g.Select(p => p.BoxScore).ToList();
                return new PlayerSeasonLine(
                    g.Key, club, players[g.Key].AgeOn(season.StartDate), b.Count, b.Sum(x => x.Minutes), b.Sum(x => x.Points), b.Sum(x => x.Rebounds), b.Sum(x => x.DefensiveRebounds),
                    b.Sum(x => x.Assists), b.Sum(x => x.Steals), b.Sum(x => x.Blocks), b.Sum(x => x.Turnovers), wins.GetValueOrDefault(club));
            }),
        ];
    }

    /// <summary>A player's career from his box scores: lines per competition season and club, totals and the hall of fame summary.</summary>
    private async Task<(IReadOnlyList<CareerLine> Lines, CareerLine Totals, CareerSummary Summary)> CareerAsync(IServiceProvider s, Player player, CancellationToken cancellationToken)
    {
        var playerId = player.Id;
        var stats = await Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>(s).ListUntrackedAsync(p => p.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
        var matchIds = stats.Select(p => p.MatchId).ToList();
        var matches = (await Repo<Match, MatchId>(s).ListUntrackedAsync(m => matchIds.Contains(m.Id) && !m.IsForfeit, cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.Id);
        var fixtureIds = matches.Values.Select(m => m.FixtureId).ToList();
        var fixtures = (await Repo<Fixture, FixtureId>(s).ListUntrackedAsync(f => fixtureIds.Contains(f.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(f => f.Id);
        var names = await NamesAsync(s, cancellationToken).ConfigureAwait(false);
        var games = stats.Where(p => matches.ContainsKey(p.MatchId) && fixtures.ContainsKey(matches[p.MatchId].FixtureId))
            .Select(p => (Stats: p, Fixture: fixtures[matches[p.MatchId].FixtureId])).ToList();

        var lines = new List<CareerLine>();
        var titles = 0;
        foreach (var group in games.GroupBy(g => (g.Fixture.SeasonId, g.Fixture.CompetitionId, g.Stats.ClubId)).OrderBy(g => names.SeasonStart(g.Key.SeasonId)).ThenBy(g => names.Competition(g.Key.CompetitionId), StringComparer.Ordinal))
        {
            lines.Add(Line(names.SeasonName(group.Key.SeasonId), names.Competition(group.Key.CompetitionId), names.Club(group.Key.ClubId), [.. group.Select(g => g.Stats.BoxScore)]));
            if (await competitions.GetPhaseAsync(group.Key.CompetitionId, group.Key.SeasonId, cancellationToken).ConfigureAwait(false) == CompetitionPhase.Completed
                && (await competitions.GetOutcomeAsync(group.Key.CompetitionId, group.Key.SeasonId, cancellationToken).ConfigureAwait(false)).Champion == group.Key.ClubId)
            {
                titles++;
            }
        }

        var all = games.Select(g => g.Stats.BoxScore).ToList();
        var awards = await Repo<SeasonAward, SeasonAwardId>(s).ListUntrackedAsync(a => a.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
        var summary = new CareerSummary(
            games.Select(g => g.Fixture.SeasonId).Distinct().Count(), all.Count, all.Sum(b => b.Points), all.Sum(b => b.Rebounds), all.Sum(b => b.Assists), titles,
            awards.Count(a => a.Kind == AwardKind.MostValuablePlayer), awards.Count(a => a.Kind is not (AwardKind.MostValuablePlayer or AwardKind.AllLeagueTeam)),
            awards.Count(a => a.Kind == AwardKind.AllLeagueTeam), player.Reputation);
        return (lines, Line("Career", string.Empty, string.Empty, all), summary);
    }

    private static CareerLine Line(string season, string competition, string club, IReadOnlyList<BoxScore> games)
    {
        double PerGame(Func<BoxScore, int> f) => games.Count == 0 ? 0 : Math.Round((double)games.Sum(f) / games.Count, 1);
        return new CareerLine(season, competition, club, games.Count, PerGame(b => b.Minutes), PerGame(b => b.Points), PerGame(b => b.Rebounds), PerGame(b => b.Assists), games.Sum(b => b.Points));
    }

    private static async Task<IReadOnlyList<AwardLine>> AwardLinesAsync(IServiceProvider s, System.Linq.Expressions.Expression<Func<SeasonAward, bool>> filter, Names names, CancellationToken cancellationToken)
    {
        var awards = await Repo<SeasonAward, SeasonAwardId>(s).ListUntrackedAsync(filter, cancellationToken).ConfigureAwait(false);
        await names.LoadPlayersAsync(awards.Where(a => a.PlayerId is not null).Select(a => a.PlayerId!.Value), cancellationToken).ConfigureAwait(false);
        var staffIds = awards.Where(a => a.StaffMemberId is not null).Select(a => a.StaffMemberId!.Value).ToList();
        var staff = (await Repo<StaffMember, StaffMemberId>(s).ListUntrackedAsync(m => staffIds.Contains(m.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.Id, m => $"{m.FirstName} {m.LastName}");
        var manager = (await Repo<Career, CareerId>(s).ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        string Winner(SeasonAward a) => a.PlayerId is { } p ? names.Player(p)
            : a.StaffMemberId is { } m && staff.TryGetValue(m, out var n) ? n
            : manager is not null && manager.ClubId == a.ClubId ? manager.ManagerName
            : $"{names.Club(a.ClubId)} coach";
        return
        [
            .. awards.OrderByDescending(a => names.SeasonStart(a.SeasonId)).ThenBy(a => names.Competition(a.CompetitionId), StringComparer.Ordinal).ThenBy(a => a.Kind).ThenBy(a => a.Rank)
                .Select(a => new AwardLine(a.SeasonId, names.SeasonName(a.SeasonId), a.CompetitionId, names.Competition(a.CompetitionId), a.Kind, a.Rank, a.PlayerId, Winner(a), a.ClubId, names.Club(a.ClubId),
                    a.Kind == AwardKind.CoachOfTheSeason ? a.Value : a.Value / 10.0)),
        ];
    }

    /// <summary>The head coach under contract at the club on <paramref name="date"/>.</summary>
    private static async Task<StaffMemberId?> HeadCoachAsync(IServiceProvider s, ClubId clubId, DateOnly date, CancellationToken cancellationToken)
    {
        var running = (await Repo<Contract, ContractId>(s).ListUntrackedAsync(Contract.RunningOn(date), cancellationToken).ConfigureAwait(false))
            .Where(c => c.ClubId == clubId && c.StaffMemberId is not null).Select(c => c.StaffMemberId!.Value).ToList();
        return (await Repo<StaffMember, StaffMemberId>(s).ListUntrackedAsync(m => running.Contains(m.Id) && m.Role == StaffRole.HeadCoach, cancellationToken).ConfigureAwait(false))
            .OrderBy(m => m.Id.Value).FirstOrDefault()?.Id;
    }

    /// <summary>Overall ratings of every active player, by current club.</summary>
    private async Task<Dictionary<ClubId, List<int>>> SquadOverallsAsync(IServiceProvider s, DateOnly date, CancellationToken cancellationToken)
    {
        var members = await ClubMembership.CurrentClubsAsync(s, date, cancellationToken).ConfigureAwait(false);
        var ids = members.Keys.ToList();
        var players = await Repo<Player, PlayerId>(s).ListUntrackedAsync(p => ids.Contains(p.Id) && p.RetiredOn == null, cancellationToken).ConfigureAwait(false);
        return players.GroupBy(p => members[p.Id]).ToDictionary(g => g.Key, g => g.Select(p => calculator.Evaluate(p).Overall).ToList());
    }

    private static async Task<Names> NamesAsync(IServiceProvider s, CancellationToken cancellationToken) => new(
        (await Repo<Club, ClubId>(s).ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id, c => c.Name),
        (await Repo<Competition, CompetitionId>(s).ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id),
        (await Repo<Season, SeasonId>(s).ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(c => c.Id),
        Repo<Player, PlayerId>(s));

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();

    /// <summary>Display names, players loaded on demand.</summary>
    private sealed class Names(Dictionary<ClubId, string> clubs, Dictionary<CompetitionId, Competition> competitions, Dictionary<SeasonId, Season> seasons, IRepository<Player, PlayerId> players)
    {
        private readonly Dictionary<PlayerId, string> _players = [];

        public Dictionary<CompetitionId, Competition> Competitions => competitions;

        public string Club(ClubId id) => clubs.GetValueOrDefault(id) ?? "?";

        public string Competition(CompetitionId id) => competitions.TryGetValue(id, out var c) ? c.Name : "?";

        public string SeasonName(SeasonId id) => seasons.TryGetValue(id, out var x) ? x.Name : "?";

        public DateOnly SeasonStart(SeasonId id) => seasons.TryGetValue(id, out var x) ? x.StartDate : DateOnly.MinValue;

        public string Player(PlayerId id) => _players.GetValueOrDefault(id) ?? "A player";

        /// <summary>Loads the names of <paramref name="ids"/> for <see cref="Player"/>.</summary>
        public async Task LoadPlayersAsync(IEnumerable<PlayerId> ids, CancellationToken cancellationToken)
        {
            var missing = ids.Where(id => !_players.ContainsKey(id)).Distinct().ToList();
            if (missing.Count > 0)
            {
                foreach (var p in await players.ListUntrackedAsync(p => missing.Contains(p.Id), cancellationToken).ConfigureAwait(false))
                {
                    _players[p.Id] = p.FullName;
                }
            }
        }
    }
}
