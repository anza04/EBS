using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions.Engine;

namespace Ebm.Tests.Unit.Competitions;

public sealed class RoundRobinSchedulerTests
{
    public static TheoryData<int, int> Sizes()
    {
        var data = new TheoryData<int, int>();
        foreach (var clubs in new[] { 2, 3, 4, 5, 8, 11, 16, 18, 20 })
        {
            foreach (var legs in new[] { 1, 2, 4 })
            {
                data.Add(clubs, legs);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Every_pair_meets_once_per_leg_and_clubs_play_once_per_round(int count, int legs)
    {
        var clubs = Clubs(count);

        var games = RoundRobinScheduler.Generate(clubs, legs);

        Assert.Equal(count * (count - 1) / 2 * legs, games.Count);
        Assert.Equal(RoundRobinScheduler.RoundsPerLeg(count) * legs, games.Max(g => g.Round));
        Assert.All(clubs, c => Assert.Equal(RoundRobinScheduler.GamesPerClub(count, legs), games.Count(g => g.Home == c || g.Away == c)));
        Assert.All(games.GroupBy(g => g.Round), round =>
        {
            var playing = round.SelectMany(g => new[] { g.Home, g.Away }).ToList();
            Assert.Equal(playing.Count, playing.Distinct().Count());
        });
        Assert.All(games.GroupBy(g => Pair(g.Home, g.Away)), pair => Assert.Equal(legs, pair.Count()));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Home_and_away_games_are_balanced(int count, int legs)
    {
        var clubs = Clubs(count);
        var perLeg = RoundRobinScheduler.RoundsPerLeg(count);

        var games = RoundRobinScheduler.Generate(clubs, legs);

        foreach (var club in clubs)
        {
            var firstLeg = games.Where(g => g.Round <= perLeg && (g.Home == club || g.Away == club)).OrderBy(g => g.Round).ToList();
            var home = firstLeg.Count(g => g.Home == club);
            Assert.InRange(home - (firstLeg.Count - home), -1, 1);
            var pattern = string.Concat(firstLeg.Select(g => g.Home == club ? 'H' : 'A'));
            Assert.DoesNotContain("HHH", pattern, StringComparison.Ordinal);
            Assert.DoesNotContain("AAA", pattern, StringComparison.Ordinal);
        }

        if (legs == 2)
        {
            // Home and away: each pair plays once at each club.
            Assert.All(games.GroupBy(g => Pair(g.Home, g.Away)), pair => Assert.Equal(1, pair.Count(g => g.Home == pair.First().Home)));
        }
    }

    [Fact]
    public void Invalid_inputs_are_refused()
    {
        var club = ClubId.New();

        Assert.Throws<DomainValidationException>(() => RoundRobinScheduler.Generate([club], 2));
        Assert.Throws<DomainValidationException>(() => RoundRobinScheduler.Generate([club, club], 2));
        Assert.Throws<DomainValidationException>(() => RoundRobinScheduler.Generate(Clubs(4), 0));
    }

    internal static List<ClubId> Clubs(int count) => [.. Enumerable.Range(0, count).Select(_ => ClubId.New())];

    private static (ClubId, ClubId) Pair(ClubId a, ClubId b) => a.Value.CompareTo(b.Value) < 0 ? (a, b) : (b, a);
}
