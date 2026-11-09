using Ebm.Domain.Clubs;
using Ebm.Domain.Competitions;
using Ebm.Domain.Contracts;
using Ebm.Domain.Countries;
using Ebm.Domain.Players;

namespace Ebm.Services.Editing;

/// <summary>
/// Use cases of the database editor. Every call is its own unit of work: a save either writes everything or nothing.
/// </summary>
/// <remarks>
/// Saves throw <see cref="Ebm.Domain.Common.DomainException"/> (invalid value, duplicate or missing record) or
/// <see cref="Data.DataConstraintException"/> (a database rule was broken); both messages can be shown to the user.
/// </remarks>
public interface IDatabaseEditor
{
    /// <summary>Loads every editable record. The returned entities are detached snapshots: change them through the save methods.</summary>
    Task<EditorSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    Task<PlayerId> SavePlayerAsync(PlayerDraft draft, CancellationToken cancellationToken = default);

    Task<ClubId> SaveClubAsync(ClubDraft draft, CancellationToken cancellationToken = default);

    Task<ContractId> SaveContractAsync(ContractDraft draft, CancellationToken cancellationToken = default);

    Task<LeagueId> SaveLeagueAsync(LeagueDraft draft, CancellationToken cancellationToken = default);

    Task<CompetitionId> SaveCompetitionAsync(CompetitionDraft draft, CancellationToken cancellationToken = default);
}

public sealed record EditorSnapshot(
    IReadOnlyList<Country> Countries,
    IReadOnlyList<Arena> Arenas,
    IReadOnlyList<Club> Clubs,
    IReadOnlyList<League> Leagues,
    IReadOnlyList<Division> Divisions,
    IReadOnlyList<Competition> Competitions,
    IReadOnlyList<Player> Players,
    IReadOnlyList<Contract> PlayerContracts);

/// <summary>Values of a player to create (<paramref name="Id"/> is <c>null</c>) or update.</summary>
public sealed record PlayerDraft(
    PlayerId? Id,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    CountryId NationalityId,
    int HeightCm,
    int WeightKg,
    Position Position,
    PlayerAttributes Attributes);

/// <summary>Values of a club to create (<paramref name="Id"/> is <c>null</c>) or update.</summary>
public sealed record ClubDraft(ClubId? Id, string Name, string ShortName, CountryId CountryId, ArenaId? HomeArenaId, int? FoundedYear);

/// <summary>Values of a player contract to create (<paramref name="Id"/> is <c>null</c>) or update. Club and player are fixed once created.</summary>
public sealed record ContractDraft(ContractId? Id, ClubId ClubId, PlayerId PlayerId, DateOnly StartDate, DateOnly EndDate, long AnnualSalary);

/// <summary>Values of a league to create (<paramref name="Id"/> is <c>null</c>) or update.</summary>
public sealed record LeagueDraft(LeagueId? Id, string Name, string ShortName, CountryId? CountryId);

/// <summary>Values of a competition to create (<paramref name="Id"/> is <c>null</c>) or update.</summary>
public sealed record CompetitionDraft(
    CompetitionId? Id,
    string Name,
    string ShortName,
    CompetitionType Type,
    CountryId? CountryId,
    DivisionId? DivisionId,
    string RulesJson);
