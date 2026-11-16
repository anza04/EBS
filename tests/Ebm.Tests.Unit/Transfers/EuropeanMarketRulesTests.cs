using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions.Rules;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Domain.Transfers;

namespace Ebm.Tests.Unit.Transfers;

/// <summary>Step 19: registration deadlines, club attractiveness, buyouts, loan clauses and AI upgrade signings.</summary>
public sealed class EuropeanMarketRulesTests
{
    private static readonly DateOnly SeasonStart = new(2026, 9, 1);
    private static readonly DateOnly SeasonEnd = new(2027, 6, 30);

    private static (string, RegistrationRules) Competition(string name, string? deadline) => (name, new RegistrationRules { Deadline = deadline });

    [Fact]
    public void A_deadline_falls_inside_the_season_even_across_new_year()
    {
        Assert.Equal(new DateOnly(2027, 2, 28), new RegistrationRules { Deadline = "02-28" }.DeadlineIn(SeasonStart, SeasonEnd));
        Assert.Equal(new DateOnly(2026, 10, 15), new RegistrationRules { Deadline = "10-15" }.DeadlineIn(SeasonStart, SeasonEnd));
        Assert.Equal(new DateOnly(2027, 2, 28), new RegistrationRules { Deadline = "02-29" }.DeadlineIn(SeasonStart, SeasonEnd)); // not a leap year
        Assert.Null(new RegistrationRules { Deadline = "08-01" }.DeadlineIn(SeasonStart, SeasonEnd)); // outside the season
        Assert.Null(new RegistrationRules().DeadlineIn(SeasonStart, SeasonEnd));
    }

    [Theory]
    [InlineData("2-28")]
    [InlineData("13-01")]
    [InlineData("02-30")]
    [InlineData("28/02")]
    public void Invalid_deadlines_are_refused(string deadline) =>
        Assert.Throws<DomainValidationException>(() => CompetitionRules.Parse("{\"registration\":{\"deadline\":\"" + deadline + "\"}}"));

    [Fact]
    public void Registrations_close_after_the_earliest_deadline_until_the_last_day_of_the_season()
    {
        var competitions = new[] { Competition("League", "03-31"), Competition("EuroCup", "02-28"), Competition("Cup", null) };

        var before = RegistrationWindow.On(new DateOnly(2027, 2, 28), SeasonStart, SeasonEnd, competitions);
        var after = RegistrationWindow.On(new DateOnly(2027, 3, 1), SeasonStart, SeasonEnd, competitions);
        var lastDay = RegistrationWindow.On(SeasonEnd, SeasonStart, SeasonEnd, competitions);

        Assert.True(before.IsOpen);
        Assert.Equal(("EuroCup", (DateOnly?)new DateOnly(2027, 2, 28)), (before.Competition, before.Deadline));
        Assert.False(after.IsOpen);
        Assert.Equal(SeasonEnd, after.ReopensOn);
        Assert.Contains("EuroCup", after.Message, StringComparison.Ordinal);
        Assert.True(lastDay.IsOpen);
        Assert.True(RegistrationWindow.On(new DateOnly(2027, 4, 1), SeasonStart, SeasonEnd, [Competition("Cup", null)]).IsOpen);
    }

    [Fact]
    public void A_club_short_of_players_may_still_register_up_to_its_minimum()
    {
        var closed = RegistrationWindow.On(new DateOnly(2027, 3, 1), SeasonStart, SeasonEnd, [Competition("League", "02-28")]);

        Assert.True(closed.Allows(firstTeamAfterSigning: 10, minPlayers: 10));
        Assert.False(closed.Allows(firstTeamAfterSigning: 11, minPlayers: 10));
        Assert.True(RegistrationWindow.Open.Allows(15, 10));
    }

    [Fact]
    public void International_competitions_make_a_club_more_attractive()
    {
        Assert.Equal(70, ClubAttractiveness.Of(60, 1));
        Assert.Equal(66, ClubAttractiveness.Of(60, 2));
        Assert.Equal(60, ClubAttractiveness.Of(60, null));
        Assert.Equal(100, ClubAttractiveness.Of(95, 1));

        var negotiator = ContractNegotiator.Default;
        var player = new NegotiationContext(75, 78, 60, 27, ClubAttractiveness.Of(60, null));
        Assert.True(negotiator.AskingSalary(player with { ClubReputation = ClubAttractiveness.Of(60, 1) }) < negotiator.AskingSalary(player));
    }

    [Fact]
    public void A_buyout_costs_more_the_more_the_player_would_lose_elsewhere()
    {
        var negotiator = ContractNegotiator.Default;
        var player = new NegotiationContext(70, 70, 50, 28, TransferNegotiator.NeutralClubReputation);
        var market = negotiator.AskingSalary(player);

        Assert.Equal(negotiator.MinBuyoutShare, negotiator.BuyoutShare(player, market));
        Assert.Equal(negotiator.MinBuyoutShare, negotiator.BuyoutShare(player, market / 2)); // underpaid: he leaves easily
        Assert.Equal(0.3 + 0.5, negotiator.BuyoutShare(player, market * 2), 3); // he would earn half elsewhere
        Assert.Equal(negotiator.MaxBuyoutShare, negotiator.BuyoutShare(player, market * 10));
        Assert.Equal(300_000, negotiator.MinimumBuyout(player, market, 1_000_000));
        Assert.Equal(1_000, negotiator.MinimumBuyout(player, market, 1_000)); // never more than is owed
    }

    [Fact]
    public void A_parent_club_shares_more_salary_for_young_players_and_sells_an_option_at_its_price()
    {
        var negotiator = TransferNegotiator.Default;
        var valuation = new PlayerValuation(500_000, 600_000, null, true, 50_000);

        Assert.True(negotiator.EvaluateLoan(valuation, 50_000, 1, new LoanClauses(21, 50, 600_000)).IsAccepted);
        Assert.Equal(TransferOutcome.Rejected, negotiator.EvaluateLoan(valuation, 50_000, 1, new LoanClauses(21, 51)).Outcome);
        Assert.Equal(TransferOutcome.Rejected, negotiator.EvaluateLoan(valuation, 50_000, 1, new LoanClauses(27, 30)).Outcome);
        Assert.True(negotiator.EvaluateLoan(valuation, 50_000, 1, new LoanClauses(27, 25)).IsAccepted);
        var option = negotiator.EvaluateLoan(valuation, 50_000, 1, new LoanClauses(27, 0, 599_999));
        Assert.Equal(TransferOutcome.Rejected, option.Outcome);
        Assert.Contains("600,000", option.Message.Replace('.', ','), StringComparison.Ordinal);
        Assert.Equal(400_000, TransferNegotiator.SalaryContribution(800_000, 50, 1));
        Assert.Equal(100_000, TransferNegotiator.SalaryContribution(800_000, 25, 0.5));
    }

    [Fact]
    public void A_rich_club_buys_one_player_clearly_better_than_its_starting_five()
    {
        var team = Enum.GetValues<Position>().SelectMany(p => new[] { Member(p, 65, RosterRole.Starter), Member(p, 55) }).Concat([Member(Position.Center, 50), Member(Position.Center, 50), Member(Position.Center, 50)]).ToList();
        var situation = new ClubSituation(ClubId.New(), 5_000_000, 5_000_000, team.Sum(m => m.Salary), team, SquadRules.Default);
        var star = Unlisted(Position.PointGuard, 72, 1_500_000);
        var tooExpensive = Unlisted(Position.Center, 80, 3_000_000);
        var notBetter = Unlisted(Position.Center, 68, 100_000);

        var plan = ClubRecruiter.Default.Plan(situation, [star, tooExpensive, notBetter]);

        var signing = Assert.Single(plan.Signings);
        Assert.Equal(star.PlayerId, signing.Candidate.PlayerId);
        Assert.StartsWith("upgrade", signing.Reason, StringComparison.Ordinal);
        Assert.Empty(new ClubRecruiter { MaxUpgrades = 0 }.Plan(situation, [star]).Signings);
    }

    [Fact]
    public void Unlisted_players_never_fill_ordinary_roster_needs()
    {
        var team = Enum.GetValues<Position>().SelectMany(p => new[] { Member(p, 65, RosterRole.Starter), Member(p, 55) }).ToList();
        var situation = new ClubSituation(ClubId.New(), 5_000_000, 5_000_000, team.Sum(m => m.Salary), team, SquadRules.Default);

        var plan = new ClubRecruiter { MaxUpgrades = 0 }.Plan(situation, [Unlisted(Position.Center, 60, 10_000)]);

        Assert.Empty(plan.Signings);
        Assert.Equal(3, plan.Unmet.Count);
    }

    private static SquadMember Member(Position position, int overall, RosterRole role = RosterRole.Bench) =>
        new(PlayerId.New(), $"{position} {overall}", position, PositionSet.Empty, overall, 27, 100_000, role, false);

    private static Candidate Unlisted(Position position, int overall, long fee) =>
        new(PlayerId.New(), $"Unlisted {position} {overall}", position, PositionSet.Empty, overall, 27, 100_000, fee, ClubId.New(), false, Listed: false);
}
