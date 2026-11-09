using Ebm.Domain.Common;

namespace Ebm.Domain.Matches;

/// <summary>A player's counting statistics in one match. Points are derived, never stored.</summary>
public sealed class BoxScore
{
    public const int MaxMinutes = 100;
    public const int MaxCount = 200;

    public int Minutes { get; init; }

    public int TwoPointMade { get; init; }

    public int TwoPointAttempted { get; init; }

    public int ThreePointMade { get; init; }

    public int ThreePointAttempted { get; init; }

    public int FreeThrowMade { get; init; }

    public int FreeThrowAttempted { get; init; }

    public int OffensiveRebounds { get; init; }

    public int DefensiveRebounds { get; init; }

    public int Assists { get; init; }

    public int Steals { get; init; }

    public int Blocks { get; init; }

    public int Turnovers { get; init; }

    public int PersonalFouls { get; init; }

    public int Points => (2 * TwoPointMade) + (3 * ThreePointMade) + FreeThrowMade;

    public int Rebounds => OffensiveRebounds + DefensiveRebounds;

    /// <summary>Checks ranges and that no shot type has more makes than attempts.</summary>
    public void Validate()
    {
        Guard.Range(Minutes, 0, MaxMinutes, nameof(Minutes));
        Shots(TwoPointMade, TwoPointAttempted, "Two-point");
        Shots(ThreePointMade, ThreePointAttempted, "Three-point");
        Shots(FreeThrowMade, FreeThrowAttempted, "Free-throw");
        foreach (var (value, name) in new[]
        {
            (OffensiveRebounds, nameof(OffensiveRebounds)),
            (DefensiveRebounds, nameof(DefensiveRebounds)),
            (Assists, nameof(Assists)),
            (Steals, nameof(Steals)),
            (Blocks, nameof(Blocks)),
            (Turnovers, nameof(Turnovers)),
            (PersonalFouls, nameof(PersonalFouls)),
        })
        {
            Guard.Range(value, 0, MaxCount, name);
        }
    }

    private static void Shots(int made, int attempted, string name)
    {
        Guard.Range(attempted, 0, MaxCount, $"{name} attempts");
        Guard.Range(made, 0, attempted, $"{name} makes");
    }
}
