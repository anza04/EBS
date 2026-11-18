using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;

namespace Ebm.Tests.Unit.Contracts;

/// <summary>Step 25: demands, promises, length preferences, clauses, competing offers, agents and rounds (docs/CONTRACTS.md §4).</summary>
public sealed class AdvancedNegotiationTests
{
    private static readonly DateOnly Start = new(2026, 7, 1);
    private static readonly ContractNegotiator N = ContractNegotiator.Default;

    // Overall 60, potential 60, reputation 50, age 26, average club: asking € 150,000, no demands.
    private static readonly NegotiationContext Neutral = new(60, 60, 50, 26, 50);

    private static ContractTerms Offer(long salary, int years = 2) => new(Start, Start.AddYears(years).AddDays(-1), salary);

    [Fact]
    public void A_neutral_context_keeps_the_market_salary_and_makes_no_demands()
    {
        var d = N.Demands(Neutral);
        Assert.Equal(N.AskingSalary(Neutral), d.AskingSalary);
        Assert.Equal(d.AskingSalary, d.MarketSalary);
        Assert.Equal(d.AskingSalary, d.MinimumValue);
        Assert.Equal((1, 5), (d.MinYears, d.MaxYears));
        Assert.Null(d.RequiredRole);
        Assert.Null(d.MaxReleaseClause);
        Assert.Equal(4, d.Patience);
        Assert.True(N.Evaluate(Neutral, Offer(d.AskingSalary)).IsAccepted);
    }

    [Fact]
    public void Competing_offers_a_trusted_agent_and_broken_promises_move_the_asking_value()
    {
        var market = N.AskingSalary(Neutral);
        Assert.Equal(Thousands(market * 1.12), N.Demands(Neutral with { CompetingOffers = 5 }).AskingSalary); // at most 3 × 4%
        Assert.Equal(Thousands(market * 0.95), N.Demands(Neutral with { Relationship = 100 }).AskingSalary);
        Assert.Equal(Thousands(market * 1.05), N.Demands(Neutral with { Relationship = -100 }).AskingSalary);
        Assert.Equal(Thousands(market * 1.10), N.Demands(Neutral with { BrokenPromise = true }).AskingSalary);
    }

    [Fact]
    public void Later_rounds_bring_small_concessions_up_to_three_percent()
    {
        var asking = N.Demands(Neutral).AskingSalary;
        Assert.Equal((long)Math.Ceiling(asking * 0.99), N.Demands(Neutral with { Round = 2 }).MinimumValue);
        Assert.Equal((long)Math.Ceiling(asking * 0.97), N.Demands(Neutral with { Round = 9 }).MinimumValue);
        Assert.True(N.Evaluate(Neutral with { Round = 4 }, Offer(asking * 97 / 100 + 1000)).IsAccepted);
        Assert.False(N.Evaluate(Neutral, Offer(asking * 97 / 100 + 1000)).IsAccepted);
    }

    [Fact]
    public void Tough_agents_are_less_patient_and_reject_more_offers_outright()
    {
        var soft = N.Demands(Neutral with { AgentToughness = 1 });
        var tough = N.Demands(Neutral with { AgentToughness = 100 });
        Assert.True(tough.RejectBelow > soft.RejectBelow);
        Assert.Equal(3, tough.Patience);
        Assert.Equal(5, N.Demands(Neutral with { Relationship = 40 }).Patience);
        Assert.Equal(3, N.Demands(Neutral with { Relationship = -40 }).Patience);

        var offer = Offer(soft.AskingSalary * 80 / 100);
        Assert.Equal(NegotiationOutcome.CounterOffer, N.Evaluate(Neutral with { AgentToughness = 1 }, offer).Outcome);
        Assert.Equal(NegotiationOutcome.Rejected, N.Evaluate(Neutral with { AgentToughness = 100 }, offer).Outcome);
    }

    [Fact]
    public void Young_improving_players_want_short_deals_and_veterans_want_security()
    {
        var young = Neutral with { Age = 21, Potential = 80 };
        var veteran = Neutral with { Age = 32 };
        Assert.Equal((1, 3), ContractNegotiator.PreferredYears(young));
        Assert.Equal((2, 5), ContractNegotiator.PreferredYears(veteran));

        var d = N.Demands(young);
        var five = Offer(d.AskingSalary, years: 5);
        Assert.Equal(N.OfferValue(five) - (long)Math.Round(0.14 * d.AskingSalary), N.OfferValue(five, d)); // two years too long
        var counter = N.Evaluate(young, five);
        Assert.Equal(NegotiationOutcome.CounterOffer, counter.Outcome);
        Assert.Equal(Start.AddYears(3).AddDays(-1), counter.CounterOffer!.EndDate);
        Assert.Equal(d.AskingSalary, counter.CounterOffer.AnnualSalary);

        var oneYear = N.Evaluate(veteran, Offer(N.Demands(veteran).AskingSalary, years: 1));
        Assert.Equal(Start.AddYears(2).AddDays(-1), oneYear.CounterOffer!.EndDate);
        Assert.True(N.Evaluate(veteran, oneYear.CounterOffer).IsAccepted);
    }

    [Fact]
    public void Promises_above_the_expected_role_are_worth_money_up_to_a_cap()
    {
        var context = Neutral with { ExpectedRole = SquadRole.Rotation };
        var d = N.Demands(context);
        var plain = Offer(d.AskingSalary);
        Assert.Equal(N.OfferValue(plain), N.OfferValue(plain, d));
        Assert.Equal(N.OfferValue(plain) + (long)Math.Round(0.08 * d.AskingSalary), N.OfferValue(plain with { Promises = new(SquadRole.Star) }, d));
        Assert.Equal(N.OfferValue(plain) + (long)Math.Round(0.10 * d.AskingSalary), N.OfferValue(plain with { Promises = new(SquadRole.Star, 40) }, d));
        Assert.Equal(N.OfferValue(plain), N.OfferValue(plain with { Promises = new(SquadRole.Reserve) }, d));

        // Without a known squad role a promise means nothing to him.
        Assert.Equal(N.OfferValue(plain), N.OfferValue(plain with { Promises = new(SquadRole.Star) }, N.Demands(Neutral)));
    }

    [Fact]
    public void Stars_insist_on_their_role_and_the_counter_offer_promises_it()
    {
        var star = Neutral with { Reputation = 70, ClubReputation = 60, ExpectedRole = SquadRole.Star };
        var d = N.Demands(star);
        Assert.Equal(SquadRole.Star, d.RequiredRole);

        var response = N.Evaluate(star, Offer(d.AskingSalary * 2));
        Assert.Equal(NegotiationOutcome.CounterOffer, response.Outcome);
        Assert.Contains("star role", response.Message, StringComparison.Ordinal);
        Assert.Equal(SquadRole.Star, response.CounterOffer!.Promises!.Role);
        Assert.Equal(d.AskingSalary * 2, response.CounterOffer.AnnualSalary);
        Assert.True(N.Evaluate(star, response.CounterOffer).IsAccepted);
        Assert.True(N.Evaluate(star, Offer(d.AskingSalary) with { Promises = new(MinutesPerGame: 30) }).IsAccepted);
    }

    [Fact]
    public void A_player_far_above_the_club_insists_on_a_release_clause()
    {
        var context = Neutral with { Reputation = 80, ClubReputation = 40 };
        var d = N.Demands(context);
        Assert.Equal((long)Math.Ceiling(20.0 * d.AskingSalary / 1000) * 1000, d.MaxReleaseClause);
        Assert.Null(N.Demands(context with { Age = 31 }).MaxReleaseClause);

        var offer = Offer(d.AskingSalary);
        var response = N.Evaluate(context, offer);
        Assert.Equal(NegotiationOutcome.CounterOffer, response.Outcome);
        Assert.Equal(d.MaxReleaseClause, response.CounterOffer!.ReleaseClause);
        Assert.True(N.Evaluate(context, offer with { ReleaseClause = d.AskingSalary * 5 }).IsAccepted);
        Assert.Equal(N.OfferValue(offer) + (long)Math.Round(0.04 * d.AskingSalary), N.OfferValue(offer with { ReleaseClause = d.AskingSalary * 5 }, d));
    }

    [Fact]
    public void Far_below_an_unmet_demand_the_player_rejects_and_says_what_he_wants()
    {
        var context = Neutral with { Reputation = 80, ClubReputation = 40 };
        var response = N.Evaluate(context, Offer(20_000));
        Assert.Equal(NegotiationOutcome.Rejected, response.Outcome);
        Assert.Contains("release clause", response.Message, StringComparison.Ordinal);
        Assert.NotNull(response.Demands);
    }

    [Fact]
    public void Talks_cost_patience_and_break_off_for_thirty_days()
    {
        var talks = new ContractNegotiation(ContractNegotiationId.New(), PlayerId.New(), ClubId.New(), null, Start, 4);
        Assert.False(talks.RecordRound(Start, NegotiationOutcome.CounterOffer));
        Assert.False(talks.RecordRound(Start, NegotiationOutcome.Accepted));
        Assert.Equal(3, talks.Patience);
        Assert.True(talks.IsOpenOn(Start.AddDays(ContractNegotiation.IdleDays)));
        Assert.False(talks.IsOpenOn(Start.AddDays(ContractNegotiation.IdleDays + 1)));

        Assert.False(talks.RecordRound(Start, NegotiationOutcome.Rejected));
        Assert.True(talks.RecordRound(Start, NegotiationOutcome.CounterOffer));
        Assert.Equal(NegotiationStatus.BrokenOff, talks.Status);
        Assert.Equal(4, talks.Rounds);
        Assert.Equal(Start.AddDays(30), talks.BrokenOffUntil);
        Assert.True(talks.RefusesTalksOn(Start.AddDays(29)));
        Assert.False(talks.RefusesTalksOn(Start.AddDays(30)));
        Assert.Throws<DomainValidationException>(() => talks.RecordRound(Start, NegotiationOutcome.Accepted));
    }

    [Theory]
    [InlineData(1, 30, SquadRole.Star)]
    [InlineData(4, 20, SquadRole.Starter)]
    [InlineData(7, 21, SquadRole.Prospect)]
    [InlineData(7, 25, SquadRole.Rotation)]
    [InlineData(12, 25, SquadRole.Reserve)]
    public void The_expected_role_follows_the_rank_in_the_squad(int rank, int age, SquadRole role) =>
        Assert.Equal(role, PromiseRules.Expected(rank, age));

    [Theory]
    [InlineData(25, 22, PromiseStatus.Kept)]
    [InlineData(25, 18, PromiseStatus.AtRisk)]
    [InlineData(25, 14, PromiseStatus.Broken)]
    public void Promises_are_judged_on_the_minutes_played(int target, double minutes, PromiseStatus status) =>
        Assert.Equal(status, PromiseRules.Assess(target, minutes));

    [Fact]
    public void A_contract_records_its_promises_and_their_reviews()
    {
        var terms = Offer(100_000) with { Promises = new(SquadRole.Starter, 28) };
        var contract = Contract.ForPlayer(ContractId.New(), ClubId.New(), PlayerId.New(), terms);
        Assert.Equal(new ContractPromises(SquadRole.Starter, 28), contract.Terms.Promises);
        Assert.Equal(28, contract.Promises!.TargetMinutes);
        Assert.False(contract.PromiseReviewDue(Start.AddDays(27)));
        Assert.True(contract.PromiseReviewDue(Start.AddDays(28)));

        Assert.False(contract.RecordPromiseReview(Start.AddDays(28), PromiseStatus.AtRisk));
        Assert.True(contract.RecordPromiseReview(Start.AddDays(56), PromiseStatus.Broken));
        Assert.False(contract.RecordPromiseReview(Start.AddDays(84), PromiseStatus.Broken)); // already broken
        Assert.False(contract.PromiseReviewDue(Start.AddDays(111)));

        Assert.Throws<DomainValidationException>(() => Contract.ForPlayer(ContractId.New(), ClubId.New(), PlayerId.New(), terms with { Promises = new(MinutesPerGame: 41) }));
        Assert.Throws<DomainValidationException>(() => Contract.ForPlayer(ContractId.New(), ClubId.New(), PlayerId.New(), Offer(1)).RecordPromiseReview(Start, PromiseStatus.Kept));
    }

    [Fact]
    public void The_agent_pool_has_unique_agents_and_stable_clients()
    {
        var pool = AgentPool.Create();
        Assert.Equal(24, pool.Count);
        Assert.Equal(24, pool.Select(a => a.Name).Distinct().Count());
        Assert.All(pool, a => Assert.InRange(a.Toughness, 30, 99));

        var player = PlayerId.New();
        var agent = AgentPool.For(player, 50, pool);
        Assert.NotNull(agent);
        Assert.Same(agent, AgentPool.For(player, 90, [.. pool.Reverse()]));
        Assert.Null(AgentPool.For(player, AgentPool.MinClientReputation - 1, pool));

        var relationship = new AgentRelationship(AgentRelationshipId.New(), agent!.Id, ClubId.New());
        relationship.Adjust(-150);
        Assert.Equal(AgentRelationship.Min, relationship.Score);
    }

    private static long Thousands(double value) => (long)Math.Round(value / 1000, MidpointRounding.AwayFromZero) * 1000;
}
