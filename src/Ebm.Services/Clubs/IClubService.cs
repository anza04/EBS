using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Players;
using Ebm.Domain.Players.Ratings;
using Ebm.Domain.Rosters;
using Ebm.Domain.Seasons;

namespace Ebm.Services.Clubs;

/// <summary>
/// Club profile and squad management. One DI scope per call; "today" comes from the <see cref="GameClock"/>.
/// </summary>
/// <remarks>
/// A club's players are those whose open club spell is at the club, plus those with a contract running at the club
/// and no open spell elsewhere (docs/ROSTER_RULES.md §1). Writes throw <see cref="Ebm.Domain.Common.DomainException"/>
/// with a player-facing message.
/// </remarks>
public interface IClubService
{
    /// <summary>The club's profile as of today, or <c>null</c> when the club does not exist.</summary>
    Task<ClubProfile?> GetProfileAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>The club's squad with roles, availability and validation, or <c>null</c> when the club does not exist.</summary>
    Task<ClubRoster?> GetRosterAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces every squad role. <paramref name="assignments"/> must list each club player exactly once; rotation
    /// players get their rotation order from the order of the list. Invalid configurations (four starters, a squad
    /// too large, …) are saved and reported in the returned validation, so a squad can be rearranged in steps.
    /// </summary>
    Task<ClubRoster> SaveRosterAsync(ClubId clubId, IReadOnlyList<RosterAssignment> assignments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a player to the club from today: his open club spell is closed and a new one opened, and he joins the
    /// squad in <paramref name="role"/> (bench, inactive or youth). Contracts are not changed (Step 07).
    /// </summary>
    Task AssignPlayerAsync(PlayerId playerId, ClubId clubId, RosterRole role = RosterRole.Bench, CancellationToken cancellationToken = default);
}

/// <summary>A player's role in a roster save. <paramref name="LineupPosition"/> is required for starters only.</summary>
public sealed record RosterAssignment(PlayerId PlayerId, RosterRole Role, Position? LineupPosition = null);

/// <summary>One club player as the squad screen shows him.</summary>
public sealed record RosterPlayer(
    Player Player,
    string Nationality,
    int Age,
    bool IsForeign,
    RosterRole Role,
    Position? LineupPosition,
    int? RotationOrder,
    PlayerAvailability Availability,
    bool HasContract,
    PlayerEvaluation Evaluation,
    bool IsNonEu = false)
{
    /// <summary>Effective rating at the lineup position for starters, otherwise the overall rating.</summary>
    public int RatingInRole => LineupPosition is { } position ? Evaluation.At(position).EffectiveRating : Evaluation.Overall;

    /// <summary>The player as the <see cref="RosterValidator"/> sees him in his stored role.</summary>
    public RosterMember ToMember() => ToMember(Role, LineupPosition);

    /// <summary>The player as the <see cref="RosterValidator"/> would see him in another role (for checks before saving).</summary>
    public RosterMember ToMember(RosterRole role, Position? lineupPosition) => new(
        Player.Id,
        Player.FullName,
        role,
        lineupPosition,
        Availability,
        IsForeign,
        Age,
        lineupPosition is { } position ? Evaluation.At(position).Familiarity : null,
        HasContract,
        IsNonEu);
}

/// <summary>A competition the club is entered in this season, and whether its squad meets the competition's registration rules.</summary>
public sealed record CompetitionRegistrationCheck(Competition Competition, SquadRules Rules, RosterValidation Validation);

/// <summary>A club's squad on <paramref name="Date"/>. <paramref name="Validation"/> applies <see cref="SquadRules.Default"/>.</summary>
public sealed record ClubRoster(
    Club Club,
    DateOnly Date,
    IReadOnlyList<RosterPlayer> Players,
    RosterValidation Validation,
    IReadOnlyList<CompetitionRegistrationCheck> Registrations)
{
    public IEnumerable<RosterPlayer> FirstTeam => Players.Where(p => p.Role.IsFirstTeam());

    /// <summary>Starters in lineup-position order.</summary>
    public IEnumerable<RosterPlayer> Starters => Players.Where(p => p.Role == RosterRole.Starter).OrderBy(p => p.LineupPosition);

    /// <summary>Rotation players in rotation order.</summary>
    public IEnumerable<RosterPlayer> Rotation => Players.Where(p => p.Role == RosterRole.Rotation).OrderBy(p => p.RotationOrder);

    public bool IsValid => Validation.IsValid && Registrations.All(r => r.Validation.IsValid);

    /// <summary>Validates <paramref name="members"/> with the same rules as this roster: club rules, then each competition's registration rules.</summary>
    public static (RosterValidation Club, IReadOnlyList<CompetitionRegistrationCheck> Registrations) Check(
        IReadOnlyList<RosterMember> members,
        IEnumerable<Competition> competitions)
    {
        ArgumentNullException.ThrowIfNull(competitions);
        return (
            RosterValidator.Validate(members, SquadRules.Default),
            [.. competitions.Select(c =>
            {
                var rules = c.ParseRules().Registration.Squad;
                return new CompetitionRegistrationCheck(c, rules, RosterValidator.ValidateRegistration(members, rules));
            })]);
    }
}

/// <summary>Everything the club profile screen shows.</summary>
/// <param name="City">The club's city, else its home arena's city.</param>
/// <param name="Season">The current season (the earliest one not yet ended), if any.</param>
/// <param name="Competitions">Competitions the club is entered in for <paramref name="Season"/>.</param>
/// <param name="WageBill">Annual salaries of the player contracts running today.</param>
public sealed record ClubProfile(
    ClubRoster Roster,
    string Country,
    string? City,
    Arena? Arena,
    Season? Season,
    IReadOnlyList<Competition> Competitions,
    long WageBill)
{
    public Club Club => Roster.Club;

    /// <summary>Wage budget minus wage bill; negative when the club spends more than the board allows.</summary>
    public long WageBudgetLeft => Club.WageBudget - WageBill;
}
