namespace Ebm.Domain.Staff;

/// <summary>Job of a staff member (docs/STAFF.md §1). Stored by name, so new roles append safely.</summary>
public enum StaffRole
{
    HeadCoach,
    AssistantCoach,
    Scout,
    Physio,
    FitnessCoach,

    /// <summary>Player development staff (Step 28): individual skill work, counted in the coaching factor of training.</summary>
    DevelopmentCoach,
}

/// <summary>A coach's way of working (docs/STAFF.md §4): it shapes the AI coach's tactic and the squad's development and morale.</summary>
public enum CoachingStyle
{
    Balanced,

    /// <summary>Faster tempo and more spacing.</summary>
    Attacking,

    /// <summary>Higher defensive intensity and ball pressure.</summary>
    Defensive,

    /// <summary>Young players (≤ 23) grow 10% faster; plays a deep rotation.</summary>
    YouthDevelopment,

    /// <summary>Everyone grows 5% faster; tight rotation, protects players in foul trouble; morale above 60 slips one more point a week.</summary>
    Disciplinarian,

    /// <summary>Counts as 15 points more motivation for morale.</summary>
    Motivator,
}
