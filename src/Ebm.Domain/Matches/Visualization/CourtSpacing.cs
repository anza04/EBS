using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;

namespace Ebm.Domain.Matches.Visualization;

/// <summary>
/// Floor spacing for the court (docs/MATCH_VISUALIZATION.md §3.3): attackers keep a few metres between each other by
/// moving to free half-court spots, and nobody stands on top of anybody else (a defender may be tight on his man).
/// </summary>
internal static class CourtSpacing
{
    /// <summary>Metres an attacker wants between himself and every teammate.</summary>
    public const double Spacing = 3.0;

    /// <summary>Nearest two players get, except a defender on his man.</summary>
    public const double Gap = 0.75;

    /// <summary>Spacing spots (across, depth from the attacked baseline): top, wings, corners, slots, short corners, elbows.</summary>
    private static readonly (double A, double D)[] Spots =
        [(0.0, 8.3), (-5.6, 6.3), (5.6, 6.3), (-6.6, 0.9), (6.6, 0.9), (-2.9, 7.7), (2.9, 7.7), (-3.4, 1.2), (3.4, 1.2), (-2.3, 5.0), (2.3, 5.0)];

    /// <summary>
    /// Moves crowded attackers (in the frontcourt, not <paramref name="anchored"/>) to the free spot that spaces them
    /// best, staying near where they were. Slot order decides who moves first, so the result is deterministic.
    /// </summary>
    public static void Spread(List<CourtPlayer> players, TeamSide offense, int period, IReadOnlySet<PlayerId> anchored)
    {
        bool Backcourt(CourtPlayer p) => Depth(offense, period, p.Position) > CourtTracker.Length / 2;
        var placed = players.Where(p => p.Side == offense && (anchored.Contains(p.Player) || Backcourt(p))).Select(p => p.Position).ToList();
        var spots = Spots.Select(s => CourtTracker.ToCourt(offense, period, s.A, s.D)).ToList();
        var order = Enumerable.Range(0, players.Count)
            .Where(i => players[i].Side == offense && !anchored.Contains(players[i].Player) && !Backcourt(players[i]))
            .OrderBy(i => players[i].Slot)
            .ToList();
        foreach (var i in order)
        {
            var p = players[i];
            if (placed.Count == 0 || placed.Min(q => q.DistanceTo(p.Position)) >= Spacing)
            {
                placed.Add(p.Position);
                continue;
            }

            var best = spots.MaxBy(s => Math.Min(Spacing + 1, placed.Min(q => q.DistanceTo(s))) - (0.15 * s.DistanceTo(p.Position)));
            players[i] = p with { Position = best };
            placed.Add(best);
        }
    }

    /// <summary>Pushes apart any two players closer than <see cref="Gap"/>; anchored players stay put.</summary>
    public static void Separate(List<CourtPlayer> players, IReadOnlySet<PlayerId> anchored)
    {
        for (var round = 0; round < 4; round++)
        {
            var moved = false;
            for (var i = 0; i < players.Count; i++)
            {
                for (var j = i + 1; j < players.Count; j++)
                {
                    var (a, b) = (players[i], players[j]);
                    if (a.Side != b.Side && a.Slot == b.Slot)
                    {
                        continue; // a defender on his man
                    }

                    var d = a.Position.DistanceTo(b.Position);
                    var (fa, fb) = (anchored.Contains(a.Player), anchored.Contains(b.Player));
                    if (d >= Gap || (fa && fb))
                    {
                        continue;
                    }

                    var (dx, dy) = d < 1e-6 ? (Math.Cos(a.Slot + (2 * b.Slot)), Math.Sin(a.Slot + (2 * b.Slot))) : ((b.Position.X - a.Position.X) / d, (b.Position.Y - a.Position.Y) / d);
                    var push = Gap - d + 0.01;
                    var shareA = fa ? 0 : fb ? 1 : 0.5;
                    players[i] = a with { Position = CourtTracker.Clamp(new CourtPoint(a.Position.X - (dx * push * shareA), a.Position.Y - (dy * push * shareA))) };
                    players[j] = b with { Position = CourtTracker.Clamp(new CourtPoint(b.Position.X + (dx * push * (1 - shareA)), b.Position.Y + (dy * push * (1 - shareA)))) };
                    moved = true;
                }
            }

            if (!moved)
            {
                return;
            }
        }
    }

    /// <summary>Distance of a point from the baseline <paramref name="offense"/> attacks.</summary>
    public static double Depth(TeamSide offense, int period, CourtPoint p) => CourtTracker.AttacksRight(offense, period) ? CourtTracker.Length - p.X : p.X;
}
