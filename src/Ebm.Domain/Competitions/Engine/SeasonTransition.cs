using Ebm.Domain.Clubs;

namespace Ebm.Domain.Competitions.Engine;

/// <summary>One division's finished season: its level in the league, its clubs and its outcome.</summary>
public sealed record DivisionSeason(int Level, IReadOnlyList<ClubId> Clubs, SeasonOutcome Outcome);

/// <summary>
/// Applies promotion and relegation between the divisions of one league: each division keeps its clubs, minus those
/// promoted to the division above or relegated to the one below, plus those coming down from above and up from below.
/// </summary>
public static class SeasonTransition
{
    /// <returns>The clubs of each division (by level) for the next season, in a stable order.</returns>
    public static IReadOnlyDictionary<int, IReadOnlyList<ClubId>> NextEntries(IReadOnlyList<DivisionSeason> divisions)
    {
        ArgumentNullException.ThrowIfNull(divisions);
        var byLevel = divisions.ToDictionary(d => d.Level);
        var next = new Dictionary<int, IReadOnlyList<ClubId>>();
        foreach (var division in divisions)
        {
            var up = byLevel.ContainsKey(division.Level - 1) ? division.Outcome.Promoted : [];
            var down = byLevel.ContainsKey(division.Level + 1) ? division.Outcome.Relegated : [];
            var arriving = (byLevel.TryGetValue(division.Level - 1, out var above) ? above.Outcome.Relegated : [])
                .Concat(byLevel.TryGetValue(division.Level + 1, out var below) ? below.Outcome.Promoted : []);
            next[division.Level] =
            [
                .. division.Clubs.Except(up).Except(down).Concat(arriving).Distinct().OrderBy(c => c.Value),
            ];
        }

        return next;
    }
}
