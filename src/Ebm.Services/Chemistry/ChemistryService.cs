using Ebm.Domain.Chemistry;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Matches;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;
using Ebm.Domain.Staff;
using Ebm.Services.Clubs;
using Ebm.Services.Data;
using Ebm.Services.Tactics;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Chemistry;

/// <summary>Implements <see cref="IChemistryService"/> over the rosters, box scores and the pure <see cref="ChemistryModel"/>.</summary>
internal sealed class ChemistryService(IServiceScopeFactory scopes, IClubService clubs, ITacticsService tactics, GameClock clock) : IChemistryService
{
    /// <summary>Notable relationships shown per club: the strongest and the weakest.</summary>
    public const int ShownRelationships = 5;

    public async Task<ChemistryWeek> ProcessWeekAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.Today;
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var games = await GamesAsync(s, null, today, cancellationToken).ConfigureAwait(false);
        var weekFrom = today.AddDays(-6);
        var active = games.Where(g => g.Date >= weekFrom).SelectMany(g => new[] { g.Home, g.Away }).Distinct().ToList();
        if (active.Count == 0)
        {
            return ChemistryWeek.None;
        }

        var rosters = await clubs.GetRostersAsync(active, cancellationToken).ConfigureAwait(false);

        var ids = rosters.SelectMany(r => r.FirstTeam.Select(p => p.Player.Id)).Distinct().ToList();
        var players = (await Repo<Player, PlayerId>(s).ListAsync(p => ids.Contains(p.Id), cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id);
        var promised = await PromisesAsync(s, today, cancellationToken).ConfigureAwait(false);
        var relationships = Repo<PlayerRelationship, PlayerRelationshipId>(s);
        var pairs = (await relationships.ListAsync(r => ids.Contains(r.PlayerAId) && ids.Contains(r.PlayerBId), cancellationToken).ConfigureAwait(false))
            .ToDictionary(r => (r.PlayerAId, r.PlayerBId));
        var coaches = (await StaffHiring.StaffAsync(s, today, null, [StaffRole.HeadCoach], cancellationToken).ConfigureAwait(false))
            .GroupBy(x => x.Contract.ClubId).ToDictionary(g => g.Key, g => g.MaxBy(x => x.Member.Ability).Member);
        int moraleChanges = 0, relationshipChanges = 0;
        var conflicts = new List<SquadConflict>();
        foreach (var roster in rosters)
        {
            var clubId = roster.Club.Id;
            var clubGames = games.Where(g => g.Home == clubId || g.Away == clubId).ToList();
            var week = clubGames.Where(g => g.Date >= weekFrom).ToList();
            var wins = week.Count(g => g.WinnerId == clubId);
            var losses = week.Count - wins;
            var minutes = RecentMinutes(clubGames, clubId);
            var places = ChemistryModel.Hierarchy([.. roster.FirstTeam.Where(p => players.ContainsKey(p.Player.Id))
                .Select(p => new HierarchyInput(players[p.Player.Id], p.Evaluation.Overall, p.Age, Promised(promised, p.Player.Id, clubId)))]);
            var hasLeader = places.Any(p => p.IsLeader);
            var coach = coaches.GetValueOrDefault(clubId);
            var inConflict = new HashSet<PlayerId>();
            var conflictPairs = new HashSet<(PlayerId, PlayerId)>();
            foreach (var place in places)
            {
                var player = place.Player;
                var satisfaction = Satisfaction(place, minutes);
                var isPromised = Promised(promised, player.Id, clubId) is not null;
                var change = ChemistryModel.MoraleChange(player.Morale, place.Role, isPromised ? RoleSatisfaction.NotJudged : satisfaction, wins, losses, hasLeader);
                if (coach is not null)
                {
                    // The head coach's motivation and style (docs/STAFF.md §5).
                    var coached = Math.Clamp(player.Morale + change + StaffRules.MoraleEffect(player.Morale + change, StaffRules.Effective(coach.Motivation, coach.Experience), coach.Style), Player.MinRating, Player.MaxRating);
                    change = coached - player.Morale;
                }

                if (change != 0)
                {
                    player.UpdateCondition(player.Fitness, player.Morale + change, player.InjuryStatus, player.InjuryReturnDate);
                    moraleChanges++;
                }

                if (!ChemistryModel.InConflict(player.Morale, satisfaction))
                {
                    continue;
                }

                // The rival: the teammate at his position playing the most minutes ahead of him.
                var rival = places.Where(p => p.Player.Id != player.Id && p.Player.Position == player.Position)
                    .Select(p => (p.Player, Minutes: minutes.GetValueOrDefault(p.Player.Id)))
                    .Where(p => p.Minutes > minutes.GetValueOrDefault(player.Id))
                    .OrderByDescending(p => p.Minutes).ThenBy(p => p.Player.Id.Value)
                    .Select(p => p.Player)
                    .FirstOrDefault();
                inConflict.Add(player.Id);
                if (rival is not null)
                {
                    conflictPairs.Add(PlayerRelationship.Order(player.Id, rival.Id));
                }

                conflicts.Add(new SquadConflict(clubId, player.Id, player.FullName, rival?.FullName));
            }

            // Shared court time this week: both played at least SharedMinutes in the same game.
            var onCourt = week.Select(g => g.Minutes.Where(m => m.Key.Club == clubId && m.Value >= ChemistryModel.SharedMinutes).Select(m => m.Key.Player).ToHashSet()).ToList();
            for (var i = 0; i < places.Count; i++)
            {
                for (var j = i + 1; j < places.Count; j++)
                {
                    var (a, b) = (places[i].Player, places[j].Player);
                    var key = PlayerRelationship.Order(a.Id, b.Id);
                    var shared = onCourt.Count(set => set.Contains(a.Id) && set.Contains(b.Id));
                    var change = ChemistryModel.RelationshipChange(shared, a.NationalityId == b.NationalityId, conflictPairs.Contains(key));
                    if (change == 0)
                    {
                        continue;
                    }

                    if (!pairs.TryGetValue(key, out var relationship))
                    {
                        relationship = new PlayerRelationship(PlayerRelationshipId.New(), a.Id, b.Id, today);
                        relationships.Add(relationship);
                        pairs[key] = relationship;
                    }

                    relationship.Adjust(change, today);
                    relationshipChanges++;
                }
            }
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ChemistryWeek(rosters.Count, moraleChanges, relationshipChanges, conflicts);
    }

    public async Task<ClubChemistry?> GetClubChemistryAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        if (await clubs.GetRosterAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } roster)
        {
            return null;
        }

        var today = clock.Today;
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var minutes = RecentMinutes(await GamesAsync(s, clubId, today, cancellationToken).ConfigureAwait(false), clubId);
        var promised = await PromisesAsync(s, today, cancellationToken).ConfigureAwait(false);
        var places = ChemistryModel.Hierarchy([.. roster.FirstTeam.Select(p => new HierarchyInput(p.Player, p.Evaluation.Overall, p.Age, Promised(promised, p.Player.Id, clubId)))]);
        var members = places.Select(place =>
        {
            var satisfaction = Satisfaction(place, minutes);
            return new SquadMember(
                place,
                minutes.Count == 0 ? null : minutes.GetValueOrDefault(place.Player.Id),
                satisfaction,
                Promised(promised, place.Player.Id, clubId) is not null,
                ChemistryModel.InConflict(place.Player.Morale, satisfaction));
        }).ToList();
        var pairs = await PairsAsync(s, [.. places.Select(p => p.Player.Id)], cancellationToken).ConfigureAwait(false);
        var names = places.ToDictionary(p => p.Player.Id, p => p.Player.FullName);
        List<PlayerRelationship> notable = [.. pairs.Values.Where(r => r.Score != 0).OrderByDescending(r => r.Score).ThenBy(r => r.Id.Value)];
        var shown = notable.Take(ShownRelationships).Concat(notable.Skip(ShownRelationships).TakeLast(ShownRelationships).Where(r => r.Score < 0));
        var teamTactics = await tactics.GetMatchTacticsAsync(clubId, cancellationToken).ConfigureAwait(false);
        var core = roster.Starters.Concat(roster.Rotation.Take(ChemistryModel.CoreRotation))
            .Select(p => (p.Player, teamTactics.AssignmentOf(p.Player).Role))
            .ToList();
        return new ClubChemistry(
            roster.Club,
            ChemistryModel.Team(core, Lookup(pairs)),
            members,
            [.. shown.Select(r => new RelationshipLine(names[r.PlayerAId], names[r.PlayerBId], r.Score))]);
    }

    public async Task<TeamChemistry> RateAsync(TeamSheet sheet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var core = sheet.Core.Select(p => (p.Player, sheet.Tactics.AssignmentOf(p.Player).Role)).ToList();
        using var scope = scopes.CreateScope();
        var pairs = await PairsAsync(scope.ServiceProvider, [.. core.Select(p => p.Player.Id)], cancellationToken).ConfigureAwait(false);
        return ChemistryModel.Team(core, Lookup(pairs));
    }

    /// <summary>A played (not forfeited) game in the promise window, with every player's minutes.</summary>
    private sealed record Game(DateOnly Date, ClubId Home, ClubId Away, ClubId WinnerId, IReadOnlyDictionary<(ClubId Club, PlayerId Player), int> Minutes);

    /// <summary>Games of the last <see cref="PromiseRules.ReviewDays"/> days, of one club or all.</summary>
    private static async Task<List<Game>> GamesAsync(IServiceProvider s, ClubId? clubId, DateOnly today, CancellationToken cancellationToken)
    {
        var from = today.AddDays(-PromiseRules.ReviewDays + 1);
        var repo = Repo<Fixture, FixtureId>(s);
        var fixtures = (clubId is { } id
                ? await repo.ListUntrackedAsync(f => f.Date >= from && f.Date <= today && (f.HomeClubId == id || f.AwayClubId == id), cancellationToken).ConfigureAwait(false)
                : await repo.ListUntrackedAsync(f => f.Date >= from && f.Date <= today, cancellationToken).ConfigureAwait(false))
            .ToDictionary(f => f.Id);
        var fixtureIds = fixtures.Keys.ToList();
        var matches = await Repo<Match, MatchId>(s).ListUntrackedAsync(m => fixtureIds.Contains(m.FixtureId) && !m.IsForfeit, cancellationToken).ConfigureAwait(false);
        var matchIds = matches.Select(m => m.Id).ToList();
        var lines = (await Repo<PlayerMatchStatistics, PlayerMatchStatisticsId>(s).ListUntrackedAsync(l => matchIds.Contains(l.MatchId), cancellationToken).ConfigureAwait(false))
            .ToLookup(l => l.MatchId);
        return
        [
            .. matches.Select(m =>
            {
                var f = fixtures[m.FixtureId];
                return new Game(
                    f.Date,
                    f.HomeClubId,
                    f.AwayClubId,
                    m.HomeScore > m.AwayScore ? f.HomeClubId : f.AwayClubId,
                    lines[m.Id].GroupBy(l => (l.ClubId, l.PlayerId)).ToDictionary(g => g.Key, g => g.Sum(l => l.BoxScore.Minutes)));
            }),
        ];
    }

    /// <summary>Minutes per club game of every player who played for the club, when it played at least <see cref="PromiseRules.MinGames"/> games.</summary>
    private static Dictionary<PlayerId, double> RecentMinutes(IReadOnlyList<Game> games, ClubId clubId)
    {
        var own = games.Where(g => g.Home == clubId || g.Away == clubId).ToList();
        return own.Count < PromiseRules.MinGames
            ? []
            : own.SelectMany(g => g.Minutes.Where(m => m.Key.Club == clubId))
                .GroupBy(m => m.Key.Player)
                .ToDictionary(g => g.Key, g => g.Sum(m => m.Value) / (double)own.Count);
    }

    /// <summary>Judged when the club played enough games and the player is healthy; a player who did not play has 0 minutes.</summary>
    private static RoleSatisfaction Satisfaction(HierarchyPlace place, Dictionary<PlayerId, double> minutes) =>
        minutes.Count == 0 || place.Player.InjuryStatus != InjuryStatus.Healthy
            ? RoleSatisfaction.NotJudged
            : ChemistryModel.Satisfaction(place.ExpectedMinutes, minutes.GetValueOrDefault(place.Player.Id));

    /// <summary>Running contracts with playing-time promises, by player.</summary>
    private static async Task<Dictionary<PlayerId, Contract>> PromisesAsync(IServiceProvider s, DateOnly today, CancellationToken cancellationToken) =>
        (await Repo<Contract, ContractId>(s).ListAsync(c => c.PlayerId != null && (c.PromisedRole != null || c.PromisedMinutes != null), cancellationToken).ConfigureAwait(false))
            .Where(c => c.Status == ContractStatus.Active && c.IsRunningOn(today))
            .GroupBy(c => c.PlayerId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

    private static ContractPromises? Promised(Dictionary<PlayerId, Contract> promised, PlayerId playerId, ClubId clubId) =>
        promised.TryGetValue(playerId, out var contract) && contract.ClubId == clubId ? contract.Promises : null;

    private static async Task<Dictionary<(PlayerId, PlayerId), PlayerRelationship>> PairsAsync(IServiceProvider s, List<PlayerId> ids, CancellationToken cancellationToken) =>
        (await Repo<PlayerRelationship, PlayerRelationshipId>(s).ListAsync(r => ids.Contains(r.PlayerAId) && ids.Contains(r.PlayerBId), cancellationToken).ConfigureAwait(false))
            .ToDictionary(r => (r.PlayerAId, r.PlayerBId));

    private static Func<PlayerId, PlayerId, int> Lookup(Dictionary<(PlayerId, PlayerId), PlayerRelationship> pairs) =>
        (a, b) => pairs.TryGetValue(PlayerRelationship.Order(a, b), out var r) ? r.Score : 0;

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();
}
