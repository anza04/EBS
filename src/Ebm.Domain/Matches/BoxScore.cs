using Ebm.Domain.Common;

namespace Ebm.Domain.Matches;

/// <summary>A player's counting statistics in one match. Points are derived, never stored.</summary>
public sealed class BoxScore
{
    public const int MaxMinutes = 100;
    public const int MaxCount = 200;
    public const int MaxOnCourt = 500;

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

    /// <summary>Team points scored while the player was on court (Step 29; 0 in older box scores).</summary>
    public int PointsFor { get; init; }

    /// <summary>Opponent points scored while the player was on court.</summary>
    public int PointsAgainst { get; init; }

    /// <summary>Team possessions played on offense while the player was on court.</summary>
    public int OffensivePossessions { get; init; }

    /// <summary>Opponent possessions defended while the player was on court.</summary>
    public int DefensivePossessions { get; init; }

    /// <summary>Field goals attempted against the player as the primary defender (individual matchups).</summary>
    public int DefendedAttempted { get; init; }

    /// <summary>Field goals made against the player as the primary defender.</summary>
    public int DefendedMade { get; init; }

    /// <summary>The player's field-goal attempts (<see cref="ShotChart"/> encoding); <c>null</c> before Step 29.</summary>
    public string? ShotData { get; init; }

    public int PlusMinus => PointsFor - PointsAgainst;

    public IReadOnlyList<ShotRecord> Shots => ShotChart.Decode(ShotData);

    public int Points => (2 * TwoPointMade) + (3 * ThreePointMade) + FreeThrowMade;

    public int Rebounds => OffensiveRebounds + DefensiveRebounds;

    /// <summary>Checks ranges and that no shot type has more makes than attempts.</summary>
    public void Validate()
    {
        Guard.Range(Minutes, 0, MaxMinutes, nameof(Minutes));
        Attempts(TwoPointMade, TwoPointAttempted, "Two-point");
        Attempts(ThreePointMade, ThreePointAttempted, "Three-point");
        Attempts(FreeThrowMade, FreeThrowAttempted, "Free-throw");
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

        foreach (var (value, name) in new[]
        {
            (PointsFor, nameof(PointsFor)),
            (PointsAgainst, nameof(PointsAgainst)),
            (OffensivePossessions, nameof(OffensivePossessions)),
            (DefensivePossessions, nameof(DefensivePossessions)),
        })
        {
            Guard.Range(value, 0, MaxOnCourt, name);
        }

        Attempts(DefendedMade, DefendedAttempted, "Defended field-goal");
        if (ShotData is not null)
        {
            // The shot chart must match the recorded attempts exactly.
            var shots = ShotChart.Decode(ShotData);
            if (shots.Count(s => !s.IsThree) != TwoPointAttempted || shots.Count(s => !s.IsThree && s.Made) != TwoPointMade
                || shots.Count(s => s.IsThree) != ThreePointAttempted || shots.Count(s => s.IsThree && s.Made) != ThreePointMade)
            {
                throw new DomainValidationException("The shot chart does not match the field goals of the box score.");
            }
        }
    }

    private static void Attempts(int made, int attempted, string name)
    {
        Guard.Range(attempted, 0, MaxCount, $"{name} attempts");
        Guard.Range(made, 0, attempted, $"{name} makes");
    }
}
