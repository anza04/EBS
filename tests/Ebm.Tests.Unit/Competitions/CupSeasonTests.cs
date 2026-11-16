using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Competitions.Rules;
using Ebm.Domain.Matches;
using Ebm.Domain.Matches.Simulation;
using Ebm.Tests.Unit.Matches;

namespace Ebm.Tests.Unit.Competitions;

/// <summary>Cup seasons through <see cref="CupSeason"/>; unless told otherwise the better seed wins every game.</summary>
public sealed class CupSeasonTests
{
    private const string FinalEight = """{"cup":{"draw":"Bracket","rounds":[{"name":"Quarter-finals"},{"name":"Semi-finals"},{"name":"Final"}]}}""";

    [Fact]
    public void Bracket_cup_pairs_one_with_eight_and_the_top_seeds_reach_the_final()
    {
        var seeds = RoundRobinSchedulerTests.Clubs(8);
        var phases = new List<SeasonPhase>();

        var cup = Run(Rules(FinalEight), seeds, onPhase: phases.Add);

        var first = cup.Ties().Where(t => t.Round == 1).Select(t => (seeds.IndexOf(t.First) + 1, seeds.IndexOf(t.Second) + 1));
        Assert.Equal([(1, 8), (4, 5), (2, 7), (3, 6)], first);
        Assert.Equal([SeasonPhase.Knockout, SeasonPhase.Completed], phases.Distinct());
        Assert.Equal((seeds[0], seeds[1]), (cup.Winner, cup.RunnerUp));
        var outcome = cup.Outcome();
        Assert.Equal((seeds[0], seeds[1], 0, 0), (outcome.Champion, outcome.RunnerUp, outcome.Promoted.Count, outcome.Relegated.Count));
        Assert.Equal(["Quarter-finals", "Semi-finals", "Final"], cup.Ties().Select(t => t.RoundName).Distinct());
        Assert.Equal(SeasonPhase.Registration, new CupSeason(Rules(FinalEight), [], [], 1).Phase);
        Assert.Throws<DomainValidationException>(() => new CupSeason(Rules(FinalEight), seeds, [], 1).Outcome());
    }

    [Theory]
    [InlineData("Bracket")]
    [InlineData("Seeded")]
    [InlineData("Open")]
    public void Top_seeds_get_the_first_round_byes(string draw)
    {
        var seeds = RoundRobinSchedulerTests.Clubs(6);
        var rules = Rules($$$"""{"cup":{"draw":"{{{draw}}}","rounds":[{"name":"R1"},{"name":"SF"},{"name":"F"}]}}""");

        var cup = Run(rules, seeds);

        Assert.Equal([seeds[0], seeds[1]], cup.Byes.OrderBy(seeds.IndexOf));
        Assert.Equal(2, cup.Ties().Count(t => t.Round == 1));
        Assert.DoesNotContain(cup.Ties().Where(t => t.Round == 1), t => t.First == seeds[0] || t.Second == seeds[0]);
        Assert.Equal(seeds[0], cup.Winner);
    }

    [Fact]
    public void Seeded_draw_puts_the_better_half_against_the_other_half_and_repeats_with_the_same_seed()
    {
        var seeds = RoundRobinSchedulerTests.Clubs(16);
        var rules = Rules("""{"cup":{"draw":"Seeded","rounds":[{"name":"R16"},{"name":"QF"},{"name":"SF"},{"name":"F"}]}}""");

        var first = new CupSeason(rules, seeds, [], 42).NextGames();
        var again = new CupSeason(rules, seeds, [], 42).NextGames();

        Assert.Equal(8, first.Count);
        Assert.All(first, g => Assert.True(seeds.IndexOf(g.Home) < 8 ^ seeds.IndexOf(g.Away) < 8));
        Assert.All(first, g => Assert.True(seeds.IndexOf(g.Home) < seeds.IndexOf(g.Away), "the better seed hosts"));
        Assert.Equal(first, again);
        var drawn = Enumerable.Range(1, 20).Select(s => string.Join(",", new CupSeason(rules, seeds, [], s).NextGames().Select(g => seeds.IndexOf(g.Away))));
        Assert.True(drawn.Distinct().Count() > 1, "different draw seeds give different draws");
    }

    [Fact]
    public void Open_draw_can_pair_any_clubs_and_lower_seeds_host_when_configured()
    {
        var seeds = RoundRobinSchedulerTests.Clubs(8);
        var rules = Rules("""{"cup":{"draw":"Open","host":"LowerSeed","rounds":[{"name":"QF"},{"name":"SF"},{"name":"F"}]}}""");

        var draws = Enumerable.Range(1, 30).Select(s => new CupSeason(rules, seeds, [], s).NextGames()).ToList();

        Assert.All(draws.SelectMany(d => d), g => Assert.True(seeds.IndexOf(g.Home) > seeds.IndexOf(g.Away)));
        Assert.Contains(draws.SelectMany(d => d), g => seeds.IndexOf(g.Home) < 4 && seeds.IndexOf(g.Away) < 4);
        Assert.Equal(seeds[0], Run(rules, seeds, drawSeed: 9).Winner);
    }

    [Fact]
    public void Two_legged_ties_are_decided_on_aggregate_with_the_second_leg_at_the_better_seed()
    {
        var seeds = RoundRobinSchedulerTests.Clubs(2);
        var rules = Rules("""{"cup":{"rounds":[{"name":"Final","legs":2}]}}""");
        var leg1 = new CupSeason(rules, seeds, [], 1).NextGames().Single();
        Assert.Equal((seeds[1], seeds[0], 1), (leg1.Home, leg1.Away, leg1.GameNumber));

        // The weaker seed wins the first leg by 10 and loses the second by 5: it goes through on aggregate.
        List<SeasonGame> games = [new(FixtureStage.Cup, 1, 1, seeds[1], seeds[0], 90, 80)];
        var leg2 = new CupSeason(rules, seeds, games, 1).NextGames().Single();
        Assert.Equal((seeds[0], seeds[1], 2), (leg2.Home, leg2.Away, leg2.GameNumber));
        games.Add(new(FixtureStage.Cup, 1, 2, seeds[0], seeds[1], 85, 80));
        var cup = new CupSeason(rules, seeds, games, 1);

        var tie = cup.Ties().Single();
        Assert.Equal((170, 165), (tie.FirstPoints, tie.SecondPoints));
        Assert.Equal((seeds[1], seeds[0]), (cup.Winner, cup.RunnerUp));
        Assert.Equal(SeasonPhase.Completed, cup.Phase);

        // A level aggregate (after a forfeit) goes to the winner of the second leg.
        games[1] = new(FixtureStage.Cup, 1, 2, seeds[0], seeds[1], 90, 80);
        Assert.Equal(seeds[0], new CupSeason(rules, seeds, games, 1).Winner);
    }

    [Fact]
    public void Group_stage_draws_one_club_per_pot_and_sends_the_group_winners_on_as_top_seeds()
    {
        var seeds = RoundRobinSchedulerTests.Clubs(8);
        var rules = Rules("""{"cup":{"draw":"Bracket","groupStage":{"groups":2,"legs":1,"advance":2},"rounds":[{"name":"SF"},{"name":"F"}]}}""");
        var phases = new List<SeasonPhase>();

        var cup = Run(rules, seeds, onPhase: phases.Add);

        Assert.Equal([SeasonPhase.GroupStage, SeasonPhase.Knockout, SeasonPhase.Completed], phases.Distinct());
        var groups = cup.Groups();
        Assert.Equal([[0, 3, 4, 7], [1, 2, 5, 6]], groups.Select(g => g.Select(c => seeds.IndexOf(c)).Order().ToArray()));
        Assert.Equal(["Group A", "Group B"], cup.GroupTables().Select(t => t.Name));
        Assert.All(cup.GroupTables(), t => Assert.All(t.Rows, r => Assert.Equal(3, r.Played)));

        // Semi-finals: each group winner against the other group's runner-up (A1 v B2, B1 v A2).
        var semis = cup.Ties().Where(t => t.Round == 1).Select(t => (seeds.IndexOf(t.First), seeds.IndexOf(t.Second))).ToList();
        Assert.Equal([(0, 2), (1, 3)], semis);
        Assert.Equal(seeds[0], cup.Winner);
    }

    [Fact]
    public void Games_that_do_not_match_the_draw_are_rejected()
    {
        var seeds = RoundRobinSchedulerTests.Clubs(8);
        var rules = Rules(FinalEight);

        Assert.Throws<DomainValidationException>(() => new CupSeason(rules, seeds, [new(FixtureStage.Cup, 1, 1, seeds[0], seeds[1], 80, 70)], 1).Ties());
        Assert.Throws<DomainValidationException>(() => new CupSeason(rules, seeds, [new(FixtureStage.Cup, 2, 1, seeds[0], seeds[3])], 1).Ties());
        Assert.Throws<DomainValidationException>(() => new CupSeason(rules, [seeds[0], seeds[0]], [], 1));
        Assert.Throws<DomainValidationException>(() => new CupSeason(Rules("{}"), seeds, [], 1));
    }

    [Theory]
    [InlineData("""{"cup":{"rounds":[]}}""")]
    [InlineData("""{"cup":{"rounds":[{"name":"F","legs":3}]}}""")]
    [InlineData("""{"cup":{"rounds":[{"name":""}]}}""")]
    [InlineData("""{"cup":{"rounds":[{"name":"F"}],"draw":"Swiss"}}""")]
    [InlineData("""{"cup":{"rounds":[{"name":"F"}],"startOffsetDays":400}}""")]
    [InlineData("""{"cup":{"rounds":[{"name":"SF"},{"name":"F"}],"groupStage":{"groups":4,"advance":2}}}""")]
    [InlineData("""{"cup":{"rounds":[{"name":"SF"},{"name":"F"}],"qualification":{"positions":8,"afterRound":15}}}""")]
    [InlineData("""{"cup":{"rounds":[{"name":"F"}],"qualification":{"positions":2}}}""")]
    [InlineData("""{"cup":{"rounds":[{"name":"F"}]},"playoffs":{"teams":2,"bestOf":[1]}}""")]
    [InlineData("""{"cup":{"rounds":[{"name":"F"}]},"relegation":{"direct":1}}""")]
    public void Invalid_cup_rules_are_refused(string json) =>
        Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse(json));

    [Fact]
    public void Cup_rules_check_the_number_of_clubs_and_belong_to_cups_only()
    {
        var rules = Rules(FinalEight);
        rules.ValidateForClubs(5);
        rules.ValidateForClubs(8);
        Assert.Throws<DomainValidationException>(() => rules.ValidateForClubs(4));
        Assert.Throws<DomainValidationException>(() => rules.ValidateForClubs(9));
        Assert.Throws<DomainValidationException>(() => Rules("""{"cup":{"rounds":[{"name":"SF"},{"name":"F"}],"groupStage":{"groups":2,"advance":2}}}""").ValidateForClubs(5));

        var league = new Competition(CompetitionId.New(), "League", "L", CompetitionType.League, null, null);
        Assert.Throws<DomainValidationException>(() => league.UpdateRules(FinalEight));
        var cup = new Competition(CompetitionId.New(), "Cup", "C", CompetitionType.Cup, null, null, FinalEight);
        Assert.Throws<DomainValidationException>(() => cup.UpdateDetails("Cup", "C", CompetitionType.League, null, null));
        Assert.Equal(3, cup.ParseRules().Cup!.Rounds.Count);
    }

    [Fact]
    public void A_second_leg_goes_to_overtime_while_the_aggregate_is_level()
    {
        const int margin = 3;
        var normal = Enumerable.Range(0, 300).Select(s => Play(s, 0)).ToList();
        var secondLegs = Enumerable.Range(0, 300).Select(s => Play(s, margin)).ToList();

        Assert.Contains(normal, r => r.HomeScore - r.AwayScore == margin);
        Assert.DoesNotContain(secondLegs, r => r.HomeScore - r.AwayScore == margin || r.HomeScore == r.AwayScore);
        Assert.True(secondLegs.Sum(r => r.Overtimes) > normal.Sum(r => r.Overtimes));

        static MatchResult Play(int seed, int overtimeMargin) =>
            new MatchSimulation(new MatchSetup(SimulationTeams.Team("Home", 60, seed), SimulationTeams.Team("Away", 60, seed + 7), seed) { OvertimeMargin = overtimeMargin }).RunToEnd();
    }

    private static CompetitionRules Rules(string json) => CompetitionRules.Parse(json);

    /// <summary>Plays the cup to the end: every requested game is won by the better seed (by 10 points, at home or away).</summary>
    private static CupSeason Run(CompetitionRules rules, IReadOnlyList<ClubId> seeds, int drawSeed = 1, Action<SeasonPhase>? onPhase = null)
    {
        var games = new List<SeasonGame>();
        for (var guard = 0; guard < 100; guard++)
        {
            var cup = new CupSeason(rules, seeds, games, drawSeed);
            onPhase?.Invoke(cup.Phase);
            var next = cup.NextGames();
            if (next.Count == 0)
            {
                return cup;
            }

            foreach (var g in next)
            {
                var homeBetter = IndexOf(seeds, g.Home) < IndexOf(seeds, g.Away);
                games.Add(new SeasonGame(g.Stage, g.Round, g.GameNumber, g.Home, g.Away, homeBetter ? 90 : 80, homeBetter ? 80 : 90));
            }
        }

        throw new InvalidOperationException("The cup never ended.");
    }

    private static int IndexOf(IReadOnlyList<ClubId> seeds, ClubId club) => seeds.ToList().IndexOf(club);
}
