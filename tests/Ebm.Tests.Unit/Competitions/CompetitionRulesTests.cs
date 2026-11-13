using Ebm.Domain.Common;
using Ebm.Domain.Competitions.Rules;

namespace Ebm.Tests.Unit.Competitions;

public sealed class CompetitionRulesTests
{
    public const string SerieA2 = """
        {"pointsForWin":2,"pointsForLoss":0,"registration":{"minTeams":6,"maxTeams":6},
         "regularSeason":{"legs":2,"daysBetweenRounds":7},"tieBreakers":["HeadToHeadWins","PointDifference"],
         "playoffs":{"teams":4,"fromPosition":2,"bestOf":[3,3]},"promotion":{"direct":1,"playoffWinner":true}}
        """;

    [Fact]
    public void Empty_rules_take_the_defaults()
    {
        var rules = CompetitionRules.Parse("{}");

        Assert.Equal(2, rules.PointsForWin);
        Assert.Equal(0, rules.PointsForLoss);
        Assert.Equal(2, rules.RegularSeason.Legs);
        Assert.Equal(CompetitionRules.DefaultTieBreakers, rules.TieBreakers);
        Assert.Null(rules.Playoffs);
        Assert.Equal(0, rules.Promotion.Places);
        Assert.Equal(0, rules.Relegation.Direct);
    }

    [Fact]
    public void Full_rules_are_read_and_written_back()
    {
        var rules = CompetitionRules.Parse(SerieA2);

        Assert.Equal(6, rules.Registration.MaxTeams);
        Assert.Equal([TieBreaker.HeadToHeadWins, TieBreaker.PointDifference], rules.TieBreakers);
        Assert.Equal(2, rules.Playoffs!.Rounds);
        Assert.Equal(5, rules.Playoffs.LastPosition);
        Assert.Equal(2, rules.Promotion.Places);

        var again = CompetitionRules.Parse(rules.ToJson());
        Assert.Equal(rules.Playoffs.BestOf, again.Playoffs!.BestOf);
        Assert.Equal(rules.Promotion, again.Promotion);
    }

    [Theory]
    [InlineData("""{"pointsForWin":1,"pointsForLoss":1}""", "greater than")]
    [InlineData("""{"pointsForWin":11}""", "pointsForWin")]
    [InlineData("""{"registration":{"minTeams":10,"maxTeams":8}}""", "maxTeams")]
    [InlineData("""{"regularSeason":{"legs":0}}""", "legs")]
    [InlineData("""{"tieBreakers":["Coin"]}""", "not valid")]
    [InlineData("""{"tieBreakers":["Wins","Wins"]}""", "repeat")]
    [InlineData("""{"playoffs":{"teams":33,"bestOf":[3,3,3]}}""", "playoffs.teams")]
    [InlineData("""{"playoffs":{"teams":8,"bestOf":[5,5]}}""", "one series length per round")]
    [InlineData("""{"playoffs":{"teams":2,"bestOf":[4]}}""", "odd")]
    [InlineData("""{"promotion":{"playoffWinner":true}}""", "need a playoffs section")]
    [InlineData("""{"playoffs":{"teams":2,"bestOf":[1]},"promotion":{"direct":1,"playoffWinner":true}}""", "fromPosition")]
    [InlineData("""{"unknown":1}""", "not valid")]
    [InlineData("""{"tieBreakers":null}""", "cannot be null")]
    public void Invalid_rules_are_refused_with_a_reason(string json, string reason)
    {
        var error = Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse(json));

        Assert.Contains(reason, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(5, "between 6 and 6")]
    [InlineData(7, "between 6 and 6")]
    public void Club_count_must_match_registration(int clubs, string reason)
    {
        var error = Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse(SerieA2).ValidateForClubs(clubs));

        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Places_must_fit_the_entered_clubs()
    {
        CompetitionRules.Parse(SerieA2).ValidateForClubs(6);
        Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse("""{"playoffs":{"teams":8,"bestOf":[1,1,1]}}""").ValidateForClubs(6));
        Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse("""{"promotion":{"direct":3},"relegation":{"direct":3}}""").ValidateForClubs(5));
        Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse(
            """{"playoffs":{"teams":4,"fromPosition":2,"bestOf":[1,1]},"promotion":{"direct":1,"playoffWinner":true},"relegation":{"direct":2}}""").ValidateForClubs(6));
    }
}
