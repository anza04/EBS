using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;

namespace Ebm.Domain.Management;

/// <summary>Where a club finished a league season, for judging its objective.</summary>
/// <param name="PlayoffPlaces">Table positions that reach the playoffs (empty when there are none).</param>
public sealed record SeasonFinish(int Position, int Clubs, bool Champion, bool Promoted, bool Relegated, IReadOnlyCollection<int> PlayoffPlaces);

/// <summary>Board objectives (docs/AI_MANAGEMENT.md §6): how a finish is judged, and the objective set for the next season.</summary>
public static class SeasonObjectives
{
    public static bool IsMet(BoardExpectation expectation, SeasonFinish finish)
    {
        ArgumentNullException.ThrowIfNull(finish);
        return expectation switch
        {
            BoardExpectation.WinTitle => finish.Champion,
            BoardExpectation.WinPromotion => finish.Promoted,
            BoardExpectation.ReachPlayoffs => finish.PlayoffPlaces.Count > 0 ? finish.PlayoffPlaces.Contains(finish.Position) : finish.Position <= Math.Max(1, finish.Clubs / 4),
            BoardExpectation.MidTable => !finish.Relegated && finish.Position <= (finish.Clubs * 2 + 2) / 3,
            BoardExpectation.AvoidRelegation => !finish.Relegated,
            _ => throw new ArgumentOutOfRangeException(nameof(expectation), expectation, "Unknown board expectation."),
        };
    }

    /// <summary>
    /// The objective for a club ranked <paramref name="reputationRank"/> (1 = highest) of <paramref name="clubs"/> in its
    /// competition: the favourite aims for the title (or promotion, where the competition promotes), the playoff places
    /// for the playoffs, the bottom places (relegation places plus one) to stay up, the rest for mid-table.
    /// </summary>
    public static BoardExpectation For(int reputationRank, int clubs, int playoffTeams, int promotionPlaces, int relegationPlaces)
    {
        if (reputationRank == 1)
        {
            return promotionPlaces > 0 ? BoardExpectation.WinPromotion : BoardExpectation.WinTitle;
        }

        if (relegationPlaces > 0 && reputationRank > clubs - relegationPlaces - 1)
        {
            return BoardExpectation.AvoidRelegation;
        }

        if (promotionPlaces > 0 && reputationRank <= promotionPlaces)
        {
            return BoardExpectation.WinPromotion;
        }

        return reputationRank <= playoffTeams ? BoardExpectation.ReachPlayoffs : BoardExpectation.MidTable;
    }
}

/// <summary>
/// Club money per game day (docs/AI_MANAGEMENT.md §5). Salaries are paid on their instalment due dates
/// (<see cref="SalaryCalculator.PaymentSchedule"/>); a parent contract of a loaned-out player is not paid (the loan club
/// pays). Income is a placeholder until the finance step (27): on the last day of each month a club receives one twelfth
/// of its wage budget (the board funds the budget it sets).
/// </summary>
public static class ClubFinances
{
    /// <summary>Salary instalments of <paramref name="contract"/> due on <paramref name="date"/>.</summary>
    public static long SalaryDue(Contract contract, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (contract.Status == ContractStatus.OnLoan || !contract.IsRunningOn(date))
        {
            return 0;
        }

        return SalaryCalculator.PaymentSchedule(contract).Where(p => p.DueDate == date).Sum(p => p.Amount);
    }

    public static bool IsIncomeDay(DateOnly date) => date.AddDays(1).Day == 1;

    public static long MonthlyIncome(Club club)
    {
        ArgumentNullException.ThrowIfNull(club);
        return club.WageBudget / 12;
    }
}

/// <summary>
/// Which contracts an AI club renews (docs/AI_MANAGEMENT.md §4): players in the club's best
/// <see cref="CoreSquad"/> by overall, aged up to <see cref="MaxAge"/>, whose contract ends within
/// <see cref="NoticeDays"/>. Length by age; the club pays up to <see cref="MaxRaise"/> times the current salary.
/// </summary>
public static class RenewalPolicy
{
    public const int CoreSquad = 10;
    public const int MaxAge = 33;
    public const int NoticeDays = 150;
    public const double MaxRaise = 1.5;

    public static bool Wants(int overallRank, int age, DateOnly endDate, DateOnly today) =>
        overallRank <= CoreSquad && age <= MaxAge && endDate >= today && endDate.DayNumber - today.DayNumber <= NoticeDays;

    public static int Years(int age) => age < 27 ? 3 : age < 31 ? 2 : 1;
}
