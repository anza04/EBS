namespace Ebm.Domain.Finances;

/// <summary>What the board knows when it sets a club's budget for a season (docs/FINANCES.md §3).</summary>
/// <param name="CurrentWageBudget">The wage budget of the year before (the dataset's for the first season).</param>
/// <param name="ArenaCapacity">Home arena capacity; <c>null</c> without an arena.</param>
/// <param name="StaffWages">Annual salaries of the staff under contract.</param>
/// <param name="Previous">The budget of the year before; <c>null</c> for the first budget of the club.</param>
/// <param name="ObjectiveMet">Whether the sporting objective of the year before was met (<c>null</c> = unknown).</param>
public sealed record BudgetInput(
    int Reputation,
    long CurrentWageBudget,
    long Balance,
    int? ArenaCapacity,
    int HomeGames,
    int DomesticAwayGames,
    int ForeignAwayGames,
    long StaffWages,
    ClubBudget? Previous = null,
    bool? ObjectiveMet = null);

/// <summary>A season budget (docs/FINANCES.md §3); stored as <see cref="ClubBudget"/>.</summary>
public sealed record BudgetPlan(
    long Sponsorship,
    long TicketPrice,
    int Attendance,
    int HomeGames,
    long TicketSales,
    long ArenaCosts,
    long TravelCosts,
    long StaffWages,
    long WageBudget,
    long TransferBudget,
    long MinimumBalance)
{
    public long ExpectedIncome => Sponsorship + TicketSales;

    public long ExpectedCosts => ArenaCosts + TravelCosts + StaffWages + WageBudget;
}

/// <summary>
/// Club income, running costs and the board's budget rules (docs/FINANCES.md). Pure and deterministic; registered as a
/// singleton. All amounts are whole euros.
/// </summary>
public sealed class FinanceModel
{
    public static FinanceModel Default { get; } = new();

    /// <summary>A financial year starts this many days before its season (preseason, transfer window) and ends the day before the next one.</summary>
    public int YearStartOffsetDays { get; init; } = 30;

    /// <summary>Capacity assumed for a club without a home arena.</summary>
    public int DefaultCapacity { get; init; } = 2_500;

    /// <summary>Share of the seats sold at reputation 0 and the gain per reputation point (max <see cref="MaxFill"/>).</summary>
    public double BaseFill { get; init; } = 0.35;

    public double FillPerReputation { get; init; } = 0.006;

    public double MaxFill { get; init; } = 0.95;

    /// <summary>Playoff, cup and international home games draw this many times the regular attendance (up to capacity).</summary>
    public double BigGameDraw { get; init; } = 1.15;

    public long BaseTicketPrice { get; init; } = 6;

    public double TicketPricePerReputation { get; init; } = 0.3;

    /// <summary>Arena upkeep a month: fixed part plus euros per seat.</summary>
    public long ArenaUpkeepBase { get; init; } = 2_000;

    public double ArenaUpkeepPerSeat { get; init; } = 1.0;

    /// <summary>Operating a home game: fixed part plus euros per seat.</summary>
    public long MatchdayCostBase { get; init; } = 3_000;

    public double MatchdayCostPerSeat { get; init; } = 1.5;

    /// <summary>An away trip in the club's own country, and abroad: fixed part plus euros per reputation point.</summary>
    public long DomesticTripBase { get; init; } = 3_000;

    public long DomesticTripPerReputation { get; init; } = 50;

    public long ForeignTripBase { get; init; } = 12_000;

    public long ForeignTripPerReputation { get; init; } = 150;

    /// <summary>The first sponsorship deal covers the planned costs (wages included) less ticket sales, plus this margin.</summary>
    public double FirstDealMargin { get; init; } = 0.05;

    /// <summary>Smallest sponsorship deal a club gets, per reputation point.</summary>
    public long MinSponsorshipPerReputation { get; init; } = 2_000;

    /// <summary>A renewed deal changes by this share per reputation point gained or lost.</summary>
    public double SponsorshipPerReputationPoint { get; init; } = 0.02;

    /// <summary>A renewed deal grows by this share when the sporting objective was met, and shrinks by it when missed.</summary>
    public double SponsorshipObjectiveEffect { get; init; } = 0.03;

    public double MinSponsorshipChange { get; init; } = 0.85;

    public double MaxSponsorshipChange { get; init; } = 1.20;

    /// <summary>Share of the operating surplus (income less running costs) the board gives to player wages.</summary>
    public double WageShare { get; init; } = 0.95;

    /// <summary>Share of a positive balance added to the wage budget, and of a debt taken off it (repaid this year).</summary>
    public double ReserveSpend { get; init; } = 0.10;

    public double DebtRepayment { get; init; } = 0.50;

    /// <summary>The wage budget never falls below this share of the operating surplus (the squad still has to be paid).</summary>
    public double MinWageShare { get; init; } = 0.40;

    /// <summary>Share of a positive balance the board allows for fees, bonuses and loan payments.</summary>
    public double TransferShare { get; init; } = 0.50;

    /// <summary>Loss the board tolerates in a year, as a share of the expected income (on top of the planned reserve spending).</summary>
    public double ToleratedLoss { get; init; } = 0.05;

    /// <summary>The financial year that holds <paramref name="seasonStart"/>'s season.</summary>
    public (DateOnly From, DateOnly To) Year(DateOnly seasonStart) =>
        (seasonStart.AddDays(-YearStartOffsetDays), seasonStart.AddYears(1).AddDays(-YearStartOffsetDays - 1));

    public int Capacity(int? arenaCapacity) => arenaCapacity is > 0 ? arenaCapacity.Value : DefaultCapacity;

    /// <summary>Spectators at a home game: a share of the seats that grows with reputation; big games draw more.</summary>
    public int Attendance(int reputation, int? arenaCapacity, bool bigGame = false)
    {
        var capacity = Capacity(arenaCapacity);
        var fill = Math.Min(MaxFill, BaseFill + (FillPerReputation * reputation)) * (bigGame ? BigGameDraw : 1.0);
        return (int)Math.Round(capacity * Math.Min(1.0, fill));
    }

    public long TicketPrice(int reputation) => BaseTicketPrice + (long)Math.Round(TicketPricePerReputation * reputation);

    public long TicketSales(int reputation, int? arenaCapacity, bool bigGame = false) => Attendance(reputation, arenaCapacity, bigGame) * TicketPrice(reputation);

    public long MatchdayCost(int? arenaCapacity) => MatchdayCostBase + (long)Math.Round(MatchdayCostPerSeat * Capacity(arenaCapacity));

    public long MonthlyUpkeep(int? arenaCapacity) => ArenaUpkeepBase + (long)Math.Round(ArenaUpkeepPerSeat * Capacity(arenaCapacity));

    public long TripCost(int reputation, bool abroad) =>
        abroad ? ForeignTripBase + (ForeignTripPerReputation * reputation) : DomesticTripBase + (DomesticTripPerReputation * reputation);

    /// <summary>
    /// The board's budget for the season (docs/FINANCES.md §3). The first budget keeps the club's wage budget and sizes the
    /// sponsorship deal to fund it; later ones renew the deal by reputation change and results, then give
    /// <see cref="WageShare"/> of the operating surplus to wages, adjusted by the reserves or the debt.
    /// </summary>
    public BudgetPlan Plan(BudgetInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var price = TicketPrice(input.Reputation);
        var attendance = Attendance(input.Reputation, input.ArenaCapacity);
        var tickets = attendance * price * input.HomeGames;
        var arena = (12 * MonthlyUpkeep(input.ArenaCapacity)) + (input.HomeGames * MatchdayCost(input.ArenaCapacity));
        var travel = (input.DomesticAwayGames * TripCost(input.Reputation, false)) + (input.ForeignAwayGames * TripCost(input.Reputation, true));
        var running = arena + travel + input.StaffWages;
        var minimumDeal = MinSponsorshipPerReputation * input.Reputation;

        long sponsorship, wages;
        if (input.Previous is not { } previous)
        {
            wages = input.CurrentWageBudget;
            sponsorship = Math.Max(minimumDeal, (long)Math.Round((wages + running - tickets) * (1 + FirstDealMargin)));
        }
        else
        {
            var change = 1.0
                + (SponsorshipPerReputationPoint * (input.Reputation - previous.Reputation))
                + (input.ObjectiveMet switch { true => SponsorshipObjectiveEffect, false => -SponsorshipObjectiveEffect, null => 0 });
            sponsorship = Math.Max(minimumDeal, (long)Math.Round(previous.Sponsorship * Math.Clamp(change, MinSponsorshipChange, MaxSponsorshipChange)));
            var surplus = Math.Max(0, sponsorship + tickets - running);
            var reserves = input.Balance >= 0 ? (long)(input.Balance * ReserveSpend) : (long)(input.Balance * DebtRepayment);
            wages = Math.Max((long)(surplus * MinWageShare), (long)(surplus * WageShare) + reserves);
        }

        wages = Math.Clamp(wages, 0, Clubs.Club.MaxWageBudget);
        var income = sponsorship + tickets;
        var transfers = (long)(Math.Max(0, input.Balance) * TransferShare);
        var minimumBalance = input.Balance >= 0
            ? Math.Max(0, input.Balance - (long)(input.Balance * ReserveSpend) - (long)(income * ToleratedLoss))
            : input.Balance - (long)(input.Balance * DebtRepayment);
        return new BudgetPlan(sponsorship, price, attendance, input.HomeGames, tickets, arena, travel, input.StaffWages, wages, transfers, minimumBalance);
    }

    /// <summary>Whether a balance meets the board's financial target of the budget.</summary>
    public static bool MeetsTarget(ClubBudget budget, long balance)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return balance >= budget.MinimumBalance;
    }
}
