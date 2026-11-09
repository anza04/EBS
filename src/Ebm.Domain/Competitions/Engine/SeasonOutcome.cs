using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions.Rules;

namespace Ebm.Domain.Competitions.Engine;

/// <summary>
/// The result of a finished competition season. The champion is the playoff winner, or the table leader when
/// there are no playoffs. Promoted clubs move up one division, relegated clubs down one.
/// </summary>
public sealed record SeasonOutcome(ClubId Champion, IReadOnlyList<ClubId> Promoted, IReadOnlyList<ClubId> Relegated);

public static class SeasonOutcomeCalculator
{
    /// <param name="finalTable">The complete regular-season table.</param>
    /// <param name="playoffChampion">Required when the rules have playoffs.</param>
    public static SeasonOutcome Calculate(CompetitionRules rules, IReadOnlyList<StandingRow> finalTable, ClubId? playoffChampion)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(finalTable);
        rules.ValidateForClubs(finalTable.Count);
        if (rules.Playoffs is not null && playoffChampion is null)
        {
            throw new DomainValidationException("The season is not over: the playoffs have no champion yet.");
        }

        var champion = playoffChampion ?? finalTable[0].ClubId;
        var promoted = finalTable.Take(rules.Promotion.Direct).Select(r => r.ClubId).ToList();
        if (rules.Promotion.PlayoffWinner)
        {
            promoted.Add(champion);
        }

        var relegated = finalTable.TakeLast(rules.Relegation.Direct).Select(r => r.ClubId).ToList();
        return new SeasonOutcome(champion, promoted, relegated);
    }
}
