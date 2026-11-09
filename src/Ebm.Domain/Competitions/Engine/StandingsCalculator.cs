using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions.Rules;

namespace Ebm.Domain.Competitions.Engine;

/// <summary>A played game as the standings see it.</summary>
public sealed record GameResult(ClubId Home, ClubId Away, int HomeScore, int AwayScore);

/// <summary>One ranked row of a table. <see cref="Points"/> are standing points from the competition rules.</summary>
public sealed record StandingRow(int Position, ClubId ClubId, int Played, int Won, int Lost, int PointsFor, int PointsAgainst, int Points)
{
    public int PointDifference => PointsFor - PointsAgainst;
}

/// <summary>
/// Ranks clubs by standing points, then by the configured tie-breakers. When a criterion splits a group of tied
/// clubs, each smaller group is ranked again from the first criterion, so head-to-head games are recounted among
/// the clubs still level. Clubs equal on every criterion are ordered by club ID (a stand-in for drawing lots).
/// </summary>
public static class StandingsCalculator
{
    public static IReadOnlyList<StandingRow> Calculate(IEnumerable<ClubId> clubs, IEnumerable<GameResult> results, CompetitionRules rules)
    {
        ArgumentNullException.ThrowIfNull(clubs);
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(rules);

        var tallies = clubs.Distinct().ToDictionary(c => c, c => new Tally(c));
        var games = results.ToList();
        foreach (var game in games)
        {
            if (!tallies.TryGetValue(game.Home, out var home) || !tallies.TryGetValue(game.Away, out var away))
            {
                throw new DomainValidationException("A result involves a club that is not in the competition.");
            }

            if (game.HomeScore == game.AwayScore)
            {
                throw new DomainValidationException("A basketball match cannot end in a draw.");
            }

            home.Add(game.HomeScore, game.AwayScore);
            away.Add(game.AwayScore, game.HomeScore);
        }

        foreach (var tally in tallies.Values)
        {
            tally.Points = (tally.Won * rules.PointsForWin) + (tally.Lost * rules.PointsForLoss);
        }

        return tallies.Values
            .GroupBy(t => t.Points)
            .OrderByDescending(g => g.Key)
            .SelectMany(g => Break([.. g], games, rules.TieBreakers))
            .Select((t, i) => new StandingRow(i + 1, t.Club, t.Won + t.Lost, t.Won, t.Lost, t.PointsFor, t.PointsAgainst, t.Points))
            .ToList();
    }

    private static IEnumerable<Tally> Break(List<Tally> tied, List<GameResult> games, IReadOnlyList<TieBreaker> criteria)
    {
        if (tied.Count == 1)
        {
            return tied;
        }

        foreach (var criterion in criteria)
        {
            var value = Values(tied, games, criterion);
            var groups = tied.GroupBy(t => value[t.Club]).OrderByDescending(g => g.Key).ToList();
            if (groups.Count > 1)
            {
                return groups.SelectMany(g => Break([.. g], games, criteria));
            }
        }

        return tied.OrderBy(t => t.Club.Value);
    }

    private static Dictionary<ClubId, int> Values(List<Tally> tied, List<GameResult> games, TieBreaker criterion)
    {
        if (criterion is TieBreaker.HeadToHeadWins or TieBreaker.HeadToHeadPointDifference or TieBreaker.HeadToHeadPointsFor)
        {
            var mini = tied.ToDictionary(t => t.Club, t => new Tally(t.Club));
            foreach (var game in games.Where(g => mini.ContainsKey(g.Home) && mini.ContainsKey(g.Away)))
            {
                mini[game.Home].Add(game.HomeScore, game.AwayScore);
                mini[game.Away].Add(game.AwayScore, game.HomeScore);
            }

            return mini.ToDictionary(p => p.Key, p => criterion switch
            {
                TieBreaker.HeadToHeadWins => p.Value.Won,
                TieBreaker.HeadToHeadPointDifference => p.Value.PointsFor - p.Value.PointsAgainst,
                _ => p.Value.PointsFor,
            });
        }

        return tied.ToDictionary(t => t.Club, t => criterion switch
        {
            TieBreaker.PointDifference => t.PointsFor - t.PointsAgainst,
            TieBreaker.PointsFor => t.PointsFor,
            TieBreaker.FewestPointsAgainst => -t.PointsAgainst,
            TieBreaker.Wins => t.Won,
            _ => throw new ArgumentOutOfRangeException(nameof(criterion), criterion, "Unknown tie-breaker."),
        });
    }

    private sealed class Tally(ClubId club)
    {
        public ClubId Club { get; } = club;

        public int Won { get; private set; }

        public int Lost { get; private set; }

        public int PointsFor { get; private set; }

        public int PointsAgainst { get; private set; }

        public int Points { get; set; }

        public void Add(int scored, int conceded)
        {
            if (scored > conceded)
            {
                Won++;
            }
            else
            {
                Lost++;
            }

            PointsFor += scored;
            PointsAgainst += conceded;
        }
    }
}
