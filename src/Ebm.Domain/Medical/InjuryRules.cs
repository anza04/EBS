using Ebm.Domain.Development;
using Ebm.Domain.Players;
using Ebm.Domain.Scouting;

namespace Ebm.Domain.Medical;

/// <summary>Everything a player's injury risk depends on (docs/INJURIES.md §2).</summary>
/// <param name="Fitness">Fitness before the match or training session (0–100).</param>
/// <param name="MinutesThisWeek">Minutes played in the last seven days (workload).</param>
/// <param name="PhysioAbility">Best physio of the club (1–100; <see cref="TrainingConditions.MissingCoachAbility"/> without one).</param>
/// <param name="RecentInjuries">Injuries in the recurrence window.</param>
/// <param name="RecentlyReturned">Back from an injury for less than <see cref="InjuryRules.ReturnWindowDays"/> days.</param>
/// <param name="PlayingHurt">Plays or trains with a minor injury (day-to-day).</param>
/// <param name="Proneness">The player's own injury proneness (<see cref="InjuryRules.Proneness"/>).</param>
public sealed record InjuryRiskFactors(
    int Age,
    int Fitness,
    int MinutesThisWeek,
    int Strength,
    int Stamina,
    int PhysioAbility,
    int RecentInjuries,
    bool RecentlyReturned,
    bool PlayingHurt,
    double Proneness);

/// <summary>
/// Injury mechanics (docs/INJURIES.md): risk, type and recovery time, medical assessment, recovery speed and
/// return-to-play fitness. Pure: randomness comes from the caller's seeded <see cref="Random"/>. The rates are
/// configurable (settings section <c>Ebm:Injuries</c>); registered as a singleton.
/// </summary>
public sealed class InjuryRules
{
    public static InjuryRules Default { get; } = new();

    /// <summary>Scales every injury chance; 0 switches injuries off.</summary>
    public double Frequency { get; init; } = 1.0;

    /// <summary>Chance of an injury per minute played, for an average player at average risk.</summary>
    public double MatchRiskPerMinute { get; init; } = 0.0011;

    /// <summary>Chance of an injury per day of normal team training, at average risk.</summary>
    public double TrainingRiskPerDay { get; init; } = 0.0012;

    /// <summary>Earlier injuries count for recurrence within this many days.</summary>
    public int RecurrenceWindowDays { get; init; } = 365;

    /// <summary>A recurring injury needs this much longer to heal.</summary>
    public double RecurrenceRecoveryFactor { get; init; } = 1.25;

    /// <summary>Players back from an injury for less than this many days are at higher risk.</summary>
    public int ReturnWindowDays { get; init; } = 21;

    /// <summary>The player's own injury proneness, 0.7–1.3: stable for his whole career.</summary>
    public static double Proneness(PlayerId playerId) =>
        0.7 + (0.6 * new Random(ScoutingRules.StableSeed(playerId.Value, Guid.Empty, 0x1A7)).NextDouble());

    /// <summary>
    /// How much more (or less) likely than average an injury is: fatigue (below 80 fitness, up to ×2 at 40), workload
    /// (above 100 minutes a week), age (+4 % a year from 30), strength and stamina (×1.3 at 0 to ×0.7 at 100), the physio
    /// (×1.1 to ×0.9), recent injuries (+25 % each), a recent return (×1.5), playing hurt (×1.5) and proneness.
    /// </summary>
    public static double RiskMultiplier(InjuryRiskFactors factors)
    {
        ArgumentNullException.ThrowIfNull(factors);
        var fatigue = 1 + (Math.Max(0, 80 - factors.Fitness) / 40.0);
        var workload = 1 + (Math.Max(0, factors.MinutesThisWeek - 100) / 150.0);
        var age = 1 + (Math.Max(0, factors.Age - 29) * 0.04);
        var body = 1.3 - (0.6 * ((factors.Strength + factors.Stamina) / 2.0) / 100);
        var physio = 1.1 - (0.2 * Math.Clamp(factors.PhysioAbility, 1, 100) / 100);
        var history = 1 + (0.25 * Math.Max(0, factors.RecentInjuries));
        return fatigue * workload * age * body * physio * history
            * (factors.RecentlyReturned ? 1.5 : 1)
            * (factors.PlayingHurt ? 1.5 : 1)
            * factors.Proneness;
    }

    /// <summary>The chance (0–1) of an injury in a match in which the player played <paramref name="minutes"/>.</summary>
    public double MatchChance(int minutes, InjuryRiskFactors factors) =>
        minutes <= 0 ? 0 : 1 - Math.Exp(-MatchRiskPerMinute * Frequency * minutes * RiskMultiplier(factors));

    /// <summary>The chance (0–1) of an injury in one day of training: light ×0.5, hard ×1.8; individual light work ×0.5.</summary>
    public double TrainingChance(TrainingIntensity intensity, bool lightWorkOnly, InjuryRiskFactors factors)
    {
        var load = lightWorkOnly ? 0.5 : intensity switch
        {
            TrainingIntensity.Light => 0.5,
            TrainingIntensity.Hard => 1.8,
            _ => 1.0,
        };
        return Math.Min(1, TrainingRiskPerDay * Frequency * load * RiskMultiplier(factors));
    }

    /// <summary>
    /// The type of a new injury: by frequency, contact injuries rarer in training, body parts injured recently twice as
    /// likely (recurrence).
    /// </summary>
    public static InjuryDefinition ChooseType(InjurySource source, IReadOnlySet<BodyPart> recentParts, Random random)
    {
        ArgumentNullException.ThrowIfNull(recentParts);
        ArgumentNullException.ThrowIfNull(random);
        var weights = InjuryCatalog.All
            .Select(d => (Definition: d, Weight: d.Weight
                * (source == InjurySource.Training && d.Contact ? InjuryCatalog.TrainingContactFactor : 1)
                * (recentParts.Contains(d.BodyPart) ? 2 : 1)))
            .ToList();
        var roll = random.NextDouble() * weights.Sum(w => w.Weight);
        foreach (var (definition, weight) in weights)
        {
            roll -= weight;
            if (roll < 0)
            {
                return definition;
            }
        }

        return weights[^1].Definition;
    }

    /// <summary>Recovery days: within the type's range, short ones more likely (min + range × u²); recurrences take longer.</summary>
    public int RecoveryDays(InjuryDefinition definition, bool recurrence, Random random)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(random);
        var u = random.NextDouble();
        var days = definition.MinDays + ((definition.MaxDays - definition.MinDays) * u * u);
        return Math.Clamp((int)Math.Round(days * (recurrence ? RecurrenceRecoveryFactor : 1), MidpointRounding.AwayFromZero), 1, 400);
    }

    /// <summary>Days of recovery a player makes per day: the physio gives 0.85 (ability 0) to 1.15 (100); from 29, −1.5 % a year (at least 0.7).</summary>
    public static double RecoverySpeed(int physioAbility, int age) =>
        (0.85 + (0.3 * Math.Clamp(physioAbility, 1, 100) / 100.0)) * Math.Max(0.7, 1 - (Math.Max(0, age - 28) * 0.015));

    /// <summary>
    /// Medical assessment: the estimated return date. The physio misjudges the remaining time by up to
    /// ±(50 % − ability / 2.5 %), so ±10 % at ability 100 and ±38 % at 30.
    /// </summary>
    public static DateOnly EstimateReturn(DateOnly today, double remainingDays, double speed, int physioAbility, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var error = 0.5 - (Math.Clamp(physioAbility, 1, 100) / 250.0);
        var estimate = remainingDays / Math.Max(0.1, speed) * (1 + (error * ((2 * random.NextDouble()) - 1)));
        return today.AddDays(Math.Max(1, (int)Math.Ceiling(estimate)));
    }

    /// <summary>Fitness a player has on his return: 100 − 0.6 per day out, at least 55 (match sharpness is lost).</summary>
    public static int ReturnFitness(int daysOut) => Math.Clamp((int)Math.Round(100 - (0.6 * daysOut), MidpointRounding.AwayFromZero), 55, Player.MaxFitness);

    /// <summary>Extra recovery days when a player makes a minor injury worse: 2–6.</summary>
    public static int AggravationDays(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return random.Next(2, 7);
    }

    /// <summary>Checks the settings: frequency 0–5, risks 0–0.05, windows 1–1,000 days, recurrence factor 1–3.</summary>
    public InjuryRules Validated()
    {
        if (Frequency is < 0 or > 5)
        {
            throw new InvalidOperationException($"Injury frequency must be between 0 and 5 (got {Frequency}).");
        }

        if (MatchRiskPerMinute is < 0 or > 0.05 || TrainingRiskPerDay is < 0 or > 0.05)
        {
            throw new InvalidOperationException("Injury risks per minute and per training day must be between 0 and 0.05.");
        }

        if (RecurrenceWindowDays is < 1 or > 1000 || ReturnWindowDays is < 1 or > 1000 || RecurrenceRecoveryFactor is < 1 or > 3)
        {
            throw new InvalidOperationException("Injury windows must be 1–1,000 days and the recurrence recovery factor 1–3.");
        }

        return this;
    }
}
