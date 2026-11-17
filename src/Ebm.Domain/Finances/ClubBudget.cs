using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Seasons;

namespace Ebm.Domain.Finances;

public readonly record struct ClubBudgetId(Guid Value) : IEntityId<ClubBudgetId>
{
    public static ClubBudgetId New() => new(Guid.CreateVersion7());

    public static ClubBudgetId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// The board's budget of one club for one season (docs/FINANCES.md §3): expected income and running costs, the wage
/// and transfer budgets and the financial target (<see cref="MinimumBalance"/>). Set once when the season is planned;
/// kept as history. The wage budget is also copied to <see cref="Club.WageBudget"/>, which the game systems read.
/// </summary>
public sealed class ClubBudget : Entity<ClubBudgetId>
{
    public ClubBudget(ClubBudgetId id, ClubId clubId, SeasonId seasonId, DateOnly plannedOn, int reputation, long openingBalance, BudgetPlan plan)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ClubId = Guard.Required(clubId, nameof(clubId));
        SeasonId = Guard.Required(seasonId, nameof(seasonId));
        PlannedOn = plannedOn;
        Reputation = Guard.Range(reputation, Club.MinReputation, Club.MaxReputation, "Club reputation");
        OpeningBalance = openingBalance;
        Sponsorship = plan.Sponsorship;
        TicketPrice = plan.TicketPrice;
        Attendance = plan.Attendance;
        HomeGames = plan.HomeGames;
        TicketSales = plan.TicketSales;
        ArenaCosts = plan.ArenaCosts;
        TravelCosts = plan.TravelCosts;
        StaffWages = plan.StaffWages;
        WageBudget = plan.WageBudget;
        TransferBudget = plan.TransferBudget;
        MinimumBalance = plan.MinimumBalance;
    }

    private ClubBudget()
    {
    }

    public ClubId ClubId { get; private set; }

    public SeasonId SeasonId { get; private set; }

    public DateOnly PlannedOn { get; private set; }

    /// <summary>The club's reputation when the budget was set (the next deal changes with it).</summary>
    public int Reputation { get; private set; }

    public long OpeningBalance { get; private set; }

    /// <summary>Sponsorship income for the year, paid in twelve monthly instalments.</summary>
    public long Sponsorship { get; private set; }

    public long TicketPrice { get; private set; }

    /// <summary>Expected spectators at a regular-season home game.</summary>
    public int Attendance { get; private set; }

    public int HomeGames { get; private set; }

    public long TicketSales { get; private set; }

    public long ArenaCosts { get; private set; }

    public long TravelCosts { get; private set; }

    public long StaffWages { get; private set; }

    public long WageBudget { get; private set; }

    /// <summary>What the club may spend on fees, signing bonuses and loan payments this year.</summary>
    public long TransferBudget { get; private set; }

    /// <summary>The board's financial target: the balance at the end of the year must not be lower.</summary>
    public long MinimumBalance { get; private set; }

    public long ExpectedIncome => Sponsorship + TicketSales;

    public long ExpectedCosts => ArenaCosts + TravelCosts + StaffWages + WageBudget;
}
