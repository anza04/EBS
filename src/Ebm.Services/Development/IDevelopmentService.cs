using Ebm.Domain.Clubs;
using Ebm.Domain.Development;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;
using Ebm.Domain.Staff;

namespace Ebm.Services.Development;

/// <summary>
/// Training, player development, ageing and retirement (Step 21, docs/DEVELOPMENT.md). The career calendar drives it:
/// fitness every day, development every Monday, potential and retirement on a season's last day.
/// </summary>
public interface IDevelopmentService
{
    /// <summary>Assistant coaches per club: one, plus one per 50 reputation points (1–3).</summary>
    static int AssistantCoachesFor(int clubReputation) => StaffRules.Wanted(StaffRole.AssistantCoach, clubReputation);

    /// <summary>The club's training plan (the default plan when none is saved), squad and coaches; <c>null</c> for an unknown club.</summary>
    Task<ClubTraining?> GetClubTrainingAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>Saves the club's training plan. Individual programmes are for players of the club's squad only.</summary>
    Task<TrainingPlan> SavePlanAsync(ClubId clubId, TrainingPlanDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Hires head, assistant, development and fitness coaches (from the staff market, else generated) where a club has fewer than it needs (docs/STAFF.md §6).</summary>
    Task<int> HireMissingCoachesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The day's training: fitness for everyone who played in <paramref name="playedToday"/> or is not fully fit; on
    /// Mondays also one week of development for every active player (and missing coaches are hired).
    /// </summary>
    Task<DevelopmentDay> ProcessDayAsync(IReadOnlyCollection<MatchId> playedToday, CancellationToken cancellationToken = default);

    /// <summary>End of a season: potential is revised and players retire by the <see cref="RetirementRules"/>.</summary>
    Task<SeasonDevelopment> ProcessSeasonEndAsync(CancellationToken cancellationToken = default);
}

/// <summary>A training plan to save.</summary>
public sealed record TrainingPlanDraft(TrainingFocus TeamFocus, TrainingIntensity Intensity, IReadOnlyList<IndividualTraining> Individuals);

/// <summary>A club's training screen.</summary>
/// <param name="IsSaved">Whether <see cref="Plan"/> is stored (otherwise it is the default plan).</param>
public sealed record ClubTraining(Club Club, TrainingPlan Plan, bool IsSaved, IReadOnlyList<StaffMember> Coaches, int CoachAbility, int FitnessCoachAbility, IReadOnlyList<TrainingPlayer> Players);

/// <summary>One squad player on the training screen.</summary>
/// <param name="Outlook">Expected change of the overall rating over a year with the current plan (average luck).</param>
public sealed record TrainingPlayer(Player Player, int Age, int Overall, int Potential, TrainingFocus? IndividualFocus, double Outlook);

/// <summary>What training did on one day.</summary>
public sealed record DevelopmentDay(int FitnessChanged, int PlayersDeveloped, int Improved, int Declined, int CoachesHired)
{
    public static DevelopmentDay None { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>What the end of a season did.</summary>
public sealed record SeasonDevelopment(int PotentialsRevised, IReadOnlyList<RetiredPlayer> Retired);

/// <summary>A player who retired; <see cref="Club"/> is his last club, if he had one.</summary>
public sealed record RetiredPlayer(PlayerId PlayerId, string Name, int Age, int Overall, string? Club);
