using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Tactics;

namespace Ebm.Domain.Management;

/// <summary>The tactic the AI coach chooses for a squad.</summary>
public sealed record TacticPlan(
    OffensiveSystem OffensiveSystem,
    OffensiveSettings Offense,
    DefensiveSystem DefensiveSystem,
    DefensiveSettings Defense,
    IReadOnlyList<TacticalAssignment> Assignments,
    string Reason);

/// <summary>
/// The AI coach's tactical choice (docs/AI_MANAGEMENT.md §3): the squad's strongest category among its playing
/// rotation picks the offensive system, its defensive profile the defence; every rotation player gets his suggested
/// role and the three best players a higher shot priority. Pure and deterministic.
/// </summary>
public static class TacticPlanner
{
    /// <summary>Players considered: the starting five and the rotation.</summary>
    public const int RotationSize = 9;

    /// <summary>A category must beat the squad's mean category rating by this much to shape the system.</summary>
    public const int Margin = 2;

    public static TacticPlan Plan(IReadOnlyList<(Player Player, PlayerEvaluation Evaluation)> squad)
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
            .DefaultIfEmpty((OffensiveSystem.MotionOffense, OffensiveFocus.Balanced, "a balanced squad"))
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
            : (DefensiveSystem.ManToMan, DefensiveSettings.Default);

        var assignments = rotation
            .Select((p, i) => new TacticalAssignment(p.Player.Id, TacticalRoles.Suggest(p.Player), i < 3 ? 4 : TacticLevel.Normal))
            .ToList();
        return new TacticPlan(system, offense, defenseSystem, defenseSettings, assignments, $"{system} for {why}, {defenseSystem} defence.");
    }
}
