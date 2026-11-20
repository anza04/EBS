using Ebm.Domain.Players;
using Ebm.Domain.Staff;

namespace Ebm.Domain.Development;

/// <summary>The training a player gets: his club's programme and staff (docs/DEVELOPMENT.md §2).</summary>
/// <param name="CoachAbility">Average ability of the club's coaches (1–100).</param>
/// <param name="FitnessCoachAbility">Ability of the club's fitness coach (1–100).</param>
public sealed record TrainingConditions(TrainingFocus TeamFocus, TrainingFocus? IndividualFocus, TrainingIntensity Intensity, int CoachAbility, int FitnessCoachAbility)
{
    /// <summary>Ability assumed for a missing coach.</summary>
    public const int MissingCoachAbility = 30;

    /// <summary>A player without a club trains alone: no focus, light, no coaches.</summary>
    public static TrainingConditions Alone { get; } = new(TrainingFocus.Balanced, null, TrainingIntensity.Light, MissingCoachAbility, MissingCoachAbility);

    /// <summary>The head coach's coaching style (Step 28, docs/STAFF.md §5): a growth multiplier by age.</summary>
    public CoachingStyle Style { get; init; } = CoachingStyle.Balanced;

    public bool Focuses(TrainingFocus focus) => TeamFocus == focus || IndividualFocus == focus;
}

/// <summary>Everything one week of development depends on.</summary>
/// <param name="PlayingTimeShare">Share of his club's minutes the player played in the last four weeks (0–1); <c>null</c> when the club played no games.</param>
/// <param name="MinutesThisWeek">Minutes played in the last seven days (experience).</param>
/// <param name="DivisionLevel">Level of the club's league division (1 = top); <c>null</c> without a club.</param>
/// <param name="International">Whether the club plays an international competition this season.</param>
public sealed record DevelopmentInput(
    int Age,
    int Overall,
    int Potential,
    PlayerAttributes Attributes,
    int Fitness,
    InjuryStatus Injury,
    double? PlayingTimeShare,
    int MinutesThisWeek,
    TrainingConditions Training,
    int? DivisionLevel,
    bool International);

/// <summary>One week of development: the new attributes and the experience gained.</summary>
public sealed record DevelopmentStep(PlayerAttributes Attributes, int ExperienceGain, int LeadershipGain);

/// <summary>
/// Player progression (docs/DEVELOPMENT.md): growth towards potential, age-related decline, potential realisation,
/// fitness recovery. Pure: randomness comes from the caller's seeded <see cref="Random"/>. Registered as a singleton.
/// </summary>
public sealed class PlayerDevelopment
{
    public const int WeeksPerYear = 52;

    /// <summary>Minutes the club plays per game.</summary>
    public const int GameMinutes = 40;

    public static PlayerDevelopment Default { get; } = new();

    /// <summary>Extra weight of an attribute covered by the team or individual focus (before normalising).</summary>
    public double FocusWeight { get; init; } = 0.75;

    /// <summary>Focused attributes decline at this share of the normal rate.</summary>
    public double FocusedDecline { get; init; } = 0.7;

    /// <summary>Fitness lost per minute played.</summary>
    public double FatiguePerMinute { get; init; } = 0.25;

    /// <summary>Experience points per minute played (times the competition factor).</summary>
    public double ExperiencePerMinute { get; init; } = 1 / 600.0;

    /// <summary>Chance of one leadership point per experience point gained.</summary>
    public double LeadershipPerExperience { get; init; } = 0.25;

    /// <summary>Share of the gap to potential a player closes in a year at a given age (before the multipliers).</summary>
    public static double GrowthShare(int age) => age switch
    {
        <= 18 => 0.32,
        <= 20 => 0.28,
        <= 22 => 0.22,
        <= 24 => 0.14,
        <= 26 => 0.08,
        <= 28 => 0.04,
        _ => 0,
    };

    /// <summary>Overall points lost per year to age (before professionalism and luck).</summary>
    public static double DeclinePerYear(int age) => age switch
    {
        <= 29 => 0,
        30 => 0.5,
        31 => 1.0,
        32 => 1.5,
        33 => 2.0,
        34 => 2.6,
        35 => 3.2,
        _ => 4.0 + (0.5 * (age - 36)),
    };

    /// <summary>Relative age decline of an attribute: athleticism goes first, shooting and the mind last.</summary>
    public static double DeclineWeight(PlayerAttribute attribute) => attribute switch
    {
        PlayerAttribute.Concentration or PlayerAttribute.Consistency or PlayerAttribute.Leadership or PlayerAttribute.WorkRate => 0,
        _ => PlayerCatalog.CategoryOf(attribute) switch
        {
            AttributeCategory.Physical => 1.6,
            AttributeCategory.Finishing => 1.2,
            AttributeCategory.Defense => 1.1,
            AttributeCategory.Rebounding => 1.0,
            AttributeCategory.Playmaking => 0.7,
            _ => 0.5,
        },
    };

    /// <summary>Coaching: 0.7 (no ability) to 1.3 (ability 100).</summary>
    public static double CoachingFactor(int coachAbility) => 0.7 + (0.6 * Math.Clamp(coachAbility, StaffMember.MinAbility, StaffMember.MaxAbility) / 100.0);

    public static double IntensityFactor(TrainingIntensity intensity) => intensity switch
    {
        TrainingIntensity.Light => 0.8,
        TrainingIntensity.Hard => 1.15,
        _ => 1.0,
    };

    /// <summary>Playing time: 0.85 without minutes to 1.25 from half of the club's minutes; 1 when the club did not play.</summary>
    public static double PlayingTimeFactor(double? share) => share is { } s ? 0.85 + (0.8 * Math.Clamp(s, 0, 0.5)) : 1.0;

    /// <summary>Competition level: 1.10 in a top division, 1.00 in a second, 0.90 below, 0.80 without a club; +0.05 internationally.</summary>
    public static double CompetitionFactor(int? divisionLevel, bool international) =>
        (divisionLevel switch
        {
            null => 0.8,
            1 => 1.1,
            2 => 1.0,
            _ => 0.9,
        }) + (international ? 0.05 : 0);

    /// <summary>Professionalism (work rate): 0.75 to 1.25 on growth.</summary>
    public static double ProfessionalismFactor(int workRate) => 0.75 + (0.5 * workRate / 100.0);

    /// <summary>Professionalism slows decline: 1.25 (work rate 0) to 0.75 (100).</summary>
    public static double DeclineProfessionalismFactor(int workRate) => 1.25 - (0.5 * workRate / 100.0);

    /// <summary>Condition: injured players do not develop, day-to-day ones half; low fitness (below 60) costs 20 %.</summary>
    public static double ConditionFactor(InjuryStatus injury, int fitness) =>
        injury switch
        {
            InjuryStatus.Injured => 0,
            InjuryStatus.DayToDay => 0.5,
            _ => 1.0,
        } * (fitness < 60 ? 0.8 : 1.0);

    /// <summary>
    /// A player's development luck for one season (0.6–1.4): the same all season, different every season, so equal
    /// players develop differently and potential is not always realised.
    /// </summary>
    public static double SeasonLuck(PlayerId playerId, int year) =>
        0.6 + (0.8 * new Random(Scouting.ScoutingRules.StableSeed(playerId.Value, Guid.Empty, year)).NextDouble());

    /// <summary>Expected overall growth per year from training, before decline and luck.</summary>
    public double GrowthPerYear(DevelopmentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var gap = input.Potential - input.Overall;
        if (gap <= 0)
        {
            return 0;
        }

        var training = input.Training;
        var recovery = training.Focuses(TrainingFocus.Recovery) ? 0.85 : 1.0;
        return gap * GrowthShare(input.Age)
            * CoachingFactor(training.CoachAbility)
            * StaffRules.StyleGrowthFactor(training.Style, input.Age)
            * IntensityFactor(training.Intensity)
            * recovery
            * PlayingTimeFactor(input.PlayingTimeShare)
            * CompetitionFactor(input.DivisionLevel, input.International)
            * ProfessionalismFactor(input.Attributes.WorkRate)
            * ConditionFactor(input.Injury, input.Fitness);
    }

    /// <summary>Expected overall decline per year from age.</summary>
    public static double DeclineFor(DevelopmentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return DeclinePerYear(input.Age) * DeclineProfessionalismFactor(input.Attributes.WorkRate);
    }

    /// <summary>Expected change of the overall rating over a year at average luck (the training screen's outlook).</summary>
    public double ExpectedYearlyChange(DevelopmentInput input) => GrowthPerYear(input) - DeclineFor(input);

    /// <summary>
    /// One week: every attribute moves by its share of the yearly growth and decline, times a weekly form (0.5–1.5) and the
    /// season <paramref name="luck"/>; fractions are rounded at random, so the expected change is exact but outcomes vary.
    /// </summary>
    public DevelopmentStep Week(DevelopmentInput input, Random random, double luck)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(random);
        var growth = GrowthPerYear(input) * luck;
        var decline = DeclineFor(input) * (2 - luck);
        var growthWeights = Normalise(PlayerCatalog.Attributes.ToDictionary(a => a, a => TrainingFocuses.IsTrainable(a) ? 1 + Focus(input.Training, a) : 0));
        var declineWeights = Normalise(PlayerCatalog.Attributes.ToDictionary(a => a, a => DeclineWeight(a) * (Focus(input.Training, a) > 0 ? FocusedDecline : 1)));
        var form = 0.5 + random.NextDouble();
        var values = new Dictionary<PlayerAttribute, int>();
        foreach (var attribute in PlayerCatalog.Attributes)
        {
            var change = ((growth * growthWeights[attribute]) - (decline * declineWeights[attribute])) * form / WeeksPerYear;
            values[attribute] = Math.Clamp(input.Attributes[attribute] + RoundAtRandom(change, random), RatingSet<PlayerAttribute>.MinRating, RatingSet<PlayerAttribute>.MaxRating);
        }

        var experience = RoundAtRandom(input.MinutesThisWeek * ExperiencePerMinute * CompetitionFactor(input.DivisionLevel, input.International), random);
        var leadership = Enumerable.Range(0, experience).Count(_ => random.NextDouble() < LeadershipPerExperience);
        return new DevelopmentStep(new PlayerAttributes(values), experience, leadership);
    }

    /// <summary>
    /// Fitness after one day: minus <see cref="FatiguePerMinute"/> per minute played, plus the day's recovery
    /// (<see cref="DailyRecovery"/>). 0–100.
    /// </summary>
    public int DailyFitness(int fitness, int minutesPlayed, TrainingConditions training, InjuryStatus injury)
    {
        ArgumentNullException.ThrowIfNull(training);
        var value = fitness - (minutesPlayed * FatiguePerMinute) + DailyRecovery(training, injury);
        return Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), Player.MinFitness, Player.MaxFitness);
    }

    /// <summary>
    /// Fitness recovered per day: light training 6, normal 4, hard 2 (injured players rest: 6); +2 with a recovery
    /// focus; the fitness coach adds (ability − 50) / 25 (−2 to +2).
    /// </summary>
    public static double DailyRecovery(TrainingConditions training, InjuryStatus injury)
    {
        ArgumentNullException.ThrowIfNull(training);
        var rest = injury == InjuryStatus.Injured ? 6 : training.Intensity switch
        {
            TrainingIntensity.Light => 6,
            TrainingIntensity.Hard => 2,
            _ => 4,
        };
        return rest + (training.Focuses(TrainingFocus.Recovery) ? 2 : 0) + ((Math.Clamp(training.FitnessCoachAbility, 1, 100) - 50) / 25.0);
    }

    /// <summary>
    /// Potential realisation at the end of a season (§4): up to 22 the potential is revised by −3 to +3 (late bloomers and
    /// busts); from 23 it moves towards the current overall (15 % of the gap at 23–24, 30 % at 25–26, 60 % at 27–28) and
    /// from 29 it is the overall. Never below the overall.
    /// </summary>
    public static int RevisePotential(int age, int overall, int potential, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var revised = age switch
        {
            <= 22 => potential + random.Next(-3, 4),
            <= 24 => potential - (0.15 * (potential - overall)),
            <= 26 => potential - (0.30 * (potential - overall)),
            <= 28 => potential - (0.60 * (potential - overall)),
            _ => overall,
        };
        return Math.Clamp((int)Math.Round(revised, MidpointRounding.AwayFromZero), Math.Max(overall, Player.MinRating), Player.MaxRating);
    }

    /// <summary>Whole points: the integer part plus one more with the probability of the fraction (works for negative values).</summary>
    public static int RoundAtRandom(double value, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var floor = Math.Floor(value);
        return (int)floor + (random.NextDouble() < value - floor ? 1 : 0);
    }

    private double Focus(TrainingConditions training, PlayerAttribute attribute) =>
        (TrainingFocuses.Covers(training.TeamFocus, attribute) ? FocusWeight : 0) +
        (training.IndividualFocus is { } own && TrainingFocuses.Covers(own, attribute) ? FocusWeight : 0);

    /// <summary>Scales the weights to an average of 1 over all attributes, so a focus moves development without adding to it.</summary>
    private static Dictionary<PlayerAttribute, double> Normalise(Dictionary<PlayerAttribute, double> weights)
    {
        var mean = weights.Values.Average();
        return weights.ToDictionary(w => w.Key, w => mean > 0 ? w.Value / mean : 0);
    }
}
