using Ebm.Domain.Players;

namespace Ebm.Domain.Tactics;

/// <summary>Role suggestions and role metadata (docs/TACTICS.md §4).</summary>
public static class TacticalRoles
{
    public static readonly IReadOnlyList<TacticalRole> All = Enum.GetValues<TacticalRole>();

    /// <summary>Roles that fit a position; the first one is the most common.</summary>
    public static IReadOnlyList<TacticalRole> TypicalFor(Position position) => position switch
    {
        Position.PointGuard => [TacticalRole.PrimaryBallHandler, TacticalRole.ShotCreator, TacticalRole.PerimeterStopper],
        Position.ShootingGuard => [TacticalRole.SpotUpShooter, TacticalRole.ShotCreator, TacticalRole.Slasher, TacticalRole.PerimeterStopper],
        Position.SmallForward => [TacticalRole.Slasher, TacticalRole.SpotUpShooter, TacticalRole.PerimeterStopper, TacticalRole.ShotCreator],
        Position.PowerForward => [TacticalRole.StretchBig, TacticalRole.Rebounder, TacticalRole.PostScorer, TacticalRole.RollMan],
        _ => [TacticalRole.RimProtector, TacticalRole.RollMan, TacticalRole.PostScorer, TacticalRole.Rebounder],
    };

    /// <summary>How well the player's attributes fit the role, 1–100.</summary>
    public static int Fit(PlayerAttributes a, TacticalRole role)
    {
        ArgumentNullException.ThrowIfNull(a);
        return role switch
        {
            TacticalRole.PrimaryBallHandler => Mean(a.BallHandling, a.Passing, a.CourtVision, a.DecisionMaking),
            TacticalRole.ShotCreator => Mean(a.MidRange, a.BallHandling, a.ThreePoint, a.Agility),
            TacticalRole.SpotUpShooter => Mean(a.ThreePoint, a.ThreePoint, a.MidRange, a.Consistency),
            TacticalRole.Slasher => Mean(a.Layup, a.Dunk, a.Speed, a.BallHandling),
            TacticalRole.PostScorer => Mean(a.PostMoves, a.PostMoves, a.CloseShot, a.Strength),
            TacticalRole.RollMan => Mean(a.Dunk, a.CloseShot, a.Vertical, a.Strength),
            TacticalRole.StretchBig => Mean(a.ThreePoint, a.ThreePoint, a.MidRange, a.DefensiveRebound),
            TacticalRole.RimProtector => Mean(a.Block, a.Block, a.InteriorDefense, a.Vertical),
            TacticalRole.PerimeterStopper => Mean(a.PerimeterDefense, a.PerimeterDefense, a.Steal, a.Agility),
            TacticalRole.Rebounder => Mean(a.DefensiveRebound, a.OffensiveRebound, a.Strength, a.Vertical),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role."),
        };
    }

    /// <summary>The best-fitting role among those typical for the player's primary position.</summary>
    public static TacticalRole Suggest(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var roles = TypicalFor(player.Position);
        var best = roles[0];
        foreach (var role in roles.Skip(1))
        {
            // The position's usual role wins ties and small differences.
            if (Fit(player.Attributes, role) > Fit(player.Attributes, best) + 3)
            {
                best = role;
            }
        }

        return best;
    }

    private static int Mean(params int[] values) => (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero);
}
