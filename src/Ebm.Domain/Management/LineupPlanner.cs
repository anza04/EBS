using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;

namespace Ebm.Domain.Management;

/// <summary>A squad player as the AI coach sees him.</summary>
public sealed record LineupCandidate(PlayerId PlayerId, RosterRole Role, PlayerAvailability Availability, PlayerEvaluation Evaluation);

/// <summary>The role the AI coach gives a player; <paramref name="LineupPosition"/> for starters.</summary>
public sealed record PlannedRole(PlayerId PlayerId, RosterRole Role, Position? LineupPosition = null);

/// <summary>
/// The AI coach's squad roles (docs/AI_MANAGEMENT.md §2): the starting five that maximises effective ratings at the
/// five positions, a rotation of the next best players by overall, the bench up to the matchday squad, the rest
/// inactive. Unavailable players are left out; doubtful players count 5 points lower. Youth players keep their role.
/// Pure and deterministic.
/// </summary>
public static class LineupPlanner
{
    public const int RotationPlayers = 4;
    public const int DoubtfulPenalty = 5;

    /// <returns>Every candidate once, rotation players in rotation order.</returns>
    public static IReadOnlyList<PlannedRole> Plan(IReadOnlyList<LineupCandidate> squad, SquadRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(squad);
        rules ??= SquadRules.Default;
        var plan = new List<PlannedRole>();
        var pool = squad.Where(p => p.Role != RosterRole.Youth && p.Availability != PlayerAvailability.Unavailable)
            .OrderByDescending(Score).ThenBy(p => p.PlayerId.Value)
            .ToList();

        // Starters: repeatedly take the best remaining (player, position) pair.
        var open = Enum.GetValues<Position>().ToList();
        while (open.Count > 0 && pool.Count > 0)
        {
            var (player, position) = pool.SelectMany(p => open.Select(pos => (p, pos)))
                .OrderByDescending(x => Effective(x.p, x.pos)).ThenBy(x => x.p.PlayerId.Value).ThenBy(x => x.pos)
                .First();
            plan.Add(new PlannedRole(player.PlayerId, RosterRole.Starter, position));
            pool.Remove(player);
            open.Remove(position);
        }

        foreach (var player in pool.Take(RotationPlayers))
        {
            plan.Add(new PlannedRole(player.PlayerId, RosterRole.Rotation));
        }

        foreach (var player in pool.Skip(RotationPlayers).Take(Math.Max(0, rules.MatchdayPlayers - SquadRules.Starters - RotationPlayers)))
        {
            plan.Add(new PlannedRole(player.PlayerId, RosterRole.Bench));
        }

        var planned = plan.Select(p => p.PlayerId).ToHashSet();
        plan.AddRange(squad.Where(p => !planned.Contains(p.PlayerId))
            .Select(p => new PlannedRole(p.PlayerId, p.Role == RosterRole.Youth ? RosterRole.Youth : RosterRole.Inactive)));
        return plan;
    }

    private static int Score(LineupCandidate p) => p.Evaluation.Overall - Penalty(p);

    private static int Effective(LineupCandidate p, Position position) => p.Evaluation.At(position).EffectiveRating - Penalty(p);

    private static int Penalty(LineupCandidate p) => p.Availability == PlayerAvailability.Doubtful ? DoubtfulPenalty : 0;
}
