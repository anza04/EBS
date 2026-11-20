using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Staff;
using Ebm.Domain.Tactics;

namespace Ebm.Domain.Management;

/// <summary>The tactic the AI coach chooses for a squad.</summary>
public sealed record TacticPlan(
    OffensiveSystem OffensiveSystem,
    OffensiveSettings Offense,
    DefensiveSystem DefensiveSystem,
    DefensiveSettings Defense,
    IReadOnlyList<TacticalAssignment> Assignments,
    string Reason)
{
    /// <summary>Game management (Step 27): the AI coach adapts to the opponent, plays two-for-ones and sizes the rotation to the bench.</summary>
    public SituationSettings Situations { get; init; } = SituationSettings.Default;
}

/// <summary>
/// The AI coach's tactical choice (docs/AI_MANAGEMENT.md §3): the squad's strongest category among its playing
/// rotation picks the offensive system, its defensive profile the defence; every rotation player gets his suggested
/// role and the three best players a higher shot priority. The head coach (Step 28, docs/STAFF.md §5) plays his
/// preferred systems where the squad does not point elsewhere, and his coaching style adjusts the settings. Pure and
/// deterministic.
/// </summary>
public static class TacticPlanner
{
    /// <summary>Players considered: the starting five and the rotation.</summary>
    public const int RotationSize = 9;

    /// <summary>A category must beat the squad's mean category rating by this much to shape the system.</summary>
    public const int Margin = 2;

    public static TacticPlan Plan(IReadOnlyList<(Player Player, PlayerEvaluation Evaluation)> squad, StaffMember? headCoach = null)
    {
        ArgumentNullException.ThrowIfNull(squad);
        var rotation = squad.OrderByDescending(p => p.Evaluation.Overall).ThenBy(p => p.Player.Id.Value).Take(RotationSize).ToList();
        if (rotation.Count == 0)
        {
            return new TacticPlan(OffensiveSystem.MotionOffense, OffensiveSettings.Default, DefensiveSystem.ManToMan, DefensiveSettings.Default, [], "No players: default tactic.");
        }

        int Category(AttributeCategory c) => (int)Math.Round(rotation.Average(p => p.Evaluation.Categories[c]));
        var shooting = Category(AttributeCategory.Shooting);
        var finishing = Category(AttributeCategory.Finishing);
        var playmaking = Category(AttributeCategory.Playmaking);
        var defense = Category(AttributeCategory.Defense);
        var rebounding = Category(AttributeCategory.Rebounding);
        var physical = Category(AttributeCategory.Physical);
        var mean = (shooting + finishing + playmaking + defense + rebounding + physical) / 6;

        var (system, focus, why) = new[]
            {
                (Value: shooting, System: OffensiveSystem.FiveOut, Focus: OffensiveFocus.Perimeter, Why: "shooters"),
                (Value: finishing, System: OffensiveSystem.PostOriented, Focus: OffensiveFocus.Inside, Why: "finishers"),
                (Value: playmaking, System: OffensiveSystem.PickAndRoll, Focus: OffensiveFocus.Balanced, Why: "playmakers"),
                (Value: physical, System: OffensiveSystem.Transition, Focus: OffensiveFocus.Balanced, Why: "athletes"),
            }
            .Where(c => c.Value >= mean + Margin)
            .OrderByDescending(c => c.Value)
            .Select(c => (c.System, c.Focus, c.Why))
            .DefaultIfEmpty(headCoach?.PreferredOffense is { } preferred
                ? (preferred, preferred == OffensiveSystem.PostOriented ? OffensiveFocus.Inside : preferred == OffensiveSystem.FiveOut ? OffensiveFocus.Perimeter : OffensiveFocus.Balanced, "the coach's preference")
                : (OffensiveSystem.MotionOffense, OffensiveFocus.Balanced, "a balanced squad"))
            .First();
        var offense = OffensiveSettings.Default with
        {
            Focus = focus,
            Tempo = system == OffensiveSystem.Transition ? 4 : TacticLevel.Normal,
            Spacing = system == OffensiveSystem.FiveOut ? 4 : TacticLevel.Normal,
            PickAndRollFrequency = system == OffensiveSystem.PickAndRoll ? 4 : TacticLevel.Normal,
        };

        // Strong defenders pressure the ball; a weak perimeter behind good rebounders packs a zone.
        var (defenseSystem, defenseSettings) =
            defense >= mean + Margin && physical >= mean ? (DefensiveSystem.ManToMan, DefensiveSettings.Default with { Intensity = 4, BallPressure = 4 })
            : defense <= mean - Margin && rebounding >= mean ? (DefensiveSystem.Zone23, DefensiveSettings.Default with { Rebounding = ReboundingPriority.Balanced })
            : (headCoach?.PreferredDefense ?? DefensiveSystem.ManToMan, DefensiveSettings.Default);
        var style = headCoach?.Style ?? CoachingStyle.Balanced;
        if (style == CoachingStyle.Attacking)
        {
            offense = offense with { Tempo = Math.Max(offense.Tempo, 4), Spacing = Math.Max(offense.Spacing, 4) };
        }
        else if (style == CoachingStyle.Defensive)
        {
            defenseSettings = defenseSettings with { Intensity = Math.Max(defenseSettings.Intensity, 4), BallPressure = Math.Max(defenseSettings.BallPressure, 4) };
        }

        var assignments = rotation
            .Select((p, i) => new TacticalAssignment(p.Player.Id, TacticalRoles.Suggest(p.Player), i < 3 ? 4 : TacticLevel.Normal))
            .ToList();
        // A strong bench plays a deep rotation, a weak one a tight eight-man rotation.
        var starters = rotation.Take(5).Average(p => p.Evaluation.Overall);
        var bench = rotation.Skip(5).Select(p => (double)p.Evaluation.Overall).DefaultIfEmpty(0).Average();
        var depth = bench >= starters - 4 ? RotationStrategy.Deep : bench <= starters - 12 ? RotationStrategy.Tight : RotationStrategy.Standard;
        depth = style switch
        {
            CoachingStyle.YouthDevelopment => RotationStrategy.Deep,
            CoachingStyle.Disciplinarian => RotationStrategy.Tight,
            _ => depth,
        };
        var situations = SituationSettings.Default with
        {
            CoachAdjusts = true,
            EndOfQuarter = EndOfQuarter.TwoForOne,
            Rotation = depth,
            FoulManagement = style == CoachingStyle.Disciplinarian ? FoulManagement.Protect : FoulManagement.Normal,
        };
        var coached = style == CoachingStyle.Balanced ? string.Empty : $" ({PlayerCatalog.DisplayName(style).ToLowerInvariant()} coach)";
        return new TacticPlan(system, offense, defenseSystem, defenseSettings, assignments, $"{system} for {why}, {defenseSystem} defence, {depth} rotation{coached}.")
        {
            Situations = situations,
        };
    }
}
