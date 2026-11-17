using Ebm.Domain.Staff;

namespace Ebm.Domain.Scouting;

/// <summary>
/// The scouting formulas (docs/SCOUTING.md §5): how long assignments take, how accurate a report is and how far its
/// estimates may be from the truth. Pure.
/// </summary>
public static class ScoutingRules
{
    public const int MinAccuracy = 5;
    public const int MaxAccuracy = 98;

    /// <summary>Study days after which more time no longer improves accuracy.</summary>
    public const int FullStudyDays = 28;

    /// <summary>Study days of background knowledge: what a club knows without an assignment (AI clubs, unscouted players).</summary>
    public const int BackgroundStudyDays = 7;

    /// <summary>A new report on a player the club already scouted is at least this much more accurate than the last one.</summary>
    public const int RepeatBonus = 5;

    /// <summary>Most players one group assignment reports on.</summary>
    public const int ReportsPerAssignment = 10;

    /// <summary>Default oldest player of a youth assignment.</summary>
    public const int YouthMaxAge = 21;

    public const int MinTargetAge = 15;
    public const int MaxTargetAge = 45;

    /// <summary>Players this young are less visible: they play fewer minutes and are less covered.</summary>
    public const int LowVisibilityAge = 20;

    /// <summary>Calendar days an assignment takes.</summary>
    public static int DurationDays(ScoutingFocus focus) => focus switch
    {
        ScoutingFocus.Player => 7,
        ScoutingFocus.Youth => 28,
        _ => 21,
    };

    /// <summary>Study days one player gets: a single-player assignment is watched closely (4 study days per day).</summary>
    public static int StudyDays(ScoutingFocus focus, int days) => focus == ScoutingFocus.Player ? days * 4 : days;

    /// <summary>
    /// How visible a player is (1–100): his reputation, plus 20 when he plays (or, as a free agent, comes from) the
    /// scouting club's country, plus 15 when his club plays an international competition, minus 15 when he is 20 or younger.
    /// </summary>
    public static int Visibility(int reputation, bool sameCountry, bool international, int age) =>
        Math.Clamp(reputation + (sameCountry ? 20 : 0) + (international ? 15 : 0) - (age <= LowVisibilityAge ? 15 : 0), 1, 100);

    /// <summary>Accuracy (5–98) = 0.4 × scout ability + 0.3 × visibility + 30 × share of the full study time.</summary>
    public static int Accuracy(int scoutAbility, int studyDays, int visibility)
    {
        var time = Math.Clamp(studyDays, 0, FullStudyDays) / (double)FullStudyDays;
        var value = (0.4 * Math.Clamp(scoutAbility, StaffMember.MinAbility, StaffMember.MaxAbility)) + (0.3 * Math.Clamp(visibility, 1, 100)) + (30 * time);
        return Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), MinAccuracy, MaxAccuracy);
    }

    /// <summary>A repeated report builds on the last one: at least the previous accuracy plus <see cref="RepeatBonus"/>.</summary>
    public static int Combine(int accuracy, int? previous) =>
        previous is { } last ? Math.Clamp(Math.Max(accuracy, last + RepeatBonus), MinAccuracy, MaxAccuracy) : accuracy;

    /// <summary>Largest error of an attribute or the overall rating: (100 − accuracy) / 4, at least 1.</summary>
    public static int Margin(int accuracy) => Math.Max(1, (int)Math.Round((100 - accuracy) / 4.0, MidpointRounding.AwayFromZero));

    /// <summary>Potential is harder to judge: half as wide again as <see cref="Margin"/>.</summary>
    public static int PotentialMargin(int accuracy) => (int)Math.Round(Margin(accuracy) * 1.5, MidpointRounding.AwayFromZero);

    public static (int Low, int High) Range(int estimate, int margin) =>
        (Math.Max(1, estimate - margin), Math.Min(100, estimate + margin));

    /// <summary>Scouts a club employs: one, plus one per 40 reputation points (1–3).</summary>
    public static int ScoutsFor(int clubReputation) => 1 + (Math.Clamp(clubReputation, 0, 100) / 40);

    /// <summary>Typical ability of a club's scouts: 25 + 0.55 × reputation (before the individual spread of ±10).</summary>
    public static int TypicalAbility(int clubReputation) => 25 + (int)Math.Round(0.55 * Math.Clamp(clubReputation, 0, 100), MidpointRounding.AwayFromZero);

    /// <summary>A seed that is the same on every run for the same values (unlike <see cref="HashCode"/>).</summary>
    public static int StableSeed(Guid first, Guid second, int salt = 0)
    {
        unchecked
        {
            var hash = (uint)2166136261;
            Span<byte> bytes = stackalloc byte[16];
            foreach (var guid in (ReadOnlySpan<Guid>)[first, second])
            {
                guid.TryWriteBytes(bytes);
                foreach (var b in bytes)
                {
                    hash = (hash ^ b) * 16777619;
                }
            }

            hash = (hash ^ (uint)salt) * 16777619;
            return (int)(hash & 0x7FFFFFFF);
        }
    }
}
