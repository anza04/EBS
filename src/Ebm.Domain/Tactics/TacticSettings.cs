using Ebm.Domain.Common;

namespace Ebm.Domain.Tactics;

/// <summary>The 1–5 scale of tactical sliders: 1 = very low, 3 = normal, 5 = very high.</summary>
public static class TacticLevel
{
    public const int Min = 1;
    public const int Normal = 3;
    public const int Max = 5;

    public static readonly IReadOnlyList<int> All = [1, 2, 3, 4, 5];

    /// <summary>The level as an offset from normal: -1 (level 1) to +1 (level 5).</summary>
    public static double Offset(int level) => (level - Normal) / 2.0;

    internal static int Check(int value, string name) => Guard.Range(value, Min, Max, name);
}

/// <summary>Offensive settings of a tactic. Levels use <see cref="TacticLevel"/>.</summary>
public sealed record OffensiveSettings
{
    public static readonly OffensiveSettings Default = new();

    /// <summary>Pace: 1 = use the whole shot clock, 5 = push every ball.</summary>
    public int Tempo { get; init; } = TacticLevel.Normal;

    /// <summary>1 = only the best shots, 5 = shoot whenever open.</summary>
    public int ShotSelection { get; init; } = TacticLevel.Normal;

    /// <summary>1 = few passes, 5 = the ball moves until the best shot appears.</summary>
    public int BallMovement { get; init; } = TacticLevel.Normal;

    /// <summary>1 = compact floor, 5 = shooters stay wide.</summary>
    public int Spacing { get; init; } = TacticLevel.Normal;

    public int PickAndRollFrequency { get; init; } = TacticLevel.Normal;

    public OffensiveFocus Focus { get; init; } = OffensiveFocus.Balanced;

    public void Validate()
    {
        TacticLevel.Check(Tempo, "Tempo");
        TacticLevel.Check(ShotSelection, "Shot selection");
        TacticLevel.Check(BallMovement, "Ball movement");
        TacticLevel.Check(Spacing, "Spacing");
        TacticLevel.Check(PickAndRollFrequency, "Pick-and-roll frequency");
        Guard.Defined(Focus, "Offensive focus");
    }
}

/// <summary>Defensive settings of a tactic. Levels use <see cref="TacticLevel"/>.</summary>
public sealed record DefensiveSettings
{
    public static readonly DefensiveSettings Default = new();

    /// <summary>Effort: higher forces more mistakes but costs energy and fouls.</summary>
    public int Intensity { get; init; } = TacticLevel.Normal;

    /// <summary>Pressure on the ball handler: more steals and turnovers, more blow-bys.</summary>
    public int BallPressure { get; init; } = TacticLevel.Normal;

    /// <summary>How quickly help comes off shooters to protect the rim.</summary>
    public int HelpDefense { get; init; } = TacticLevel.Normal;

    public SwitchingPolicy Switching { get; init; } = SwitchingPolicy.SameSize;

    public PickAndRollCoverage PickAndRollCoverage { get; init; } = PickAndRollCoverage.Drop;

    public ReboundingPriority Rebounding { get; init; } = ReboundingPriority.Balanced;

    public void Validate()
    {
        TacticLevel.Check(Intensity, "Defensive intensity");
        TacticLevel.Check(BallPressure, "Ball pressure");
        TacticLevel.Check(HelpDefense, "Help defense");
        Guard.Defined(Switching, "Switching");
        Guard.Defined(PickAndRollCoverage, "Pick-and-roll coverage");
        Guard.Defined(Rebounding, "Rebounding priority");
    }
}
