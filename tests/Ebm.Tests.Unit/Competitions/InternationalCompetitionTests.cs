using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Competitions.Engine;
using Ebm.Domain.Competitions.Rules;

namespace Ebm.Tests.Unit.Competitions;

/// <summary>Step 18: international entry rules, qualification, rewards and the league-phase format.</summary>
public sealed class InternationalCompetitionTests
{
    private const string EuroLeague = """
        {"registration":{"minTeams":10,"maxTeams":10},"cup":{"draw":"Bracket","entry":{"tier":1,"slots":[{"type":"Holder"},{"type":"League","countries":["ESP"],"clubs":2},{"type":"Reputation","clubs":0}]},
         "groupStage":{"groups":1,"legs":2,"advance":4},
         "rounds":[{"name":"Semi-finals","neutral":true},{"name":"Final","neutral":true}],
         "rewards":{"entry":1000,"win":100,"knockout":500,"winner":5000,"runnerUp":2000,"reputationKnockout":1,"reputationWinner":3,"reputationRunnerUp":2}}}
        """;

    [Fact]
    public void A_single_group_league_phase_of_twenty_clubs_is_valid_and_sends_its_top_eight_on()
    {
        var rules = CompetitionRules.Parse("""{"registration":{"maxTeams":20},"cup":{"draw":"Bracket","groupStage":{"groups":1,"legs":2,"advance":8},"rounds":[{"name":"QF","legs":2},{"name":"SF","neutral":true},{"name":"F","neutral":true}]}}""");
        var seeds = RoundRobinSchedulerTests.Clubs(20);
        rules.ValidateForClubs(20);

        var games = new CupSeason(rules, seeds, [], 1).NextGames();

        Assert.Equal(20 * 19, games.Count);
        Assert.Equal(38, games.Max(g => g.Round));
        Assert.True(rules.Cup!.Rounds[1].Neutral);
    }

    [Theory]
    [InlineData("""{"cup":{"rounds":[{"name":"F","legs":2,"neutral":true}]}}""")]
    [InlineData("""{"cup":{"entry":{"slots":[{"type":"Holder"}]},"qualification":{"positions":2,"afterRound":3},"rounds":[{"name":"F"}]}}""")]
    [InlineData("""{"cup":{"entry":{"slots":[]},"rounds":[{"name":"F"}]}}""")]
    [InlineData("""{"cup":{"entry":{"slots":[{"type":"League"}]},"rounds":[{"name":"F"}]}}""")]
    [InlineData("""{"cup":{"entry":{"slots":[{"type":"Winner"}]},"rounds":[{"name":"F"}]}}""")]
    [InlineData("""{"cup":{"entry":{"tier":0,"slots":[{"type":"Holder"}]},"rounds":[{"name":"F"}]}}""")]
    [InlineData("""{"cup":{"rewards":{"winner":-1},"rounds":[{"name":"F"}]}}""")]
    [InlineData("""{"cup":{"rewards":{"reputationWinner":11},"rounds":[{"name":"F"}]}}""")]
    public void Invalid_international_rules_are_refused(string json) =>
        Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse(json));

    [Fact]
    public void Qualification_fills_tiers_in_order_and_a_club_plays_one_competition_only()
    {
        var spain = Clubs("ESP", 4, 80);
        var italy = Clubs("ITA", 4, 70);
        var holder = italy[3].Club;
        var wildcard = italy[2].Club;
        var top = Competition("EL", 1, 5, [new CupEntrySlot { Type = CupSlotType.Holder }, new CupEntrySlot { Type = CupSlotType.League, Countries = ["ESP"], Clubs = 2 }, new CupEntrySlot { Type = CupSlotType.Reputation, Clubs = 0 }]);
        var second = Competition("EC", 2, 4, [new CupEntrySlot { Type = CupSlotType.League, Countries = ["ESP", "ITA"], Clubs = 1 }, new CupEntrySlot { Type = CupSlotType.Reputation }], entered: [wildcard]);
        var sources = Sources(spain, italy, ("EL", holder));

        var selection = InternationalQualification.Select([second, top], sources);

        // EL: holder, Spain's top two, then the best remaining reputation; EC: Italy's best left (Spain has none), then
        // reputation, skipping its wildcard and the holder.
        Assert.Equal([holder, spain[0].Club, spain[1].Club, spain[2].Club, spain[3].Club], selection[top.Id]);
        Assert.Equal([italy[0].Club, italy[1].Club], selection[second.Id]);
        Assert.Empty(selection[top.Id].Intersect(selection[second.Id]));
    }

    [Fact]
    public void Qualification_respects_the_registration_countries_and_skips_missing_winners()
    {
        var spain = Clubs("ESP", 3, 80);
        var italy = Clubs("ITA", 3, 70);
        var italian = Competition("IT", 1, 3, [new CupEntrySlot { Type = CupSlotType.Winner, Competition = "Nobody" }, new CupEntrySlot { Type = CupSlotType.Reputation, Clubs = 0 }], countries: ["ITA"]);

        var selection = InternationalQualification.Select([italian], Sources(spain, italy));

        Assert.Equal(italy.Select(c => c.Club), selection[italian.Id]);
    }

    [Fact]
    public void Rewards_pay_entry_wins_knockout_and_the_finalists()
    {
        var rules = CompetitionRules.Parse(EuroLeague);
        var seeds = RoundRobinSchedulerTests.Clubs(10);
        var cup = Run(rules, seeds);

        Assert.Equal((seeds[0], seeds[1]), (cup.Winner, cup.RunnerUp));
        // The best seed wins all 18 group games, the semi-final and the final.
        Assert.Equal(new CupReward(1000 + (20 * 100) + 500 + 5000, 1 + 3), cup.Reward(seeds[0]));
        Assert.Equal(new CupReward(1000 + (17 * 100) + 500 + 2000, 1 + 2), cup.Reward(seeds[1]));
        Assert.Equal(new CupReward(1000, 0), cup.Reward(seeds[9]));
        Assert.Equal(CupReward.None, cup.Reward(ClubId.New()));
        Assert.Throws<DomainValidationException>(() => new CupSeason(rules, seeds, [], 1).Reward(seeds[0]));
    }

    [Fact]
    public void Club_reputation_changes_stay_within_the_limits_and_rewards_are_paid_once()
    {
        var club = new Club(ClubId.New(), "Test", "TST", Ebm.Domain.Countries.CountryId.New());
        club.ChangeReputation(500);
        Assert.Equal(Club.MaxReputation, club.Reputation);
        club.ChangeReputation(-500);
        Assert.Equal(Club.MinReputation, club.Reputation);

        var entry = new CompetitionParticipant(CompetitionParticipantId.New(), CompetitionId.New(), Ebm.Domain.Seasons.SeasonId.New(), club.Id);
        entry.RecordReward(1000, 2);
        Assert.Equal((1000L, 2), (entry.PrizeMoney, entry.ReputationGain));
        Assert.Throws<DomainValidationException>(() => entry.RecordReward(1, 0));
    }

    private static List<QualifyingClub> Clubs(string country, int count, int bestReputation) =>
        [.. Enumerable.Range(0, count).Select(i => new QualifyingClub(ClubId.New(), country, 1, bestReputation - i))];

    private static EntryCompetition Competition(string shortName, int tier, int teams, IReadOnlyList<CupEntrySlot> slots, IReadOnlyList<ClubId>? entered = null, IReadOnlyList<string>? countries = null) =>
        new(CompetitionId.New(), shortName, new CupEntry { Tier = tier, Slots = slots }, teams, entered ?? [], countries ?? []);

    /// <summary>Rankings follow reputation (as the last tables would); winners by short name.</summary>
    private static QualificationSources Sources(List<QualifyingClub> a, List<QualifyingClub> b, params (string Competition, ClubId Club)[] winners) =>
        new(
            new Dictionary<(string, int), IReadOnlyList<ClubId>> { [(a[0].Country, 1)] = [.. a.Select(c => c.Club)], [(b[0].Country, 1)] = [.. b.Select(c => c.Club)] },
            winners.ToDictionary(w => w.Competition, w => w.Club),
            [.. a, .. b]);

    /// <summary>Plays the cup to the end; the better seed wins every game by 10.</summary>
    private static CupSeason Run(CompetitionRules rules, IReadOnlyList<ClubId> seeds)
    {
        var games = new List<SeasonGame>();
        for (var guard = 0; guard < 100; guard++)
        {
            var cup = new CupSeason(rules, seeds, games, 1);
            var next = cup.NextGames();
            if (next.Count == 0)
            {
                return cup;
            }

            foreach (var g in next)
            {
                var homeBetter = seeds.ToList().IndexOf(g.Home) < seeds.ToList().IndexOf(g.Away);
                games.Add(new SeasonGame(g.Stage, g.Round, g.GameNumber, g.Home, g.Away, homeBetter ? 90 : 80, homeBetter ? 80 : 90));
            }
        }

        throw new InvalidOperationException("The cup never ended.");
    }
}
