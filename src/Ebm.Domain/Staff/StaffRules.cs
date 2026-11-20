using Ebm.Domain.Scouting;
using Ebm.Domain.Tactics;

namespace Ebm.Domain.Staff;

/// <summary>The head coach as the match engine sees him (docs/STAFF.md §5).</summary>
/// <param name="TacticalKnowledge">Effective tactical knowledge (with the experience bonus), 1–100.</param>
public sealed record CoachProfile(int TacticalKnowledge, CoachingStyle Style)
{
    /// <summary>No head coach under contract: average knowledge, no style. Changes nothing in the engine.</summary>
    public static CoachProfile Neutral { get; } = new(StaffMember.DefaultAbility, CoachingStyle.Balanced);

    public static CoachProfile Of(StaffMember headCoach)
    {
        ArgumentNullException.ThrowIfNull(headCoach);
        return new(StaffRules.Effective(headCoach.TacticalKnowledge, headCoach.Experience), headCoach.Style);
    }
}

/// <summary>
/// Staff roles, abilities, salaries, hiring and influence rules (Step 28, docs/STAFF.md). Pure and deterministic.
/// </summary>
public static class StaffRules
{
    public const int MinContractYears = 1;
    public const int MaxContractYears = 5;

    /// <summary>A staff member joins a club whose reputation is at most this much below his own.</summary>
    public const int ReputationGap = 20;

    /// <summary>Make-probability change for a head coach of tactical knowledge 100 (−this at 1, 0 at 50).</summary>
    public const double MakeEdge = 0.01;

    /// <summary>Head coaches below this tactical knowledge prepare no game plan and review late.</summary>
    public const int PlanningKnowledge = 40;

    /// <summary>Head coaches from this tactical knowledge stop opponents' runs sooner.</summary>
    public const int ExpertKnowledge = 70;

    /// <summary>Roles that coach the squad (the coaching factor of training).</summary>
    public static IReadOnlyList<StaffRole> CoachingRoles { get; } = [StaffRole.HeadCoach, StaffRole.AssistantCoach, StaffRole.DevelopmentCoach];

    /// <summary>Bonus of experience to every attribute: +1 per four years, at most +5.</summary>
    public static int ExperienceBonus(int experience) => Math.Clamp(experience, 0, 20) / 4;

    /// <summary>An attribute as it works in the game: the rating plus the experience bonus, at most 100.</summary>
    public static int Effective(int attribute, int experience) => Math.Clamp(attribute + ExperienceBonus(experience), StaffMember.MinAbility, StaffMember.MaxAbility);

    /// <summary>
    /// Ability in a role: head coach 40% tactical knowledge, 30% motivation, 30% development; assistant coach half
    /// tactical knowledge, half development; development coach development; fitness coach half development, half
    /// medical expertise; physio medical expertise; scout scouting; plus the experience bonus.
    /// </summary>
    public static int RoleAbility(StaffRole role, StaffProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var p = profile;
        var raw = role switch
        {
            StaffRole.HeadCoach => (0.4 * p.TacticalKnowledge) + (0.3 * p.Motivation) + (0.3 * p.Development),
            StaffRole.AssistantCoach => (0.5 * p.TacticalKnowledge) + (0.5 * p.Development),
            StaffRole.DevelopmentCoach => p.Development,
            StaffRole.FitnessCoach => (0.5 * p.Development) + (0.5 * p.MedicalExpertise),
            StaffRole.Physio => p.MedicalExpertise,
            StaffRole.Scout => p.Scouting,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
        return Effective((int)Math.Round(raw, MidpointRounding.AwayFromZero), p.Experience);
    }

    /// <summary>
    /// How many of a role a club employs (the AI's target): one head coach, physio and fitness coach; assistant coaches
    /// 1 + reputation / 50; scouts 1 + reputation / 40; a development coach from reputation 50.
    /// </summary>
    public static int Wanted(StaffRole role, int clubReputation)
    {
        var reputation = Math.Clamp(clubReputation, 0, 100);
        return role switch
        {
            StaffRole.AssistantCoach => 1 + (reputation / 50),
            StaffRole.Scout => ScoutingRules.ScoutsFor(reputation),
            StaffRole.DevelopmentCoach => reputation >= 50 ? 1 : 0,
            _ => 1,
        };
    }

    /// <summary>Most staff of a role a club may employ at once.</summary>
    public static int MaxPerRole(StaffRole role) => role switch
    {
        StaffRole.HeadCoach => 1,
        StaffRole.AssistantCoach => 3,
        StaffRole.Scout => 6,
        _ => 2,
    };

    /// <summary>
    /// Market salary for an ability: head coach € 40,000 + € 1,500 a point; assistant, fitness and development coach
    /// € 25,000 + € 700; scout and physio € 20,000 + € 600 (the Step 20–22 rates).
    /// </summary>
    public static long BaseSalary(StaffRole role, int ability) => role switch
    {
        StaffRole.HeadCoach => 40_000 + (1_500L * ability),
        StaffRole.AssistantCoach or StaffRole.FitnessCoach or StaffRole.DevelopmentCoach => 25_000 + (700L * ability),
        _ => 20_000 + (600L * ability),
    };

    /// <summary>What a staff member asks for: the base salary of his role ability × (0.8 + 0.4 × reputation / 100), to € 1,000.</summary>
    public static long SalaryDemand(StaffRole role, StaffProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var demand = BaseSalary(role, RoleAbility(role, profile)) * (0.8 + (0.4 * profile.Reputation / 100.0));
        return (long)Math.Round(demand / 1_000, MidpointRounding.AwayFromZero) * 1_000;
    }

    public static bool WillJoin(int staffReputation, int clubReputation) => clubReputation >= staffReputation - ReputationGap;

    /// <summary>Most an AI club pays for a role: the demand of a typical member for its reputation + 10 ability, reputation 100.</summary>
    public static long AiSalaryCap(StaffRole role, int clubReputation) =>
        (long)(BaseSalary(role, Math.Min(StaffMember.MaxAbility, ScoutingRules.TypicalAbility(clubReputation) + 10)) * 1.2);

    /// <summary>
    /// A generated staff member for a club of <paramref name="clubReputation"/>: the role's key attributes at the
    /// typical ability (<see cref="ScoutingRules.TypicalAbility"/>) ± 10, the others 15 lower ± 10, reputation the
    /// club's ± 10, 0–25 years of experience; coaches get a style, head coaches half the time tactical preferences.
    /// </summary>
    public static StaffProfile Generate(StaffRole role, int clubReputation, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var typical = ScoutingRules.TypicalAbility(clubReputation);
        int Key() => Rating(typical + random.Next(-10, 11));
        int Other() => Rating(typical - 15 + random.Next(-10, 11));
        var tactical = role is StaffRole.HeadCoach or StaffRole.AssistantCoach ? Key() : Other();
        var development = role is StaffRole.HeadCoach or StaffRole.AssistantCoach or StaffRole.DevelopmentCoach or StaffRole.FitnessCoach ? Key() : Other();
        var motivation = role == StaffRole.HeadCoach ? Key() : Other();
        var scouting = role == StaffRole.Scout ? Key() : Other();
        var medical = role is StaffRole.Physio or StaffRole.FitnessCoach ? Key() : Other();
        var reputation = Rating(Math.Clamp(clubReputation, 0, 100) + random.Next(-10, 11));
        var experience = random.Next(0, 26);
        var coach = CoachingRoles.Contains(role);
        var style = coach ? (CoachingStyle)random.Next(Enum.GetValues<CoachingStyle>().Length) : CoachingStyle.Balanced;
        OffensiveSystem? offense = null;
        DefensiveSystem? defense = null;
        if (role == StaffRole.HeadCoach && random.Next(2) == 0)
        {
            offense = PreferredOffenses[random.Next(PreferredOffenses.Length)];
            defense = PreferredDefenses[random.Next(PreferredDefenses.Length)];
        }

        return new StaffProfile(tactical, development, motivation, scouting, medical, reputation, experience, style, offense, defense);
    }

    // ---------------------------------------------------------------- influence (docs/STAFF.md §5)

    /// <summary>Growth multiplier of a coaching style: youth development ×1.10 up to age 23, disciplinarian ×1.05.</summary>
    public static double StyleGrowthFactor(CoachingStyle style, int age) => style switch
    {
        CoachingStyle.YouthDevelopment when age <= 23 => 1.10,
        CoachingStyle.Disciplinarian => 1.05,
        _ => 1.0,
    };

    /// <summary>
    /// The head coach's weekly morale effect: effective motivation (+15 for a motivator) ≥ 70 lifts morale below 50 by
    /// one, ≤ 30 lowers morale above 50 by one; a disciplinarian takes one more point from morale above 60.
    /// </summary>
    public static int MoraleEffect(int morale, int motivation, CoachingStyle style)
    {
        var drive = motivation + (style == CoachingStyle.Motivator ? 15 : 0);
        var change = drive >= 70 && morale < 50 ? 1 : drive <= 30 && morale > 50 ? -1 : 0;
        return change - (style == CoachingStyle.Disciplinarian && morale > 60 ? 1 : 0);
    }

    /// <summary>Make-probability edge of the head coach's tactical knowledge: ±<see cref="MakeEdge"/> at 100 / 1.</summary>
    public static double CoachEdge(int tacticalKnowledge) => MakeEdge * (Math.Clamp(tacticalKnowledge, 1, 100) - 50) / 50.0;

    /// <summary>Whether the engine's coach prepares a game plan against the opponent.</summary>
    public static bool PreparesGamePlan(int tacticalKnowledge) => tacticalKnowledge >= PlanningKnowledge;

    /// <summary>Seconds left in the last period when the engine's coach reviews the closing minutes: 240, or 120 below <see cref="PlanningKnowledge"/>.</summary>
    public static int ClosingReviewSeconds(int tacticalKnowledge) => tacticalKnowledge < PlanningKnowledge ? 120 : 240;

    /// <summary>Unanswered points after which the engine's coach calls a timeout: 8, or 6 from <see cref="ExpertKnowledge"/>.</summary>
    public static int TimeoutRun(int tacticalKnowledge) => tacticalKnowledge >= ExpertKnowledge ? 6 : 8;

    private static readonly OffensiveSystem[] PreferredOffenses =
        [OffensiveSystem.MotionOffense, OffensiveSystem.PickAndRoll, OffensiveSystem.FiveOut, OffensiveSystem.Transition, OffensiveSystem.PostOriented];

    private static readonly DefensiveSystem[] PreferredDefenses = [DefensiveSystem.ManToMan, DefensiveSystem.Zone23, DefensiveSystem.HalfCourtPress];

    private static int Rating(int value) => Math.Clamp(value, StaffMember.MinAbility, StaffMember.MaxAbility);
}
