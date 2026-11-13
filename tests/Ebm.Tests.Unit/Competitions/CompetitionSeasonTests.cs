using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Competitions.Rules;
using Ebm.Domain.Matches;

namespace Ebm.Tests.Unit.Competitions;

/// <summary>Whole competition seasons through <see cref="CompetitionSeason"/>; the earlier club in the list always wins unless told otherwise.</summary>
public sealed class CompetitionSeasonTests
{
    [Fact]
    public void Outcome_lists_champion_promoted_and_relegated_clubs()
    {
        var clubs = RoundRobinSchedulerTests.Clubs(6);
        var rules = CompetitionRules.Parse(
            """{"playoffs":{"teams":4,"fromPosition":2,"bestOf":[1,1]},"promotion":{"direct":1,"playoffWinner":true},"relegation":{"direct":1}}""");

        var season = Run(rules, clubs);

        var outcome = season.Outcome();
        Assert.Equal(clubs[1], outcome.Champion);
        Assert.Equal([clubs[0], clubs[1]], outcome.Promoted);
        Assert.Equal([clubs[5]], outcome.Relegated);
        Assert.Equal(clubs[0], Run(CompetitionRules.Parse("{}"), clubs).Outcome().Champion);
    }

    [Fact]
    public void Phases_follow_each_other_and_an_unfinished_season_has_no_outcome()
    {
        var clubs = RoundRobinSchedulerTests.Clubs(10);
        var rules = CompetitionRules.Parse(
            """{"secondPhase":{"groups":[{"name":"Top","from":1,"to":6}]},"playoffs":{"teams":4,"bestOf":[1,1],"playIn":true}}""");
        var games = Schedule(clubs, rules);
        var phases = new List<SeasonPhase>();

        var season = Run(rules, clubs, games, onPhase: phases.Add);

        Assert.Equal([SeasonPhase.RegularSeason, SeasonPhase.SecondPhase, SeasonPhase.PlayIn, SeasonPhase.Playoffs, SeasonPhase.Completed], phases.Distinct());
        Assert.Equal(SeasonPhase.Registration, new CompetitionSeason(rules, Entries(clubs), []).Phase);
        Assert.Throws<DomainValidationException>(() => new CompetitionSeason(rules, Entries(clubs), Schedule(clubs, rules)).Outcome());
        Assert.Equal(SeasonPhase.Completed, season.Phase);
    }

    [Fact]
    public void Top_seeds_get_byes_when_the_bracket_is_not_full()
    {
        var clubs = RoundRobinSchedulerTests.Clubs(8);
        var rules = CompetitionRules.Parse("""{"playoffs":{"teams":6,"bestOf":[3,3,5]}}""");
        var games = Schedule(clubs, rules);
        PlayAll(games, clubs);

        var first = new CompetitionSeason(rules, Entries(clubs), games).NextGames();

        Assert.Equal([(clubs[3], clubs[4]), (clubs[2], clubs[5])], first.Select(g => (g.Home, g.Away)));
        var season = Run(rules, clubs, games);
        Assert.Equal(clubs[0], season.Outcome().Champion);
        Assert.Equal(2 + 2 + 1, season.Bracket()!.Series.Count);
    }

    [Fact]
    public void Play_in_decides_the_last_two_seeds()
    {
        var clubs = RoundRobinSchedulerTests.Clubs(12);
        var rules = CompetitionRules.Parse("""{"playoffs":{"teams":8,"bestOf":[1,1,1],"playIn":true}}""");
        var games = Schedule(clubs, rules);
        PlayAll(games, clubs);

        var round1 = new CompetitionSeason(rules, Entries(clubs), games).NextGames();
        Assert.Equal([(clubs[6], clubs[7]), (clubs[8], clubs[9])], round1.Select(g => (g.Home, g.Away)));
        Assert.All(round1, g => Assert.Equal(FixtureStage.PlayIn, g.Stage));

        // Upsets: 8th beats 7th, 10th beats 9th; then 7th (loser A, better position, at home) loses to 10th.
        games.AddRange(round1.Select(r => new SeasonGame(r.Stage, r.Round, r.GameNumber, r.Home, r.Away, 70, 80)));
        var round2 = new CompetitionSeason(rules, Entries(clubs), games).NextGames().Single();
        Assert.Equal((2, clubs[6], clubs[9]), (round2.Round, round2.Home, round2.Away));
        games.Add(new SeasonGame(round2.Stage, round2.Round, 1, round2.Home, round2.Away, 70, 80));

        var bracket = new CompetitionSeason(rules, Entries(clubs), games).Bracket()!;
        var opening = bracket.Series.Where(s => s.Round == 1).Select(s => (s.HigherSeed, s.LowerSeed)).ToList();
        Assert.Contains((clubs[0], clubs[9]), opening);
        Assert.Contains((clubs[1], clubs[7]), opening);
    }

    [Fact]
    public void Second_phase_groups_rank_the_final_table_and_keep_their_records()
    {
        var clubs = RoundRobinSchedulerTests.Clubs(8);
        var rules = CompetitionRules.Parse(
            """{"secondPhase":{"groups":[{"name":"Championship","from":1,"to":4},{"name":"Relegation","from":5,"to":8}],"legs":2},"relegation":{"direct":1}}""");
        var games = Schedule(clubs, rules);
        PlayAll(games, clubs);

        var requests = new CompetitionSeason(rules, Entries(clubs), games).NextGames();
        Assert.Equal(2 * 4 * 3, requests.Count);
        Assert.All(requests, r => Assert.Equal(clubs.IndexOf(r.Home) < 4, clubs.IndexOf(r.Away) < 4));

        // The relegation group's winner (5th) ends with more wins than the championship's last (4th) but stays below it.
        var season = Run(rules, clubs, games, winner: (a, b) => clubs.IndexOf(a) == 4 || clubs.IndexOf(b) == 4 ? clubs[4] : Stronger(clubs, a, b));
        var table = season.FinalTable();
        Assert.Equal(clubs, table.Select(r => r.ClubId));
        Assert.True(table[4].Won > table[3].Won);
        Assert.Equal(14 + 6, table[0].Played);
        Assert.Equal([clubs[7]], season.Outcome().Relegated);
        Assert.Equal(["Championship", "Relegation"], season.GroupTables().Select(g => g.Name));
    }

    [Theory]
    [InlineData("All", 14 + 6)]
    [InlineData("WithinGroup", 6 + 6)]
    [InlineData("None", 6)]
    public void Carry_over_decides_which_first_phase_games_count(string carryOver, int played)
    {
        var clubs = RoundRobinSchedulerTests.Clubs(8);
        var rules = CompetitionRules.Parse(
            $$$"""{"secondPhase":{"groups":[{"name":"A","from":1,"to":4},{"name":"B","from":5,"to":8}],"carryOver":"{{{carryOver}}}"}}""");

        var season = Run(rules, clubs);

        Assert.All(season.FinalTable(), r => Assert.Equal(played, r.Played));
    }

    [Fact]
    public void Groups_can_be_formed_by_position_within_each_country()
    {
        var clubs = RoundRobinSchedulerTests.Clubs(8);
        var countries = clubs.Select((c, i) => (c, i % 2 == 0 ? "BEL" : "NLD")).ToDictionary(x => x.c, x => x.Item2);
        var rules = CompetitionRules.Parse(
            """{"registration":{"sameCountry":false},"secondPhase":{"groups":[{"name":"Gold","from":1,"to":3},{"name":"Silver","from":4,"to":4}],"byCountry":true}}""");
        var games = Schedule(clubs, rules);
        PlayAll(games, clubs);

        var groups = new CompetitionSeason(rules, countries, games).GroupTables();

        Assert.Equal(clubs[..6], groups[0].Rows.Select(r => r.ClubId));
        Assert.Equal(clubs[6..], groups[1].Rows.Select(r => r.ClubId));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Playoff_places_promote_the_last_rounds_clubs(int places)
    {
        var clubs = RoundRobinSchedulerTests.Clubs(10);
        var rules = CompetitionRules.Parse(
            $$$"""{"playoffs":{"teams":8,"bestOf":[3,3,3]},"promotion":{"playoffPlaces":{{{places}}}}}""");

        var promoted = Run(rules, clubs).Outcome().Promoted;

        Assert.Equal(clubs[..places], promoted);
    }

    [Theory]
    [InlineData("""{"promotion":{"playoffPlaces":3},"playoffs":{"teams":8,"bestOf":[1,1,1]}}""", "playoffPlaces")]
    [InlineData("""{"promotion":{"playoffPlaces":2,"playoffWinner":true},"playoffs":{"teams":8,"bestOf":[1,1,1]}}""", "either")]
    [InlineData("""{"promotion":{"playoffPlaces":2}}""", "playoffs section")]
    [InlineData("""{"playoffs":{"teams":6,"bestOf":[1,1]}}""", "bestOf")]
    [InlineData("""{"secondPhase":{"groups":[{"name":"A","from":1,"to":6},{"name":"B","from":5,"to":8}]}}""", "non-overlapping")]
    [InlineData("""{"secondPhase":{"groups":[]}}""", "at least one group")]
    [InlineData("""{"registration":{"countries":["BE"]}}""", "alpha-3")]
    [InlineData("""{"regularSeason":{"startOffsetDays":200}}""", "startOffsetDays")]
    public void Invalid_format_rules_are_refused(string json, string message) =>
        Assert.Contains(message, Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse(json)).Message, StringComparison.Ordinal);

    [Fact]
    public void Play_in_and_groups_need_enough_clubs()
    {
        Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse("""{"playoffs":{"teams":8,"bestOf":[1,1,1],"playIn":true}}""").ValidateForClubs(9));
        CompetitionRules.Parse("""{"playoffs":{"teams":8,"bestOf":[1,1,1],"playIn":true}}""").ValidateForClubs(10);
        Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse("""{"secondPhase":{"groups":[{"name":"A","from":1,"to":12}]}}""").ValidateForClubs(10));
    }

    internal static Dictionary<ClubId, string> Entries(IEnumerable<ClubId> clubs) => clubs.ToDictionary(c => c, _ => "ITA");

    internal static List<SeasonGame> Schedule(IReadOnlyList<ClubId> clubs, CompetitionRules rules) =>
        [.. RoundRobinScheduler.Generate(clubs, rules.RegularSeason.Legs).Select(g => new SeasonGame(FixtureStage.RegularSeason, g.Round, 1, g.Home, g.Away))];

    private static ClubId Stronger(List<ClubId> clubs, ClubId a, ClubId b) => clubs.IndexOf(a) < clubs.IndexOf(b) ? a : b;

    private static void PlayAll(List<SeasonGame> games, List<ClubId> clubs, Func<ClubId, ClubId, ClubId>? winner = null)
    {
        winner ??= (a, b) => Stronger(clubs, a, b);
        for (var i = 0; i < games.Count; i++)
        {
            if (!games[i].IsPlayed)
            {
                var homeWins = winner(games[i].Home, games[i].Away) == games[i].Home;
                games[i] = games[i] with { HomeScore = homeWins ? 80 : 70, AwayScore = homeWins ? 70 : 80 };
            }
        }
    }

    /// <summary>Plays the season to the end: every game, then whatever the season asks to schedule next.</summary>
    private static CompetitionSeason Run(
        CompetitionRules rules,
        List<ClubId> clubs,
        List<SeasonGame>? games = null,
        Func<ClubId, ClubId, ClubId>? winner = null,
        Action<SeasonPhase>? onPhase = null)
    {
        games ??= Schedule(clubs, rules);
        for (var guard = 0; guard < 100; guard++)
        {
            var season = new CompetitionSeason(rules, Entries(clubs), games);
            onPhase?.Invoke(season.Phase);
            PlayAll(games, clubs, winner);
            season = new CompetitionSeason(rules, Entries(clubs), games);
            if (season.Phase == SeasonPhase.Completed)
            {
                onPhase?.Invoke(season.Phase);
                return season;
            }

            games.AddRange(season.NextGames().Select(r => new SeasonGame(r.Stage, r.Round, r.GameNumber, r.Home, r.Away)));
        }

        throw new InvalidOperationException("The season did not finish.");
    }
}
