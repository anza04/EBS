using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;
using Ebm.Domain.Staff;

namespace Ebm.Domain.Scouting;

/// <summary>What a scout is sent to watch (docs/SCOUTING.md §2).</summary>
public enum ScoutingFocus
{
    /// <summary>One named player.</summary>
    Player,

    /// <summary>Players at the clubs of one country (free agents: by nationality).</summary>
    Region,

    /// <summary>Players at the clubs entered in one competition this season.</summary>
    League,

    /// <summary>Players of one position, anywhere.</summary>
    Position,

    /// <summary>Young players (at most <see cref="ScoutingAssignment.MaxAge"/>), optionally in one country.</summary>
    Youth,
}

public enum AssignmentStatus
{
    Active,
    Completed,
    Cancelled,
}

/// <summary>
/// A scout's assignment for his club: a focus with its target and optional filters, from <see cref="StartDate"/> to
/// <see cref="DueDate"/>. Reports are written when it is due (docs/SCOUTING.md §3). Never deleted.
/// </summary>
public sealed class ScoutingAssignment : Entity<ScoutingAssignmentId>
{
    private ScoutingAssignment(
        ScoutingAssignmentId id,
        ClubId clubId,
        StaffMemberId scoutId,
        ScoutingFocus focus,
        DateOnly startDate,
        PlayerId? playerId,
        CountryId? countryId,
        CompetitionId? competitionId,
        Position? position,
        int? maxAge)
        : base(id)
    {
        ClubId = Guard.Required(clubId, nameof(clubId));
        ScoutId = Guard.Required(scoutId, nameof(scoutId));
        Focus = Guard.Defined(focus, "Scouting focus");
        PlayerId = Guard.Optional(playerId, nameof(playerId));
        CountryId = Guard.Optional(countryId, nameof(countryId));
        CompetitionId = Guard.Optional(competitionId, nameof(competitionId));
        Position = position is { } p ? Guard.Defined(p, "Position") : null;
        MaxAge = maxAge is { } age ? Guard.Range(age, ScoutingRules.MinTargetAge, ScoutingRules.MaxTargetAge, "Maximum age") : null;
        StartDate = startDate;
        DueDate = startDate.AddDays(ScoutingRules.DurationDays(focus));
        Status = AssignmentStatus.Active;

        var targetMissing = focus switch
        {
            ScoutingFocus.Player => playerId is null ? "a player" : null,
            ScoutingFocus.Region => countryId is null ? "a country" : null,
            ScoutingFocus.League => competitionId is null ? "a competition" : null,
            ScoutingFocus.Position => position is null ? "a position" : null,
            _ => null,
        };
        if (targetMissing is not null)
        {
            throw new DomainValidationException($"A {focus.ToString().ToLowerInvariant()} assignment needs {targetMissing}.");
        }

        if (focus == ScoutingFocus.Youth)
        {
            MaxAge ??= ScoutingRules.YouthMaxAge;
        }
    }

    private ScoutingAssignment()
    {
    }

    public ClubId ClubId { get; private set; }

    /// <summary>The staff member (role <see cref="StaffRole.Scout"/>) doing the work.</summary>
    public StaffMemberId ScoutId { get; private set; }

    public ScoutingFocus Focus { get; private set; }

    /// <summary>Target of a <see cref="ScoutingFocus.Player"/> assignment.</summary>
    public PlayerId? PlayerId { get; private set; }

    /// <summary>Target of a <see cref="ScoutingFocus.Region"/> assignment; optional filter of a youth assignment.</summary>
    public CountryId? CountryId { get; private set; }

    /// <summary>Target of a <see cref="ScoutingFocus.League"/> assignment.</summary>
    public CompetitionId? CompetitionId { get; private set; }

    /// <summary>Target of a <see cref="ScoutingFocus.Position"/> assignment; optional filter otherwise (primary or secondary position).</summary>
    public Position? Position { get; private set; }

    /// <summary>Oldest player watched; always set for youth assignments.</summary>
    public int? MaxAge { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>The day the reports are written.</summary>
    public DateOnly DueDate { get; private set; }

    public AssignmentStatus Status { get; private set; }

    /// <summary>The day the assignment was completed or cancelled.</summary>
    public DateOnly? EndedOn { get; private set; }

    /// <summary>Days the scout spends watching.</summary>
    public int Days => DueDate.DayNumber - StartDate.DayNumber;

    public static ScoutingAssignment ForPlayer(ScoutingAssignmentId id, ClubId clubId, StaffMemberId scoutId, DateOnly startDate, PlayerId playerId) =>
        new(id, clubId, scoutId, ScoutingFocus.Player, startDate, playerId, null, null, null, null);

    /// <summary>A group assignment (every focus but <see cref="ScoutingFocus.Player"/>); unused targets must be <c>null</c>.</summary>
    public static ScoutingAssignment ForGroup(
        ScoutingAssignmentId id,
        ClubId clubId,
        StaffMemberId scoutId,
        ScoutingFocus focus,
        DateOnly startDate,
        CountryId? countryId = null,
        CompetitionId? competitionId = null,
        Position? position = null,
        int? maxAge = null)
    {
        if (focus == ScoutingFocus.Player)
        {
            throw new DomainValidationException("Use a player assignment to scout one player.");
        }

        return new(id, clubId, scoutId, focus, startDate, null, countryId, focus == ScoutingFocus.League ? competitionId : null, position, maxAge);
    }

    public bool IsDueOn(DateOnly date) => Status == AssignmentStatus.Active && date >= DueDate;

    public void Complete(DateOnly date)
    {
        RequireActive();
        Status = AssignmentStatus.Completed;
        EndedOn = date;
    }

    public void Cancel(DateOnly date)
    {
        RequireActive();
        Status = AssignmentStatus.Cancelled;
        EndedOn = date;
    }

    private void RequireActive()
    {
        if (Status != AssignmentStatus.Active)
        {
            throw new DomainValidationException($"The assignment is already {Status.ToString().ToLowerInvariant()}.");
        }
    }
}
