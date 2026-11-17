using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Scouting;
using Ebm.Domain.Staff;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Clubs;

/// <summary>
/// Club staff under contract and the generated staff the game hires where a club has fewer than it needs (scouts,
/// Step 20; coaches, Step 21). The datasets carry no staff.
/// </summary>
internal static class StaffHiring
{
    /// <summary>Length of a generated staff contract, in years.</summary>
    public const int ContractYears = 3;

    /// <summary>Staff members in <paramref name="roles"/> with a contract running on <paramref name="today"/> (at one club or at all).</summary>
    public static async Task<IReadOnlyList<(StaffMember Member, Contract Contract)>> StaffAsync(
        IServiceProvider s, DateOnly today, ClubId? clubId, IReadOnlyCollection<StaffRole> roles, CancellationToken cancellationToken)
    {
        var contracts = (await s.GetRequiredService<IRepository<Contract, ContractId>>()
                .ListAsync(c => c.StaffMemberId != null && (clubId == null || c.ClubId == clubId), cancellationToken).ConfigureAwait(false))
            .Where(c => c.IsRunningOn(today))
            .ToList();
        var ids = contracts.Select(c => c.StaffMemberId!.Value).Distinct().ToList();
        var members = (await s.GetRequiredService<IRepository<StaffMember, StaffMemberId>>()
                .ListAsync(m => ids.Contains(m.Id), cancellationToken).ConfigureAwait(false))
            .Where(m => roles.Contains(m.Role))
            .ToDictionary(m => m.Id);
        return [.. contracts.Where(c => members.ContainsKey(c.StaffMemberId!.Value)).Select(c => (members[c.StaffMemberId!.Value], c))];
    }

    /// <summary>
    /// Hires generated staff in <paramref name="role"/> until every club employs <paramref name="wanted"/> of them, and saves.
    /// Ability = <see cref="ScoutingRules.TypicalAbility"/> of the club's reputation ± 10; names recombine the players' names
    /// of the club's country (data, not code), else of every player. Returns the number hired.
    /// </summary>
    public static async Task<int> HireMissingAsync(
        IServiceProvider s, DateOnly today, StaffRole role, Func<Club, int> wanted, Func<int, long> salary, int seedSalt, CancellationToken cancellationToken)
    {
        var clubs = await s.GetRequiredService<IRepository<Club, ClubId>>().ListAsync(null, cancellationToken).ConfigureAwait(false);
        var employed = (await StaffAsync(s, today, null, [role], cancellationToken).ConfigureAwait(false)).GroupBy(x => x.Contract.ClubId).ToDictionary(g => g.Key, g => g.Count());
        var missing = clubs.Select(c => (Club: c, Count: wanted(c) - employed.GetValueOrDefault(c.Id))).Where(x => x.Count > 0).ToList();
        if (missing.Count == 0)
        {
            return 0;
        }

        var players = await s.GetRequiredService<IRepository<Player, PlayerId>>().ListAsync(null, cancellationToken).ConfigureAwait(false);
        var byCountry = players.GroupBy(p => p.NationalityId).ToDictionary(g => g.Key, g => g.ToList());
        var staff = s.GetRequiredService<IRepository<StaffMember, StaffMemberId>>();
        var contracts = s.GetRequiredService<IRepository<Contract, ContractId>>();
        var hired = 0;
        foreach (var (club, count) in missing)
        {
            var pool = byCountry.GetValueOrDefault(club.CountryId) is { Count: > 0 } local ? local : players;
            if (pool.Count == 0)
            {
                continue;
            }

            var random = new Random(ScoutingRules.StableSeed(club.Id.Value, Guid.Empty, today.DayNumber ^ seedSalt));
            for (var i = 0; i < count; i++)
            {
                var ability = Math.Clamp(ScoutingRules.TypicalAbility(club.Reputation) + random.Next(-10, 11), StaffMember.MinAbility, StaffMember.MaxAbility);
                var member = new StaffMember(
                    StaffMemberId.New(),
                    pool[random.Next(pool.Count)].FirstName,
                    pool[random.Next(pool.Count)].LastName,
                    today.AddYears(-random.Next(35, 61)).AddDays(-random.Next(0, 365)),
                    club.CountryId,
                    role,
                    ability);
                staff.Add(member);
                contracts.Add(Contract.ForStaff(ContractId.New(), club.Id, member.Id, today, today.AddYears(ContractYears).AddDays(-1), salary(ability)));
                hired++;
            }
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return hired;
    }
}
