using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Rosters;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Clubs;

/// <summary>
/// Moves players in and out of clubs: club spells (history) and roster spots (current squad role), within the
/// caller's unit of work. Shared by squad management and the contract system (docs/ROSTER_RULES.md §1, docs/CONTRACTS.md §5).
/// </summary>
internal static class ClubMembership
{
    /// <summary>
    /// Current club of every player who has one: the open club spell, else the club of the latest contract running on <paramref name="date"/>.
    /// </summary>
    public static async Task<Dictionary<PlayerId, ClubId>> CurrentClubsAsync(IServiceProvider services, DateOnly date, CancellationToken cancellationToken)
    {
        var members = new Dictionary<PlayerId, ClubId>();
        var running = await services.GetRequiredService<IRepository<Contract, ContractId>>()
            .ListAsync(Contract.RunningOn(date), cancellationToken).ConfigureAwait(false);
        foreach (var contract in running.Where(c => c.PlayerId is not null).OrderBy(c => c.StartDate))
        {
            members[contract.PlayerId!.Value] = contract.ClubId;
        }

        foreach (var spell in await OpenSpellsAsync(services, null, cancellationToken).ConfigureAwait(false))
        {
            members[spell.PlayerId] = spell.ClubId;
        }

        return members;
    }

    /// <summary>
    /// The players whose current club (<see cref="CurrentClubsAsync"/>, same rule) is <paramref name="clubId"/>, with their
    /// contracts running at the club. Reads only the club's contracts and spells and those of its candidates, so a squad
    /// loads in constant time however large the database is (512 clubs, Step 16).
    /// </summary>
    public static async Task<(IReadOnlyList<PlayerId> Members, IReadOnlyList<Contract> Contracts)> MembersOfAsync(
        IServiceProvider services, ClubId clubId, DateOnly date, CancellationToken cancellationToken)
    {
        var contracts = services.GetRequiredService<IRepository<Contract, ContractId>>();
        var history = services.GetRequiredService<IRepository<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>>();
        var here = (await contracts.ListAsync(c => c.ClubId == clubId && c.PlayerId != null, cancellationToken).ConfigureAwait(false))
            .Where(c => c.IsRunningOn(date))
            .ToList();
        var spellsHere = await history.ListAsync(s => s.ClubId == clubId && s.EndDate == null, cancellationToken).ConfigureAwait(false);
        List<PlayerId> candidates = [.. here.Select(c => c.PlayerId!.Value).Concat(spellsHere.Select(s => s.PlayerId)).Distinct()];
        List<PlayerId?> contractCandidates = [.. candidates.Select(p => (PlayerId?)p)];

        var openSpells = (await history.ListAsync(s => s.EndDate == null && candidates.Contains(s.PlayerId), cancellationToken).ConfigureAwait(false))
            .ToDictionary(s => s.PlayerId, s => s.ClubId);
        var latestContract = (await contracts.ListAsync(c => contractCandidates.Contains(c.PlayerId), cancellationToken).ConfigureAwait(false))
            .Where(c => c.IsRunningOn(date))
            .OrderBy(c => c.StartDate)
            .GroupBy(c => c.PlayerId!.Value)
            .ToDictionary(g => g.Key, g => g.Last().ClubId);
        List<PlayerId> members =
        [
            .. candidates.Where(p => (openSpells.TryGetValue(p, out var spell) ? spell : latestContract.GetValueOrDefault(p)) == clubId),
        ];
        return (members, here);
    }

    /// <summary>
    /// The player joins <paramref name="clubId"/> on <paramref name="date"/>: his open spell (at any club) ends that day,
    /// a new spell starts, and his roster spot moves to the club in <paramref name="role"/>.
    /// </summary>
    public static async Task JoinAsync(IServiceProvider services, PlayerId playerId, ClubId clubId, DateOnly date, RosterRole role, CancellationToken cancellationToken)
    {
        foreach (var open in await OpenSpellsAsync(services, playerId, cancellationToken).ConfigureAwait(false))
        {
            open.Close(open.StartDate > date ? open.StartDate : date);
        }

        services.GetRequiredService<IRepository<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>>()
            .Add(new PlayerClubHistoryEntry(PlayerClubHistoryEntryId.New(), playerId, clubId, date));
        var spots = services.GetRequiredService<IRepository<RosterSpot, RosterSpotId>>();
        if ((await spots.ListAsync(s => s.PlayerId == playerId, cancellationToken).ConfigureAwait(false)).SingleOrDefault() is { } spot)
        {
            spot.MoveTo(clubId);
            spot.Assign(role, null, null);
        }
        else
        {
            spots.Add(new RosterSpot(RosterSpotId.New(), playerId, clubId, role));
        }
    }

    /// <summary>
    /// The player leaves <paramref name="clubId"/>: his open spell there ends on <paramref name="lastDay"/> and his
    /// roster spot there is removed. Nothing happens when he is not (or no longer) at the club.
    /// </summary>
    public static async Task LeaveAsync(IServiceProvider services, PlayerId playerId, ClubId clubId, DateOnly lastDay, CancellationToken cancellationToken)
    {
        foreach (var open in (await OpenSpellsAsync(services, playerId, cancellationToken).ConfigureAwait(false)).Where(s => s.ClubId == clubId))
        {
            open.Close(open.StartDate > lastDay ? open.StartDate : lastDay);
        }

        var spots = services.GetRequiredService<IRepository<RosterSpot, RosterSpotId>>();
        foreach (var spot in await spots.ListAsync(s => s.PlayerId == playerId && s.ClubId == clubId, cancellationToken).ConfigureAwait(false))
        {
            spots.Remove(spot);
        }
    }

    /// <summary>Open spells of one player (or of everyone). Re-checked in memory: a spell closed earlier in this unit of work is not open.</summary>
    public static async Task<IReadOnlyList<PlayerClubHistoryEntry>> OpenSpellsAsync(IServiceProvider services, PlayerId? playerId, CancellationToken cancellationToken)
    {
        var history = services.GetRequiredService<IRepository<PlayerClubHistoryEntry, PlayerClubHistoryEntryId>>();
        var spells = playerId is { } id
            ? await history.ListAsync(s => s.PlayerId == id && s.EndDate == null, cancellationToken).ConfigureAwait(false)
            : await history.ListAsync(s => s.EndDate == null, cancellationToken).ConfigureAwait(false);
        return [.. spells.Where(s => s.IsCurrent)];
    }
}
