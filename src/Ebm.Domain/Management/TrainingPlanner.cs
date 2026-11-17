using Ebm.Domain.Development;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;

namespace Ebm.Domain.Management;

/// <summary>The training programme the AI coach chooses for a squad.</summary>
public sealed record TrainingProgramme(TrainingFocus TeamFocus, TrainingIntensity Intensity, IReadOnlyList<IndividualTraining> Individuals, string Reason);

/// <summary>
/// The AI coach's training choice (docs/AI_MANAGEMENT.md §3, docs/DEVELOPMENT.md §6): the team works on the rotation's
/// weakest area, intensity follows the squad's fitness and age, young players with room to grow work on their own
/// weakest area, veterans on their physique and tired players rest. Injured players get no programme, players with a knock
/// rest, and a squad with a quarter of its players injured never trains hard (docs/INJURIES.md §5). Pure and deterministic.
/// </summary>
public static class TrainingPlanner
{
    /// <summary>The areas a team or player can focus on (not Balanced or Recovery).</summary>
    public static readonly IReadOnlyList<TrainingFocus> Areas = [TrainingFocus.Shooting, TrainingFocus.Tactical, TrainingFocus.Defense, TrainingFocus.Physical];

    /// <summary>The weakest area must trail the mean of the areas by this much to become the team focus.</summary>
    public const int Margin = 3;

    /// <summary>Players up to this age with at least <see cref="GrowthGap"/> points to potential get an individual programme.</summary>
    public const int YoungAge = 23;

    public const int GrowthGap = 5;

    /// <summary>Players from this age work on their physique.</summary>
    public const int VeteranAge = 31;

    /// <summary>Players below this fitness rest.</summary>
    public const int TiredFitness = 70;

    /// <summary>With at least this share of the squad injured, the coach does not train hard.</summary>
    public const double InjuryCrisisShare = 0.25;

    public static TrainingProgramme Plan(IReadOnlyList<(Player Player, PlayerEvaluation Evaluation)> squad, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(squad);
        if (squad.Count == 0)
        {
            return new TrainingProgramme(TrainingFocus.Balanced, TrainingIntensity.Normal, [], "No players: balanced training.");
        }

        var rotation = squad.OrderByDescending(p => p.Evaluation.Overall).ThenBy(p => p.Player.Id.Value).Take(TacticPlanner.RotationSize).ToList();
        var areas = Areas.ToDictionary(a => a, a => rotation.Average(p => AreaRating(p.Player.Attributes, a)));
        var weakest = areas.OrderBy(a => a.Value).ThenBy(a => a.Key).First();
        var teamFocus = weakest.Value <= areas.Values.Average() - Margin ? weakest.Key : TrainingFocus.Balanced;

        var fit = squad.Where(p => p.Player.InjuryStatus != InjuryStatus.Injured).ToList();
        var injured = squad.Count - fit.Count;
        var fitness = (fit.Count > 0 ? fit : squad).Average(p => p.Player.Fitness);
        var ages = squad.Select(p => p.Player.AgeOn(today)).Order().ToList();
        var intensity = fitness < 85 ? TrainingIntensity.Light
            : fitness >= 95 && ages[ages.Count / 2] <= 24 && injured < squad.Count * InjuryCrisisShare ? TrainingIntensity.Hard
            : TrainingIntensity.Normal;

        var individuals = new List<IndividualTraining>();
        foreach (var (player, evaluation) in squad.OrderBy(p => p.Player.Id.Value).Take(TrainingPlan.MaxIndividuals))
        {
            if (player.InjuryStatus == InjuryStatus.Injured)
            {
                continue; // The medical staff decides what an injured player does.
            }

            var age = player.AgeOn(today);
            TrainingFocus? focus = player.Fitness < TiredFitness || player.InjuryStatus == InjuryStatus.DayToDay ? TrainingFocus.Recovery
                : age >= VeteranAge ? TrainingFocus.Physical
                : age <= YoungAge && evaluation.PotentialRating - evaluation.Overall >= GrowthGap
                    ? Areas.OrderBy(a => AreaRating(player.Attributes, a)).ThenBy(a => a).First()
                    : null;
            if (focus is { } chosen && chosen != teamFocus)
            {
                individuals.Add(new IndividualTraining(player.Id, chosen));
            }
        }

        return new TrainingProgramme(
            teamFocus,
            intensity,
            individuals,
            $"Team: {(teamFocus == TrainingFocus.Balanced ? "balanced" : $"weakest area {teamFocus}")}; {intensity} (fitness {fitness:0}, median age {ages[ages.Count / 2]}, {injured} injured); {individuals.Count} individual programmes.");
    }

    /// <summary>Average of the attributes an area trains.</summary>
    public static double AreaRating(PlayerAttributes attributes, TrainingFocus area)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        return TrainingFocuses.AttributesOf(area).Average(a => attributes[a]);
    }
}
