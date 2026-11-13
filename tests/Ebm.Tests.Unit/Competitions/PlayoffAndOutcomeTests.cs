using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Competitions.Rules;
using Ebm.Domain.Countries;

namespace Ebm.Tests.Unit.Competitions;

public sealed class PlayoffAndOutcomeTests
{
    [Fact]
    public void Bracket_order_keeps_top_seeds_apart()
    {
        Assert.Equal([1, 2], PlayoffBracket.BracketOrder(2));
        Assert.Equal([1, 4, 2, 3], PlayoffBracket.BracketOrder(4));
        Assert.Equal([1, 8, 4, 5, 2, 7, 3, 6], PlayoffBracket.BracketOrder(8));
    }

    [Theory]
    [InlineData(1, "H")]
    [InlineData(3, "HAH")]
    [InlineData(5, "HHAAH")]
    [InlineData(7, "HHAAHAH")]
    public void Higher_seed_has_home_court(int bestOf, string pattern)
    {
        var actual = string.Concat(Enumerable.Range(1, bestOf).Select(g => PlayoffBracket.HigherSeedHosts(g, bestOf) ? 'H' : 'A'));

        Assert.Equal(pattern, actual);
    }

    [Fact]
    public void Series_are_played_game_by_game_until_a_champion()
    {
        var seeds = RoundRobinSchedulerTests.Clubs(4);
        var rules = new PlayoffRules { Teams = 4, BestOf = [3, 1] };
        var games = new List<PlayoffGame>();

        var bracket = PlayoffBracket.Build(seeds, rules, games);
        var first = bracket.NextGames();
        Assert.Equal([(seeds[0], seeds[3]), (seeds[1], seeds[2])], first.Select(g => (g.Home, g.Away)));

        // While a game is pending nothing new is scheduled.
        games.AddRange(first.Select(g => new PlayoffGame(g.Round, g.GameNumber, g.Home, g.Away)));
        Assert.Empty(PlayoffBracket.Build(seeds, rules, games).NextGames());

        var champion = Play(seeds, rules, games, winner: (home, away) => home == seeds[3] || away == seeds[3] ? seeds[3] : home);

        Assert.Equal(seeds[3], champion);
        var final = PlayoffBracket.Build(seeds, rules, games).Series.Single(s => s.Round == 2);
        Assert.Equal((seeds[1], seeds[3]), (final.HigherSeed, final.LowerSeed));
        Assert.Equal(2 + 3 + 1, games.Count);
    }

    [Fact]
    public void Games_outside_the_bracket_are_refused()
    {
        var seeds = RoundRobinSchedulerTests.Clubs(4);
        var rules = new PlayoffRules { Teams = 4, BestOf = [1, 1] };

        Assert.Throws<DomainValidationException>(() => PlayoffBracket.Build(seeds, rules, [new PlayoffGame(1, 1, seeds[0], seeds[1], 80, 70)]));
        Assert.Throws<DomainValidationException>(() => PlayoffBracket.Build(seeds, rules, [new PlayoffGame(1, 2, seeds[0], seeds[3], 80, 70)]));
        Assert.Throws<DomainValidationException>(() => PlayoffBracket.Build(seeds[..3], rules, []));
    }

    [Fact]
    public void Registration_checks_country_capacity_and_other_leagues()
    {
        var italy = CountryId.New();
        var league = new Competition(CompetitionId.New(), "Serie A", "SA", CompetitionType.League, italy, null, """{"registration":{"maxTeams":2}}""");
        var club = new Club(ClubId.New(), "Mock Milano", "MIL", italy);
        var foreign = new Club(ClubId.New(), "Mock Madrid", "MAD", CountryId.New());

        CompetitionRegistration.Check(league, club, 1, inAnotherLeague: false);
        Assert.Contains("country", Assert.Throws<DomainValidationException>(() => CompetitionRegistration.Check(league, foreign, 0, false)).Message, StringComparison.Ordinal);
        Assert.Contains("full", Assert.Throws<DomainValidationException>(() => CompetitionRegistration.Check(league, club, 2, false)).Message, StringComparison.Ordinal);
        Assert.Contains("another league", Assert.Throws<DomainValidationException>(() => CompetitionRegistration.Check(league, club, 0, true)).Message, StringComparison.Ordinal);

        var cup = new Competition(CompetitionId.New(), "Cup", "CUP", CompetitionType.Cup, italy, null);
        CompetitionRegistration.Check(cup, club, 0, inAnotherLeague: true);

        var crossBorder = new Competition(CompetitionId.New(), "BNXT", "BNXT", CompetitionType.League, null, null, """{"registration":{"countries":["BEL","NLD"]}}""");
        CompetitionRegistration.Check(crossBorder, club, 0, false, "NLD");
        Assert.Contains("BEL, NLD", Assert.Throws<DomainValidationException>(() => CompetitionRegistration.Check(crossBorder, club, 0, false, "ITA")).Message, StringComparison.Ordinal);
    }

    /// <summary>Plays every requested game with the given winner until the bracket has a champion.</summary>
    private static ClubId Play(IReadOnlyList<ClubId> seeds, PlayoffRules rules, List<PlayoffGame> games, Func<ClubId, ClubId, ClubId> winner)
    {
        for (var guard = 0; guard < 50; guard++)
        {
            for (var i = 0; i < games.Count; i++)
            {
                if (!games[i].IsPlayed)
                {
                    var g = games[i];
                    var homeWins = winner(g.Home, g.Away) == g.Home;
                    games[i] = g with { HomeScore = homeWins ? 80 : 70, AwayScore = homeWins ? 70 : 80 };
                }
            }

            var bracket = PlayoffBracket.Build(seeds, rules, games);
            if (bracket.Champion is { } champion)
            {
                return champion;
            }

            games.AddRange(bracket.NextGames().Select(r => new PlayoffGame(r.Round, r.GameNumber, r.Home, r.Away)));
        }

        throw new InvalidOperationException("The playoffs did not finish.");
    }
}
