using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Matches;
using Ebm.Domain.Players;

namespace Ebm.Domain.Medical;

/// <summary>Where an injury happened.</summary>
public enum InjurySource
{
    Match,
    Training,
}

/// <summary>Where a player is in his recovery (docs/INJURIES.md §3).</summary>
public enum InjuryPhase
{
    /// <summary>Treatment: no training.</summary>
    Treatment,

    /// <summary>Rehabilitation (the last part of the recovery): individual light work, no team training.</summary>
    Rehabilitation,

    Recovered,
}

/// <summary>What training an injured player may do.</summary>
public enum TrainingRestriction
{
    None,

    /// <summary>Individual light work only: no team training (rehabilitation, minor knocks).</summary>
    LightOnly,

    /// <summary>No training at all (treatment).</summary>
    NoTraining,
}

public readonly record struct InjuryId(Guid Value) : IEntityId<InjuryId>
{
    public static InjuryId New() => new(Guid.CreateVersion7());

    public static InjuryId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// One injury of one player: the medical record (docs/INJURIES.md). History, never deleted. <see cref="RecoveryDays"/> is
/// the true recovery time; the club only knows the medical staff's estimate (<see cref="EstimatedReturn"/>).
/// </summary>
public sealed class Injury : Entity<InjuryId>
{
    /// <summary>The last share of the recovery is rehabilitation.</summary>
    public const double RehabilitationShare = 0.3;

    public Injury(InjuryId id, PlayerId playerId, ClubId? clubId, InjuryType type, InjurySource source, DateOnly occurredOn, int recoveryDays, bool isRecurrence, MatchId? matchId = null)
        : base(id)
    {
        PlayerId = Guard.Required(playerId, nameof(playerId));
        ClubId = Guard.Optional(clubId, nameof(clubId));
        Type = Guard.Defined(type, "Injury type");
        Source = Guard.Defined(source, "Injury source");
        OccurredOn = occurredOn;
        RecoveryDays = Guard.Range(recoveryDays, 1, 400, "Recovery days");
        Severity = InjuryCatalog.SeverityFor(recoveryDays);
        IsRecurrence = isRecurrence;
        MatchId = Guard.Optional(matchId, nameof(matchId));
        EstimatedReturn = occurredOn.AddDays(recoveryDays);
        AssessedOn = occurredOn;
    }

    private Injury()
    {
    }

    public PlayerId PlayerId { get; private set; }

    /// <summary>The club the player was at when he got injured (<c>null</c> for a free agent).</summary>
    public ClubId? ClubId { get; private set; }

    public InjuryType Type { get; private set; }

    public InjurySeverity Severity { get; private set; }

    public InjurySource Source { get; private set; }

    public DateOnly OccurredOn { get; private set; }

    public MatchId? MatchId { get; private set; }

    /// <summary>Days of recovery needed at normal speed (grows when the injury is aggravated).</summary>
    public int RecoveryDays { get; private set; }

    /// <summary>Days of recovery done so far (medical care speeds it up or slows it down).</summary>
    public double Progress { get; private set; }

    /// <summary>The medical staff's latest estimate of the return date.</summary>
    public DateOnly EstimatedReturn { get; private set; }

    public DateOnly AssessedOn { get; private set; }

    /// <summary>Whether the same body part was injured within the recurrence window before.</summary>
    public bool IsRecurrence { get; private set; }

    /// <summary>How often the player made it worse by playing on it.</summary>
    public int Aggravations { get; private set; }

    /// <summary>The first day the player was fit again; <c>null</c> while injured.</summary>
    public DateOnly? RecoveredOn { get; private set; }

    public InjuryDefinition Definition => InjuryCatalog.Of(Type);

    public BodyPart BodyPart => Definition.BodyPart;

    public bool IsActive => RecoveredOn is null;

    public double RemainingDays => Math.Max(0, RecoveryDays - Progress);

    public InjuryPhase Phase => RecoveredOn is not null ? InjuryPhase.Recovered
        : Progress >= RecoveryDays * (1 - RehabilitationShare) ? InjuryPhase.Rehabilitation
        : InjuryPhase.Treatment;

    /// <summary>Minor injuries leave the player doubtful (he may play); the others rule him out until he has recovered.</summary>
    public InjuryStatus Status => !IsActive ? InjuryStatus.Healthy : Severity == InjurySeverity.Minor ? InjuryStatus.DayToDay : InjuryStatus.Injured;

    public TrainingRestriction Restriction => !IsActive ? TrainingRestriction.None
        : Severity == InjurySeverity.Minor || Phase == InjuryPhase.Rehabilitation ? TrainingRestriction.LightOnly
        : TrainingRestriction.NoTraining;

    /// <summary>Days the player was (or has been) out up to <paramref name="today"/>.</summary>
    public int DaysOut(DateOnly today) => (RecoveredOn ?? today).DayNumber - OccurredOn.DayNumber;

    /// <summary>Records a medical assessment: the estimated return date.</summary>
    public void Assess(DateOnly today, DateOnly estimatedReturn)
    {
        EnsureActive();
        AssessedOn = today;
        EstimatedReturn = estimatedReturn > today ? estimatedReturn : today.AddDays(1);
    }

    /// <summary>One day of recovery worth <paramref name="days"/>; when the recovery is complete the player is fit from tomorrow.</summary>
    public void Recover(DateOnly today, double days)
    {
        EnsureActive();
        if (days < 0)
        {
            throw new DomainValidationException("Recovery cannot go backwards.");
        }

        Progress = Math.Min(RecoveryDays, Progress + days);
        if (Progress >= RecoveryDays)
        {
            RecoveredOn = today.AddDays(1);
        }
    }

    /// <summary>The player made the injury worse (played on it): <paramref name="extraDays"/> more recovery.</summary>
    public void Aggravate(int extraDays)
    {
        EnsureActive();
        RecoveryDays = Guard.Range(RecoveryDays + extraDays, 1, 400, "Recovery days");
        Severity = InjuryCatalog.SeverityFor(RecoveryDays);
        Aggravations++;
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new DomainValidationException("The player has already recovered from this injury.");
        }
    }
}
