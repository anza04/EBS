using Ebm.Domain.Rosters;
using Ebm.Domain.Transfers;

namespace Ebm.Tests.Unit.Transfers;

public sealed class TransferNegotiatorTests
{
    private static readonly TransferNegotiator Negotiator = TransferNegotiator.Default;

    private static ValuationContext Typical(int overall = 70, int age = 27, double yearsLeft = 2, RosterRole? role = RosterRole.Bench) =>
        new(overall, overall + 2, 50, age, yearsLeft, role);

    [Fact]
    public void Market_value_follows_rating_age_potential_and_remaining_contract()
    {
        var typical = Negotiator.MarketValue(Typical());
        Assert.Equal(0, typical % 10_000);
        Assert.InRange(typical, 500_000, 1_500_000); // about 2.5 × the market salary of a 70-rated player, ×1.1 for his age

        Assert.True(Negotiator.MarketValue(Typical(overall: 80)) > typical);
        Assert.True(Negotiator.MarketValue(Typical(age: 34)) < typical);
        Assert.True(Negotiator.MarketValue(Typical(age: 21)) > typical);
        Assert.True(Negotiator.MarketValue(Typical(yearsLeft: 0.5)) < Negotiator.MarketValue(Typical(yearsLeft: 1)));
        Assert.Equal(Negotiator.MarketValue(Typical(yearsLeft: 2)), Negotiator.MarketValue(Typical(yearsLeft: 4))); // capped at two years
        Assert.True(Negotiator.MarketValue(Typical() with { Potential = 90 }) > typical);
        Assert.Equal(TransferNegotiator.MinimumValue, Negotiator.MarketValue(new ValuationContext(1, 1, 1, 40, 0)));
    }

    [Fact]
    public void Clubs_ask_more_for_important_players_and_less_when_they_need_money()
    {
        var bench = Negotiator.Value(Typical());
        var starter = Negotiator.Value(Typical(role: RosterRole.Starter));
        var rotation = Negotiator.Value(Typical(role: RosterRole.Rotation));
        var inactive = Negotiator.Value(Typical(role: RosterRole.Inactive));
        var needy = Negotiator.Value(Typical() with { ClubInDebt = true, ClubOverWageBudget = true });

        Assert.Equal(bench.MarketValue, bench.AskingFee);
        Assert.Equal(bench.MarketValue, starter.MarketValue);
        Assert.True(starter.AskingFee > rotation.AskingFee && rotation.AskingFee > bench.AskingFee && bench.AskingFee > inactive.AskingFee);
        Assert.True(needy.AskingFee < bench.AskingFee);
        Assert.True(bench.AvailableForLoan && inactive.AvailableForLoan);
        Assert.False(starter.AvailableForLoan || rotation.AvailableForLoan);
        Assert.Equal(bench.AskingFee, bench.BuyNowFee);
    }

    [Fact]
    public void A_fee_is_accepted_countered_or_rejected()
    {
        var valuation = Negotiator.Value(Typical());
        var asking = valuation.AskingFee;

        Assert.Equal(TransferOutcome.Accepted, Negotiator.EvaluateFee(valuation, asking).Outcome);
        Assert.Equal(TransferOutcome.Accepted, Negotiator.EvaluateFee(valuation, asking * 2).Outcome);
        var counter = Negotiator.EvaluateFee(valuation, (long)(asking * 0.85));
        Assert.Equal((TransferOutcome.CounterOffer, (long?)asking, asking), (counter.Outcome, counter.CounterFee, counter.AskingFee));
        var rejected = Negotiator.EvaluateFee(valuation, asking / 2);
        Assert.Equal((TransferOutcome.Rejected, (long?)null, false), (rejected.Outcome, rejected.CounterFee, rejected.IsAccepted));
    }

    [Fact]
    public void A_fee_that_meets_the_release_clause_cannot_be_refused()
    {
        var valuation = Negotiator.Value(Typical(role: RosterRole.Starter) with { ReleaseClause = 100_000 });
        Assert.True(valuation.AskingFee > 100_000);
        Assert.Equal(100_000, valuation.BuyNowFee);

        var answer = Negotiator.EvaluateFee(valuation, 100_000);
        Assert.True(answer.IsAccepted);
        Assert.Contains("release clause", answer.Message, StringComparison.Ordinal);
        Assert.Equal(TransferOutcome.Rejected, Negotiator.EvaluateFee(valuation, 99_999).Outcome);

        var high = Negotiator.Value(Typical() with { ReleaseClause = 50_000_000 });
        Assert.Equal(high.AskingFee, high.BuyNowFee); // a clause above the asking fee changes nothing
    }

    [Fact]
    public void Loan_fees_scale_with_the_length_and_starters_are_not_loaned()
    {
        var bench = Negotiator.Value(Typical());
        var oneYear = Negotiator.EvaluateLoan(bench, bench.LoanFeePerYear, 1.0);
        Assert.True(oneYear.IsAccepted);
        var halfYear = Negotiator.EvaluateLoan(bench, 0, 0.5);
        Assert.Equal(TransferOutcome.Rejected, halfYear.Outcome);
        Assert.InRange(halfYear.AskingFee, bench.LoanFeePerYear / 2 - 1_000, bench.LoanFeePerYear / 2 + 1_000);
        Assert.Equal(TransferOutcome.CounterOffer, Negotiator.EvaluateLoan(bench, (long)(bench.LoanFeePerYear * 0.9), 1.0).Outcome);

        var starter = Negotiator.Value(Typical(role: RosterRole.Starter));
        var refused = Negotiator.EvaluateLoan(starter, 100_000_000, 1.0);
        Assert.Equal(TransferOutcome.Rejected, refused.Outcome);
        Assert.Contains("starting five", refused.Message, StringComparison.Ordinal);

        Assert.Equal(1.0, TransferNegotiator.LoanYears(new DateOnly(2026, 7, 1), new DateOnly(2027, 6, 30)), 2);
    }
}
