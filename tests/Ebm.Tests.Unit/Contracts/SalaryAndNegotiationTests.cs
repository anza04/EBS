using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;

namespace Ebm.Tests.Unit.Contracts;

public sealed class SalaryCalculatorTests
{
    private static Contract Make(DateOnly start, DateOnly end, long salary, PaymentFrequency frequency = PaymentFrequency.Monthly) =>
        Contract.ForPlayer(ContractId.New(), ClubId.New(), PlayerId.New(), new ContractTerms(start, end, salary, frequency));

    [Fact]
    public void A_full_contract_year_costs_exactly_the_annual_salary_even_in_a_leap_year()
    {
        var c = Make(new DateOnly(2027, 7, 1), new DateOnly(2029, 6, 30), 365_000); // second year contains 29 Feb 2028
        Assert.Equal(365_000, SalaryCalculator.SalaryBetween(c, new DateOnly(2027, 7, 1), new DateOnly(2028, 6, 30)));
        Assert.Equal(730_000, SalaryCalculator.TotalSalary(c));
        Assert.Equal(1_000, SalaryCalculator.SalaryBetween(c, new DateOnly(2028, 7, 1), new DateOnly(2028, 7, 1))); // 365-day year
        Assert.Equal(997, SalaryCalculator.SalaryBetween(c, new DateOnly(2027, 7, 1), new DateOnly(2027, 7, 1))); // 366-day year
        Assert.Equal(0, SalaryCalculator.SalaryBetween(c, new DateOnly(2020, 1, 1), new DateOnly(2027, 6, 30)));
    }

    [Theory]
    [InlineData(PaymentFrequency.Monthly, 24)]
    [InlineData(PaymentFrequency.Quarterly, 8)]
    [InlineData(PaymentFrequency.Annually, 2)]
    public void Instalments_follow_the_frequency_and_add_up_to_the_total(PaymentFrequency frequency, int count)
    {
        var c = Make(new DateOnly(2026, 7, 1), new DateOnly(2028, 6, 30), 250_001, frequency);
        var schedule = SalaryCalculator.PaymentSchedule(c);
        Assert.Equal(count, schedule.Count);
        Assert.Equal(SalaryCalculator.TotalSalary(c), schedule.Sum(p => p.Amount));
        Assert.Equal(new DateOnly(2028, 6, 30), schedule[^1].DueDate);
        Assert.Equal(schedule[0].PeriodEnd.AddDays(1), schedule[1].PeriodStart);
    }

    [Fact]
    public void Remaining_salary_and_payments_stop_at_the_early_end()
    {
        var c = Make(new DateOnly(2026, 7, 1), new DateOnly(2028, 6, 30), 120_000);
        Assert.Equal(120_000, SalaryCalculator.RemainingSalary(c, new DateOnly(2027, 7, 1)));
        c.Terminate(new DateOnly(2027, 1, 1), 0);
        Assert.Equal(0, SalaryCalculator.RemainingSalary(c, new DateOnly(2027, 1, 1)));
        var schedule = SalaryCalculator.PaymentSchedule(c);
        Assert.Equal(6, schedule.Count);
        Assert.Equal(new DateOnly(2026, 12, 31), schedule[^1].DueDate);
        Assert.Equal(new DateOnly(2026, 11, 30), SalaryCalculator.NextPayment(c, new DateOnly(2026, 11, 9))!.DueDate);
        Assert.Null(SalaryCalculator.NextPayment(c, new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Club_commitments_add_running_and_upcoming_contracts()
    {
        var today = new DateOnly(2026, 11, 9);
        var running = Make(new DateOnly(2026, 7, 1), new DateOnly(2027, 6, 30), 100_000);
        var upcoming = Contract.ForPlayer(ContractId.New(), ClubId.New(), PlayerId.New(),
            new ContractTerms(new DateOnly(2027, 7, 1), new DateOnly(2029, 6, 30), 200_000, Bonuses: PerformanceBonuses.Of((BonusCondition.LeagueTitle, 30_000))));
        var past = Make(new DateOnly(2024, 7, 1), new DateOnly(2026, 6, 30), 900_000);

        var c = SalaryCommitments.Calculate([running, upcoming, past], today);

        Assert.Equal((100_000L, 1, 1, 30_000L), (c.WageBill, c.RunningContracts, c.UpcomingContracts, c.MaxSeasonBonuses));
        Assert.Equal(SalaryCalculator.RemainingSalary(running, today) + 400_000, c.RemainingSalary);
        Assert.Equal(3, c.Periods.Count); // to 2027-11-08, 2028-11-08, 2029-11-08
        Assert.Equal(today, c.Periods[0].From);
        Assert.Equal(2, c.Periods[0].Contracts);
        Assert.InRange(c.Periods.Sum(p => p.Salary) - c.RemainingSalary, -2, 2); // rounding per period: within a couple of euros
        Assert.Equal(c.NextTwelveMonths, c.Periods[0].Salary);
    }
}

public sealed class ContractNegotiatorTests
{
    private static readonly ContractNegotiator Negotiator = ContractNegotiator.Default;
    private static readonly NegotiationContext Typical = new(Overall: 70, Potential: 72, Reputation: 50, Age: 27, ClubReputation: 50);

    private static ContractTerms Offer(long salary, int years = 2) =>
        new(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1).AddYears(years).AddDays(-1), salary);

    [Fact]
    public void Asking_salary_grows_with_quality_and_falls_with_club_prestige_and_age()
    {
        var asking = Negotiator.AskingSalary(Typical);
        Assert.InRange(asking, 250_000, 400_000);
        Assert.Equal(0, asking % 1000);
        Assert.True(Negotiator.AskingSalary(Typical with { Overall = 80 }) > asking);
        Assert.True(Negotiator.AskingSalary(Typical with { Reputation = 90 }) > asking);
        Assert.True(Negotiator.AskingSalary(Typical with { ClubReputation = 95 }) < asking);
        Assert.True(Negotiator.AskingSalary(Typical with { Age = 35 }) < asking);
        Assert.True(Negotiator.AskingSalary(Typical with { Age = 20, Potential = 90 }) > asking);
        Assert.Equal(ContractNegotiator.MinimumSalary, Negotiator.AskingSalary(Typical with { Overall = 1 }));
        Assert.Equal(900_000, Negotiator.AskingSalary(Typical with { CurrentSalary = 1_000_000 })); // at most a 10% pay cut
    }

    [Fact]
    public void Offers_are_accepted_countered_or_rejected()
    {
        var asking = Negotiator.AskingSalary(Typical);
        Assert.Equal(NegotiationOutcome.Accepted, Negotiator.Evaluate(Typical, Offer(asking)).Outcome);

        var close = Negotiator.Evaluate(Typical, Offer(asking * 9 / 10));
        Assert.Equal(NegotiationOutcome.CounterOffer, close.Outcome);
        Assert.True(Negotiator.Evaluate(Typical, close.CounterOffer!).IsAccepted);

        var low = Negotiator.Evaluate(Typical, Offer(asking / 2));
        Assert.Equal(NegotiationOutcome.Rejected, low.Outcome);
        Assert.Null(low.CounterOffer);

        Assert.Equal(NegotiationOutcome.Rejected, Negotiator.Evaluate(Typical, Offer(asking * 2, years: 6)).Outcome);
    }

    [Fact]
    public void Signing_bonus_bonuses_and_options_change_the_value_of_an_offer()
    {
        var plain = Negotiator.OfferValue(Offer(200_000));
        Assert.Equal(200_000, plain);
        Assert.Equal(250_000, Negotiator.OfferValue(Offer(200_000) with { SigningBonus = 100_000 }));
        Assert.Equal(210_000, Negotiator.OfferValue(Offer(200_000) with { PlayerOptionUntil = new DateOnly(2029, 6, 30) }));
        Assert.Equal(190_000, Negotiator.OfferValue(Offer(200_000) with { ClubOptionUntil = new DateOnly(2029, 6, 30) }));
        Assert.Equal(205_000, Negotiator.OfferValue(Offer(200_000) with { Bonuses = PerformanceBonuses.Of((BonusCondition.LeagueTitle, 20_000)) }));

        // A counter-offer that includes a club option still reaches the asking value.
        var asking = Negotiator.AskingSalary(Typical);
        var counter = Negotiator.Evaluate(Typical, Offer(asking * 9 / 10) with { ClubOptionUntil = new DateOnly(2029, 6, 30) }).CounterOffer!;
        Assert.True(Negotiator.Evaluate(Typical, counter).IsAccepted);
    }

    [Fact]
    public void A_player_takes_up_his_option_only_when_it_pays_at_least_his_market_value()
    {
        var market = Negotiator.AskingSalary(Typical);
        Assert.True(Negotiator.ExercisesPlayerOption(Typical, market));
        Assert.False(Negotiator.ExercisesPlayerOption(Typical with { CurrentSalary = market * 3 }, market - 1000));
    }
}
