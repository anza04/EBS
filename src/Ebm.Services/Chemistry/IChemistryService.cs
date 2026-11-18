using Ebm.Domain.Chemistry;
using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Matches.Simulation;
using Ebm.Domain.Players;

namespace Ebm.Services.Chemistry;

/// <summary>Squad hierarchy, morale, relationships and team chemistry (docs/CHEMISTRY.md).</summary>
public interface IChemistryService
{
    /// <summary>
    /// The weekly dressing-room update for every club that played in the last seven days: morale from results and
    /// playing time, relationships from shared court time and conflicts. Called on Mondays by the career.
    /// </summary>
    Task<ChemistryWeek> ProcessWeekAsync(CancellationToken cancellationToken = default);

    /// <summary>The club's hierarchy, satisfaction, chemistry of its current core and notable relationships; <c>null</c> for an unknown club.</summary>
    Task<ClubChemistry?> GetClubChemistryAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>The chemistry of a matchday squad's core, as the match engine reads it.</summary>
    Task<TeamChemistry> RateAsync(TeamSheet sheet, CancellationToken cancellationToken = default);
}

/// <summary>A player in conflict (unhappy and short of minutes) and the teammate playing ahead of him, if any.</summary>
public sealed record SquadConflict(ClubId ClubId, PlayerId PlayerId, string Player, string? Rival);

/// <summary>What one weekly update changed.</summary>
public sealed record ChemistryWeek(int Clubs, int MoraleChanges, int RelationshipChanges, IReadOnlyList<SquadConflict> Conflicts)
{
    public static readonly ChemistryWeek None = new(0, 0, 0, []);
}

/// <summary>
/// A first-team player in the dressing room: his place in the hierarchy, recent minutes per game (<c>null</c> below
/// three club games in the last four weeks), satisfaction and whether he is in conflict.
/// </summary>
public sealed record SquadMember(HierarchyPlace Place, double? RecentMinutes, RoleSatisfaction Satisfaction, bool Promised, bool InConflict);

/// <summary>Two teammates and their relationship score.</summary>
public sealed record RelationshipLine(string First, string Second, int Score);

public sealed record ClubChemistry(Club Club, TeamChemistry Chemistry, IReadOnlyList<SquadMember> Members, IReadOnlyList<RelationshipLine> Relationships)
{
    public SquadMember? Captain => Members.FirstOrDefault(m => m.Place.IsCaptain);
}
