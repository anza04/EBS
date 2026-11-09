using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions.Rules;

namespace Ebm.Domain.Competitions.Engine;

/// <summary>A scheduled playoff game; the scores are <c>null</c> until it is played.</summary>
public sealed record PlayoffGame(int Round, int GameNumber, ClubId Home, ClubId Away, int? HomeScore = null, int? AwayScore = null)
{
    public bool IsPlayed => HomeScore is not null && AwayScore is not null;

    public ClubId? Winner => IsPlayed ? (HomeScore > AwayScore ? Home : Away) : null;
}

/// <summary>A playoff game the bracket needs scheduled next.</summary>
public sealed record PlayoffGameRequest(int Round, int GameNumber, ClubId Home, ClubId Away);

/// <summary>The state of one best-of series. The higher seed has home court.</summary>
public sealed record PlayoffSeries(
    int Round,
    ClubId HigherSeed,
    ClubId LowerSeed,
    int BestOf,
    int HigherSeedWins,
    int LowerSeedWins,
    int GamesScheduled,
    bool HasUnplayedGame)
{
    public int WinsNeeded => (BestOf / 2) + 1;

    public ClubId? Winner =>
        HigherSeedWins >= WinsNeeded ? HigherSeed : LowerSeedWins >= WinsNeeded ? LowerSeed : null;

    public bool IsDecided => Winner is not null;
}

/// <summary>
/// A fixed single-elimination bracket (no reseeding): 1 v N, 2 v N-1, … placed so the top two seeds can only meet
/// in the final. Built from the seeds and the games scheduled so far, it reports each series and the games to
/// schedule next. Home court pattern: best of 3 = H-A-H, best of 5 = 2-2-1, best of 7 = 2-2-1-1-1.
/// </summary>
public sealed class PlayoffBracket
{
    private PlayoffBracket(IReadOnlyList<PlayoffSeries> series, ClubId? champion)
    {
        Series = series;
        Champion = champion;
    }

    /// <summary>Every series whose two clubs are known, round by round.</summary>
    public IReadOnlyList<PlayoffSeries> Series { get; }

    public ClubId? Champion { get; }

    public bool IsComplete => Champion is not null;

    /// <param name="seeds">Qualified clubs, best seed first; the count must equal <see cref="PlayoffRules.Teams"/>.</param>
    public static PlayoffBracket Build(IReadOnlyList<ClubId> seeds, PlayoffRules rules, IEnumerable<PlayoffGame> games)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(games);
        if (seeds.Count != rules.Teams || seeds.Distinct().Count() != seeds.Count)
        {
            throw new DomainValidationException($"The playoffs need {rules.Teams} different clubs.");
        }

        var allGames = games.ToList();
        var seedOf = seeds.Select((club, i) => (club, i)).ToDictionary(x => x.club, x => x.i);
        List<ClubId?> entrants = [.. BracketOrder(rules.Teams).Select(seed => (ClubId?)seeds[seed - 1])];
        var series = new List<PlayoffSeries>();
        var used = 0;
        for (var round = 1; round <= rules.Rounds; round++)
        {
            var winners = new List<ClubId?>();
            for (var i = 0; i < entrants.Count; i += 2)
            {
                if (entrants[i] is not { } a || entrants[i + 1] is not { } b)
                {
                    winners.Add(null);
                    continue;
                }

                var (higher, lower) = seedOf[a] < seedOf[b] ? (a, b) : (b, a);
                var seriesGames = allGames
                    .Where(g => g.Round == round && ((g.Home == a && g.Away == b) || (g.Home == b && g.Away == a)))
                    .OrderBy(g => g.GameNumber)
                    .ToList();
                var current = new PlayoffSeries(
                    round,
                    higher,
                    lower,
                    rules.BestOf[round - 1],
                    seriesGames.Count(g => g.Winner == higher),
                    seriesGames.Count(g => g.Winner == lower),
                    seriesGames.Count,
                    seriesGames.Any(g => !g.IsPlayed));
                if (!seriesGames.Select(g => g.GameNumber).SequenceEqual(Enumerable.Range(1, seriesGames.Count))
                    || seriesGames.Count > current.BestOf)
                {
                    throw new DomainValidationException($"The games of playoff round {round} are inconsistent.");
                }

                used += seriesGames.Count;
                series.Add(current);
                winners.Add(current.Winner);
            }

            entrants = winners;
        }

        if (used != allGames.Count)
        {
            throw new DomainValidationException("A playoff game does not belong to the bracket.");
        }

        return new PlayoffBracket(series, entrants.Single());
    }

    /// <summary>Games to schedule now: the next game of every undecided series with no game pending.</summary>
    public IReadOnlyList<PlayoffGameRequest> NextGames() =>
        [.. Series
            .Where(s => !s.IsDecided && !s.HasUnplayedGame)
            .Select(s =>
            {
                var game = s.GamesScheduled + 1;
                return HigherSeedHosts(game, s.BestOf)
                    ? new PlayoffGameRequest(s.Round, game, s.HigherSeed, s.LowerSeed)
                    : new PlayoffGameRequest(s.Round, game, s.LowerSeed, s.HigherSeed);
            })];

    /// <summary>Seed numbers in bracket order; adjacent pairs meet in the first round (8 teams: 1-8, 4-5, 2-7, 3-6).</summary>
    public static IReadOnlyList<int> BracketOrder(int teams)
    {
        List<int> order = [1];
        while (order.Count < teams)
        {
            var size = order.Count * 2;
            order = [.. order.SelectMany(seed => new[] { seed, size + 1 - seed })];
        }

        return order;
    }

    /// <summary>Whether the higher seed is at home in game <paramref name="gameNumber"/> of a best-of series.</summary>
    public static bool HigherSeedHosts(int gameNumber, int bestOf) => bestOf switch
    {
        <= 3 => gameNumber % 2 == 1,
        _ => gameNumber <= 2 || (gameNumber > 4 && gameNumber % 2 == 1),
    };
}
