using Ebm.Domain.Careers;
using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;
using Ebm.Domain.Scouting;
using Ebm.Domain.Staff;
using Ebm.Services.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Clubs;

/// <summary>
/// Club staff under contract, the staff market and AI staff recruitment (scouts, Step 20; coaches, Step 21; physios,
/// Step 22; every role from the market, Step 28, docs/STAFF.md §6). The datasets carry no staff: the game generates it.
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

    /// <summary>Staff members without a running or future contract on <paramref name="today"/>: the staff market.</summary>
    public static async Task<IReadOnlyList<StaffMember>> FreeAsync(IServiceProvider s, DateOnly today, StaffRole? role, CancellationToken cancellationToken)
    {
        var employed = (await s.GetRequiredService<IRepository<Contract, ContractId>>()
                .ListAsync(c => c.StaffMemberId != null, cancellationToken).ConfigureAwait(false))
            .Where(c => c.IsRunningOn(today) || (c.Status != ContractStatus.Terminated && c.StartDate > today))
            .Select(c => c.StaffMemberId!.Value)
            .ToHashSet();
        return [.. (await s.GetRequiredService<IRepository<StaffMember, StaffMemberId>>()
                .ListAsync(m => role == null || m.Role == role, cancellationToken).ConfigureAwait(false))
            .Where(m => !employed.Contains(m.Id))];
    }

    /// <summary>
    /// Hires staff in <paramref name="role"/> until every club employs <see cref="StaffRules.Wanted"/> of them, and saves.
    /// Each hire is the best free staff member who would join, asks at most <see cref="StaffRules.AiSalaryCap"/> and was
    /// never dismissed by the club, else a generated one (<see cref="StaffRules.Generate"/>). The managed club is staffed once, when its career
    /// starts; afterwards only while the assistant manages it. Returns the number hired.
    /// </summary>
    public static async Task<int> HireMissingAsync(IServiceProvider s, DateOnly today, StaffRole role, int seedSalt, CancellationToken cancellationToken)
    {
        var clubs = await s.GetRequiredService<IRepository<Club, ClubId>>().ListAsync(null, cancellationToken).ConfigureAwait(false);
        var employed = (await StaffAsync(s, today, null, [role], cancellationToken).ConfigureAwait(false)).GroupBy(x => x.Contract.ClubId).ToDictionary(g => g.Key, g => g.Count());
        var missing = clubs.Select(c => (Club: c, Count: StaffRules.Wanted(role, c.Reputation) - employed.GetValueOrDefault(c.Id))).Where(x => x.Count > 0).ToList();
        var contracts = s.GetRequiredService<IRepository<Contract, ContractId>>();
        if ((await s.GetRequiredService<IRepository<Career, CareerId>>().ListAsync(null, cancellationToken).ConfigureAwait(false)).FirstOrDefault() is { AssistantManages: false } career
            && missing.Any(m => m.Club.Id == career.ClubId)
            && (await contracts.ListAsync(c => c.ClubId == career.ClubId && c.StaffMemberId != null, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            missing.RemoveAll(m => m.Club.Id == career.ClubId);
        }

        if (missing.Count == 0)
        {
            return 0;
        }

        var market = (await FreeAsync(s, today, role, cancellationToken).ConfigureAwait(false)).ToList();
        var dismissed = (await contracts.ListAsync(c => c.StaffMemberId != null && c.Status == ContractStatus.Terminated, cancellationToken).ConfigureAwait(false))
            .Select(c => (c.ClubId, c.StaffMemberId!.Value))
            .ToHashSet();
        var players = await s.GetRequiredService<IRepository<Player, PlayerId>>().ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false);
        var byCountry = players.GroupBy(p => p.NationalityId).ToDictionary(g => g.Key, g => g.ToList());
        var staff = s.GetRequiredService<IRepository<StaffMember, StaffMemberId>>();
        var hired = 0;
        foreach (var (club, count) in missing.OrderByDescending(m => m.Club.Reputation).ThenBy(m => m.Club.Id.Value))
        {
            var random = new Random(ScoutingRules.StableSeed(club.Id.Value, Guid.Empty, today.DayNumber ^ seedSalt));
            var cap = StaffRules.AiSalaryCap(role, club.Reputation);
            for (var i = 0; i < count; i++)
            {
                var member = market
                    .Where(m => !dismissed.Contains((club.Id, m.Id)) && StaffRules.WillJoin(m.Reputation, club.Reputation) && StaffRules.SalaryDemand(role, m.Profile) <= cap)
                    .OrderByDescending(m => m.Ability).ThenBy(m => m.Id.Value)
                    .FirstOrDefault();
                if (member is not null)
                {
                    market.Remove(member);
                }
                else
                {
                    IReadOnlyList<Player> pool = byCountry.GetValueOrDefault(club.CountryId) is { Count: > 0 } local ? local : players;
                    if (pool.Count == 0)
                    {
                        break;
                    }

                    member = Generate(role, club.Reputation, club.CountryId, pool, today, random);
                    staff.Add(member);
                }

                contracts.Add(Contract.ForStaff(ContractId.New(), club.Id, member.Id, today, today.AddYears(ContractYears).AddDays(-1), StaffRules.SalaryDemand(role, member.Profile)));
                hired++;
            }
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return hired;
    }

    /// <summary>
    /// Keeps the staff market supplied: tops up each role to <see cref="IStaffService.MarketSizeFor"/> free members of
    /// mixed quality (generated for a random club reputation 10–95 and a random club country). Returns the number added.
    /// </summary>
    public static async Task<int> SupplyMarketAsync(IServiceProvider s, DateOnly today, CancellationToken cancellationToken)
    {
        var clubs = await s.GetRequiredService<IRepository<Club, ClubId>>().ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false);
        if (clubs.Count == 0)
        {
            return 0;
        }

        var free = (await FreeAsync(s, today, null, cancellationToken).ConfigureAwait(false)).GroupBy(m => m.Role).ToDictionary(g => g.Key, g => g.Count());
        var target = IStaffService.MarketSizeFor(clubs.Count);
        var shortRoles = Enum.GetValues<StaffRole>().Select(r => (Role: r, Count: target - free.GetValueOrDefault(r))).Where(x => x.Count > 0).ToList();
        if (shortRoles.Count == 0)
        {
            return 0;
        }

        var players = await s.GetRequiredService<IRepository<Player, PlayerId>>().ListUntrackedAsync(null, cancellationToken).ConfigureAwait(false);
        if (players.Count == 0)
        {
            return 0;
        }

        var countries = clubs.Select(c => c.CountryId).Distinct().OrderBy(c => c.Value).ToList();
        var staff = s.GetRequiredService<IRepository<StaffMember, StaffMemberId>>();
        var random = new Random(ScoutingRules.StableSeed(Guid.Empty, Guid.Empty, today.DayNumber ^ 0x5A5A));
        var added = 0;
        foreach (var (role, count) in shortRoles)
        {
            for (var i = 0; i < count; i++)
            {
                staff.Add(Generate(role, random.Next(10, 96), countries[random.Next(countries.Count)], players, today, random));
                added++;
            }
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return added;
    }

    /// <summary>Names recombine the players' names (data, not code); age 35–60.</summary>
    private static StaffMember Generate(StaffRole role, int reputation, Domain.Countries.CountryId country, IReadOnlyList<Player> names, DateOnly today, Random random) => new(
        StaffMemberId.New(),
        names[random.Next(names.Count)].FirstName,
        names[random.Next(names.Count)].LastName,
        today.AddYears(-random.Next(35, 61)).AddDays(-random.Next(0, 365)),
        country,
        role,
        StaffRules.Generate(role, reputation, random));
}
