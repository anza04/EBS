using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Competitions;
using Ebm.Domain.Rosters;
using Ebm.Domain.Seasons;
using Ebm.Domain.Transfers;
using Ebm.Services.Clubs;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Transfers;

/// <summary>
/// What the market needs to know about the clubs' current season: their international competition (attractiveness)
/// and their registration window (docs/TRANSFERS.md §2, §9). Reads within the caller's scope.
/// </summary>
internal static class ClubSeason
{
    /// <summary>The current season: the earliest one not yet ended (as <c>IClubService</c> registrations).</summary>
    public static async Task<Season?> CurrentAsync(IServiceProvider services, DateOnly today, CancellationToken cancellationToken) =>
        (await Repo<Season, SeasonId>(services).ListAsync(s => s.EndDate >= today, cancellationToken).ConfigureAwait(false))
            .OrderBy(s => s.StartDate)
            .FirstOrDefault();

    /// <summary>The tier of the international competition each club plays this season (clubs without one are missing).</summary>
    public static async Task<Dictionary<ClubId, int>> InternationalTiersAsync(IServiceProvider services, DateOnly today, CancellationToken cancellationToken)
    {
        if (await CurrentAsync(services, today, cancellationToken).ConfigureAwait(false) is not { } season)
        {
            return [];
        }

        var tiers = (await Repo<Competition, CompetitionId>(services).ListAsync(c => c.Type == CompetitionType.Cup && c.CountryId == null, cancellationToken).ConfigureAwait(false))
            .Select(c => (c.Id, Tier: c.ParseRules().Cup?.Entry?.Tier))
            .Where(c => c.Tier is not null)
            .ToDictionary(c => c.Id, c => c.Tier!.Value);
        if (tiers.Count == 0)
        {
            return [];
        }

        List<CompetitionId> ids = [.. tiers.Keys];
        return (await Repo<CompetitionParticipant, CompetitionParticipantId>(services)
                .ListAsync(p => p.SeasonId == season.Id && ids.Contains(p.CompetitionId), cancellationToken).ConfigureAwait(false))
            .GroupBy(p => p.ClubId)
            .ToDictionary(g => g.Key, g => g.Min(p => tiers[p.CompetitionId]));
    }

    /// <summary>The club's attractiveness to players (<see cref="ClubAttractiveness"/>).</summary>
    public static async Task<int> AttractivenessAsync(IServiceProvider services, Club club, DateOnly today, CancellationToken cancellationToken) =>
        ClubAttractiveness.Of(club.Reputation, (await InternationalTiersAsync(services, today, cancellationToken).ConfigureAwait(false)).TryGetValue(club.Id, out var tier) ? tier : null);

    /// <summary>The club's registration window today, the fewest first-team players its competitions require and its first-team size.</summary>
    public static async Task<(RegistrationWindow Window, int MinPlayers, int FirstTeam)> RegistrationAsync(IServiceProvider services, ClubId clubId, DateOnly today, CancellationToken cancellationToken)
    {
        var (members, _) = await ClubMembership.MembersOfAsync(services, clubId, today, cancellationToken).ConfigureAwait(false);
        var roles = (await Repo<RosterSpot, RosterSpotId>(services).ListAsync(s => s.ClubId == clubId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(s => s.PlayerId, s => s.Role);
        var firstTeam = members.Count(p => !roles.TryGetValue(p, out var role) || role.IsFirstTeam());
        if (await CurrentAsync(services, today, cancellationToken).ConfigureAwait(false) is not { } season)
        {
            return (RegistrationWindow.Open, SquadRules.Default.MinPlayers, firstTeam);
        }

        var entered = (await Repo<CompetitionParticipant, CompetitionParticipantId>(services)
                .ListAsync(p => p.ClubId == clubId && p.SeasonId == season.Id, cancellationToken).ConfigureAwait(false))
            .Select(p => p.CompetitionId)
            .ToList();
        var registrations = (await Repo<Competition, CompetitionId>(services).ListAsync(c => entered.Contains(c.Id), cancellationToken).ConfigureAwait(false))
            .Select(c => (c.Name, c.ParseRules().Registration))
            .ToList();
        var window = RegistrationWindow.On(today, season.StartDate, season.EndDate, registrations);
        var minPlayers = SquadRules.Strictest([SquadRules.Default, .. registrations.Select(r => r.Registration.Squad)]).MinPlayers;
        return (window, minPlayers, firstTeam);
    }

    /// <summary>
    /// Refuses to register a new first-team player at <paramref name="club"/> after its registration deadline, unless
    /// the club is short of players (docs/TRANSFERS.md §9). A player joining the youth squad is not registered.
    /// </summary>
    public static async Task CheckRegistrationAsync(IServiceProvider services, Club club, DateOnly today, RosterRole joinAs, CancellationToken cancellationToken)
    {
        if (!joinAs.IsFirstTeam())
        {
            return;
        }

        var (window, minPlayers, firstTeam) = await RegistrationAsync(services, club.Id, today, cancellationToken).ConfigureAwait(false);
        if (!window.Allows(firstTeam + 1, minPlayers))
        {
            throw new DomainException($"{club.Name} cannot register a new player: {window.Message}");
        }
    }

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();
}
