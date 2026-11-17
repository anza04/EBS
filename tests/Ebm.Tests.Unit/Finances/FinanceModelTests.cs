using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Finances;
using Ebm.Domain.Seasons;

namespace Ebm.Tests.Unit.Finances;

/// <summary>Step 23: club income, running costs and the board's budget rules (docs/FINANCES.md).</summary>
public sealed class FinanceModelTests
{
    private static readonly FinanceModel Model = FinanceModel.Default;

    private static BudgetInput Input(int reputation = 50, long wageBudget = 1_000_000, long balance = 500_000, ClubBudget? previous = null, bool? objectiveMet = null) =>
        new(reputation, wageBudget, balance, 5_000, 15, 13, 2, 150_000, previous, objectiveMet);

    private static ClubBudget Stored(BudgetPlan plan, int reputation = 50, long balance = 500_000) =>
        new(ClubBudgetId.New(), ClubId.New(), SeasonId.New(), new DateOnly(2026, 9, 1), reputation, balance, plan);

    [Fact]
    public void The_first_budget_keeps_the_wage_budget_and_the_sponsor_funds_it()
    {
        var plan = Model.Plan(Input());

        Assert.Equal(1_000_000, plan.WageBudget);
        Assert.True(plan.ExpectedIncome > plan.ExpectedCosts, "the first year must not plan a loss");
        Assert.InRange(plan.ExpectedIncome - plan.ExpectedCosts, 0, plan.ExpectedCosts / 10);
        Assert.Equal(15, plan.HomeGames);
        Assert.Equal(plan.Attendance * plan.TicketPrice * 15, plan.TicketSales);
        Assert.Equal((13 * Model.TripCost(50, false)) + (2 * Model.TripCost(50, true)), plan.TravelCosts);
        Assert.Equal((12 * Model.MonthlyUpkeep(5_000)) + (15 * Model.MatchdayCost(5_000)), plan.ArenaCosts);
        Assert.Equal(250_000, plan.TransferBudget); // half the balance
    }

    [Fact]
    public void Crowds_and_prices_grow_with_reputation_and_never_exceed_the_arena()
    {
        Assert.True(Model.Attendance(80, 5_000) > Model.Attendance(30, 5_000));
        Assert.True(Model.TicketPrice(80) > Model.TicketPrice(30));
        Assert.True(Model.Attendance(60, 5_000, bigGame: true) > Model.Attendance(60, 5_000));
        Assert.Equal(5_000, Model.Attendance(100, 5_000, bigGame: true));
        Assert.Equal(Model.Attendance(50, Model.DefaultCapacity), Model.Attendance(50, null));
        Assert.True(Model.TripCost(50, abroad: true) > Model.TripCost(50, abroad: false));
    }

    [Fact]
    public void Renewed_sponsorship_follows_reputation_and_results_within_limits()
    {
        var first = Stored(Model.Plan(Input()));

        var same = Model.Plan(Input(previous: first));
        var success = Model.Plan(Input(reputation: 55, previous: first, objectiveMet: true));
        var failure = Model.Plan(Input(reputation: 45, previous: first, objectiveMet: false));
        var collapse = Model.Plan(Input(reputation: 1, previous: first, objectiveMet: false));

        Assert.Equal(first.Sponsorship, same.Sponsorship);
        Assert.InRange(success.Sponsorship, (long)(first.Sponsorship * 1.13) - 1, (long)(first.Sponsorship * 1.13) + 1);
        Assert.InRange(failure.Sponsorship, (long)(first.Sponsorship * 0.87) - 1, (long)(first.Sponsorship * 0.87) + 1);
        Assert.InRange(collapse.Sponsorship, (long)(first.Sponsorship * Model.MinSponsorshipChange) - 1, (long)(first.Sponsorship * Model.MinSponsorshipChange) + 1);
        Assert.True(success.WageBudget > same.WageBudget && same.WageBudget > failure.WageBudget);
    }

    [Fact]
    public void The_board_spends_part_of_the_reserves_and_makes_a_club_in_debt_pay_it_back()
    {
        var first = Stored(Model.Plan(Input()));

        var rich = Model.Plan(Input(balance: 2_000_000, previous: first));
        var even = Model.Plan(Input(balance: 0, previous: first));
        var debt = Model.Plan(Input(balance: -600_000, previous: first));

        Assert.Equal(even.WageBudget + 200_000, rich.WageBudget);
        Assert.True(debt.WageBudget < even.WageBudget);
        Assert.True(debt.ExpectedIncome - debt.ExpectedCosts >= 300_000 - 1, "a club in debt plans a surplus to repay half of it");
        Assert.Equal((0L, -300_000L), (debt.TransferBudget, debt.MinimumBalance));
        Assert.True(rich.MinimumBalance is > 0 and < 2_000_000);
        Assert.Equal(0, even.MinimumBalance);
    }

    [Fact]
    public void A_crushing_debt_still_leaves_wages_for_the_squad()
    {
        var first = Stored(Model.Plan(Input()));
        var plan = Model.Plan(Input(balance: -50_000_000, previous: first));
        var surplus = plan.Sponsorship + plan.TicketSales - plan.ArenaCosts - plan.TravelCosts - plan.StaffWages;
        Assert.Equal((long)(surplus * Model.MinWageShare), plan.WageBudget);
    }

    [Fact]
    public void The_target_is_met_at_or_above_the_minimum_balance()
    {
        var budget = Stored(Model.Plan(Input(balance: 1_000_000)));
        Assert.True(FinanceModel.MeetsTarget(budget, budget.MinimumBalance));
        Assert.False(FinanceModel.MeetsTarget(budget, budget.MinimumBalance - 1));
    }

    [Fact]
    public void Financial_years_follow_each_other_without_gaps()
    {
        var (from, to) = Model.Year(new DateOnly(2026, 10, 1));
        var (nextFrom, _) = Model.Year(new DateOnly(2027, 10, 1));
        Assert.Equal(new DateOnly(2026, 9, 1), from);
        Assert.Equal(to.AddDays(1), nextFrom);
    }

    [Fact]
    public void A_transaction_needs_an_amount_and_trims_long_descriptions()
    {
        Assert.Throws<DomainValidationException>(() => new FinancialTransaction(FinancialTransactionId.New(), ClubId.New(), new DateOnly(2026, 1, 1), FinanceCategory.Sponsorship, 0));
        var t = new FinancialTransaction(FinancialTransactionId.New(), ClubId.New(), new DateOnly(2026, 1, 1), FinanceCategory.TicketSales, 10, new string('x', 300));
        Assert.Equal(FinancialTransaction.DescriptionMaxLength, t.Description!.Length);
    }
}
