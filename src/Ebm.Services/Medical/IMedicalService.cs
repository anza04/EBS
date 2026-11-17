using Ebm.Domain.Clubs;
using Ebm.Domain.Matches;
using Ebm.Domain.Medical;
using Ebm.Domain.Players;
using Ebm.Domain.Staff;

namespace Ebm.Services.Medical;

/// <summary>
/// Injuries and medical management (Step 22, docs/INJURIES.md). The career calendar calls <see cref="ProcessDayAsync"/>
/// every day after training: injured players recover, and new injuries happen in the day's matches and training.
/// </summary>
public interface IMedicalService
{
    /// <summary>
    /// One medical day: active injuries recover (players who are fit again return with reduced fitness); players who
    /// played in <paramref name="playedToday"/> and players at training may get injured. On Mondays missing physios are
    /// hired and injuries are reassessed.
    /// </summary>
    Task<MedicalDay> ProcessDayAsync(IReadOnlyCollection<MatchId> playedToday, CancellationToken cancellationToken = default);

    /// <summary>Hires a generated physio for every club without one.</summary>
    Task<int> HireMissingPhysiosAsync(CancellationToken cancellationToken = default);

    /// <summary>The club's medical room: physio, squad injury status and risk, injury history of the last year; <c>null</c> for an unknown club.</summary>
    Task<ClubMedical?> GetClubMedicalAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>A player's medical record: every injury, newest first.</summary>
    Task<IReadOnlyList<Injury>> GetInjuriesAsync(PlayerId playerId, CancellationToken cancellationToken = default);
}

/// <summary>What one medical day did.</summary>
public sealed record MedicalDay(IReadOnlyList<InjuryNews> Injuries, int Aggravated, int Recovered, int PhysiosHired)
{
    public static MedicalDay None { get; } = new([], 0, 0, 0);
}

/// <summary>A new or aggravated injury, as the news reports it.</summary>
public sealed record InjuryNews(PlayerId PlayerId, string Player, ClubId? ClubId, InjuryType Type, InjurySeverity Severity, InjurySource Source, DateOnly EstimatedReturn, bool Aggravated);

/// <summary>A club's medical room.</summary>
/// <param name="PhysioAbility">Ability of the best physio (the default ability without one).</param>
public sealed record ClubMedical(Club Club, StaffMember? Physio, int PhysioAbility, IReadOnlyList<MedicalPlayer> Players, IReadOnlyList<InjuryRecord> History);

/// <summary>One squad player in the medical room.</summary>
/// <param name="Injury">The active injury, if any.</param>
/// <param name="Risk">Injury risk relative to an average player (1 = average).</param>
/// <param name="RecentInjuries">Injuries in the recurrence window, the active one included.</param>
public sealed record MedicalPlayer(Player Player, int Age, Injury? Injury, double Risk, int RecentInjuries);

/// <summary>An injury with the player's name.</summary>
public sealed record InjuryRecord(Injury Injury, string Player);
