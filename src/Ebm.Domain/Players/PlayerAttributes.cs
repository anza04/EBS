using Ebm.Domain.Common;

namespace Ebm.Domain.Players;

/// <summary>
/// A player's core ability ratings, each from <see cref="MinRating"/> to <see cref="MaxRating"/>.
/// </summary>
/// <remarks>
/// Initial set. Step 05 (player model and attributes) finalises the attribute catalogue and may
/// add attributes through a migration.
/// </remarks>
public sealed class PlayerAttributes
{
    public const int MinRating = 1;
    public const int MaxRating = 100;

    public PlayerAttributes(
        int shooting,
        int insideScoring,
        int passing,
        int ballHandling,
        int rebounding,
        int defense,
        int athleticism,
        int basketballIq)
    {
        Shooting = Rating(shooting, nameof(Shooting));
        InsideScoring = Rating(insideScoring, nameof(InsideScoring));
        Passing = Rating(passing, nameof(Passing));
        BallHandling = Rating(ballHandling, nameof(BallHandling));
        Rebounding = Rating(rebounding, nameof(Rebounding));
        Defense = Rating(defense, nameof(Defense));
        Athleticism = Rating(athleticism, nameof(Athleticism));
        BasketballIq = Rating(basketballIq, nameof(BasketballIq));
    }

    private PlayerAttributes()
    {
    }

    public int Shooting { get; private set; }

    public int InsideScoring { get; private set; }

    public int Passing { get; private set; }

    public int BallHandling { get; private set; }

    public int Rebounding { get; private set; }

    public int Defense { get; private set; }

    public int Athleticism { get; private set; }

    public int BasketballIq { get; private set; }

    private static int Rating(int value, string name) => Guard.Range(value, MinRating, MaxRating, name);
}
