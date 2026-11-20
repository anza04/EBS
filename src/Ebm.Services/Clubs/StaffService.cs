using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Development;
using Ebm.Domain.Finances;
using Ebm.Domain.Staff;
using Ebm.Services.Data;
using Ebm.Services.Finances;
using Microsoft.Extensions.DependencyInjection;

namespace Ebm.Services.Clubs;

/// <summary>Implements <see cref="IStaffService"/> over the staff contracts and the pure <see cref="StaffRules"/>.</summary>
internal sealed class StaffService(IServiceScopeFactory scopes, GameClock clock) : IStaffService
{
    private static readonly StaffRole[] AllRoles = Enum.GetValues<StaffRole>();

    public async Task<ClubStaff?> GetClubStaffAsync(ClubId clubId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var today = clock.Today;
        if (await Repo<Club, ClubId>(s).FindAsync(clubId, cancellationToken).ConfigureAwait(false) is not { } club)
        {
            return null;
        }

        var lines = (await StaffHiring.StaffAsync(s, today, clubId, AllRoles, cancellationToken).ConfigureAwait(false))
            .Select(x => new StaffLine(x.Member, x.Contract, x.Member.AgeOn(today)))
            .OrderBy(x => x.Member.Role).ThenByDescending(x => x.Member.Ability).ThenBy(x => x.Member.LastName)
            .ToList();
        return new ClubStaff(club, lines, Duties([.. lines.Select(l => l.Member)]), lines.Sum(l => l.Contract.AnnualSalary));
    }

    /// <summary>Who is responsible for what and the rating each system uses (docs/STAFF.md §5).</summary>
    internal static IReadOnlyList<StaffDuty> Duties(IReadOnlyList<StaffMember> staff)
    {
        StaffMember? Best(StaffRole role) => staff.Where(m => m.Role == role).MaxBy(m => m.Ability);
        var head = Best(StaffRole.HeadCoach);
        var coaches = staff.Where(m => StaffRules.CoachingRoles.Contains(m.Role)).ToList();
        var coaching = coaches.Count == 0
            ? TrainingConditions.MissingCoachAbility
            : (int)Math.Round(coaches.Average(m => StaffRules.Effective(m.Development, m.Experience)), MidpointRounding.AwayFromZero);
        int Role(StaffRole role) => Best(role)?.Ability ?? TrainingConditions.MissingCoachAbility;
        return
        [
            new("Tactics and match coaching", StaffRole.HeadCoach, head, head is null ? CoachProfile.Neutral.TacticalKnowledge : CoachProfile.Of(head).TacticalKnowledge,
                "AI tactic (style, preferred systems), game plan, timeouts and closing review, shooting edge"),
            new("Motivation", StaffRole.HeadCoach, head, head is null ? StaffMember.DefaultAbility : StaffRules.Effective(head.Motivation, head.Experience),
                "Weekly morale"),
            new("Training and development", StaffRole.AssistantCoach, coaches.MaxBy(m => StaffRules.Effective(m.Development, m.Experience)), coaching,
                "Player growth (all head, assistant and development coaches; the head coach's style)"),
            new("Fitness", StaffRole.FitnessCoach, Best(StaffRole.FitnessCoach), Role(StaffRole.FitnessCoach), "Daily fitness recovery"),
            new("Medical care", StaffRole.Physio, Best(StaffRole.Physio), Role(StaffRole.Physio), "Injury risk, recovery and return estimates"),
            new("Scouting", StaffRole.Scout, Best(StaffRole.Scout), Role(StaffRole.Scout), "Report accuracy (each scout his own)"),
        ];
    }

    public async Task<IReadOnlyList<StaffCandidate>> GetMarketAsync(ClubId clubId, StaffRole? role = null, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var today = clock.Today;
        var club = await Repo<Club, ClubId>(s).FindAsync(clubId, cancellationToken).ConfigureAwait(false) ?? throw new DomainException("The club no longer exists in the database.");
        return [.. (await StaffHiring.FreeAsync(s, today, role, cancellationToken).ConfigureAwait(false))
            .Select(m => new StaffCandidate(m, m.AgeOn(today), StaffRules.SalaryDemand(m.Role, m.Profile), StaffRules.WillJoin(m.Reputation, club.Reputation)))
            .OrderBy(c => c.Member.Role).ThenByDescending(c => c.Member.Ability).ThenBy(c => c.Member.LastName)];
    }

    public async Task<Contract> HireAsync(ClubId clubId, StaffMemberId staffMemberId, long annualSalary, int years, CancellationToken cancellationToken = default)
    {
        if (years is < StaffRules.MinContractYears or > StaffRules.MaxContractYears)
        {
            throw new DomainValidationException($"A staff contract runs {StaffRules.MinContractYears}–{StaffRules.MaxContractYears} years.");
        }

        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var today = clock.Today;
        var club = await Repo<Club, ClubId>(s).FindAsync(clubId, cancellationToken).ConfigureAwait(false) ?? throw new DomainException("The club no longer exists in the database.");
        var member = (await StaffHiring.FreeAsync(s, today, null, cancellationToken).ConfigureAwait(false)).FirstOrDefault(m => m.Id == staffMemberId)
            ?? throw new DomainException("This staff member is not on the market (he is under contract or no longer exists).");
        var role = RoleName(member.Role);
        var employed = (await StaffHiring.StaffAsync(s, today, clubId, [member.Role], cancellationToken).ConfigureAwait(false)).Count;
        if (employed >= StaffRules.MaxPerRole(member.Role))
        {
            throw new DomainException($"{club.Name} already employs {employed} {role}(s), the most allowed; dismiss one first.");
        }

        if (!StaffRules.WillJoin(member.Reputation, club.Reputation))
        {
            throw new DomainException($"{member.FullName} (reputation {member.Reputation}) will not join a club of reputation {club.Reputation}.");
        }

        var demand = StaffRules.SalaryDemand(member.Role, member.Profile);
        if (annualSalary < demand)
        {
            throw new DomainException($"{member.FullName} asks for at least € {demand:N0} a year.");
        }

        var contract = Contract.ForStaff(ContractId.New(), clubId, member.Id, today, today.AddYears(years).AddDays(-1), annualSalary);
        Repo<Contract, ContractId>(s).Add(contract);
        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return contract;
    }

    public async Task<long> DismissAsync(ContractId contractId, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var paid = await DismissAsync(s, contractId, clock.Today, cancellationToken).ConfigureAwait(false);
        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return paid;
    }

    public async Task<int> SupplyMarketAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        return await StaffHiring.SupplyMarketAsync(scope.ServiceProvider, clock.Today, cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> ReviewHeadCoachesAsync(IReadOnlyDictionary<ClubId, bool> objectivesMet, IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectivesMet);
        ArgumentNullException.ThrowIfNull(managedClubs);
        using var scope = scopes.CreateScope();
        var s = scope.ServiceProvider;
        var today = clock.Today;
        var dismissed = 0;
        foreach (var (_, contract) in (await StaffHiring.StaffAsync(s, today, null, [StaffRole.HeadCoach], cancellationToken).ConfigureAwait(false))
                     .Where(x => !managedClubs.Contains(x.Contract.ClubId) && objectivesMet.TryGetValue(x.Contract.ClubId, out var met) && !met))
        {
            await DismissAsync(s, contract.Id, today, cancellationToken).ConfigureAwait(false);
            dismissed++;
        }

        await s.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dismissed;
    }

    private static async Task<long> DismissAsync(IServiceProvider s, ContractId contractId, DateOnly today, CancellationToken cancellationToken)
    {
        var contract = await Repo<Contract, ContractId>(s).FindAsync(contractId, cancellationToken).ConfigureAwait(false);
        if (contract?.StaffMemberId is not { } staffId)
        {
            throw new DomainException("This is not a staff contract.");
        }

        if (!contract.IsRunningOn(today))
        {
            throw new DomainValidationException("Only a running staff contract can be ended.");
        }

        var member = await Repo<StaffMember, StaffMemberId>(s).FindAsync(staffId, cancellationToken).ConfigureAwait(false);
        var club = (await Repo<Club, ClubId>(s).FindAsync(contract.ClubId, cancellationToken).ConfigureAwait(false))!;
        var paid = SalaryCalculator.RemainingSalary(contract, today);
        contract.Terminate(today, paid);
        Ledger.Book(s, club, today, FinanceCategory.Settlements, -paid, $"Dismissal of {member?.FullName ?? "a staff member"}");
        return paid;
    }

    private static string RoleName(StaffRole role) => Domain.Players.PlayerCatalog.DisplayName(role).ToLowerInvariant();

    private static IRepository<TEntity, TId> Repo<TEntity, TId>(IServiceProvider services)
        where TEntity : Entity<TId>
        where TId : struct, IEntityId<TId> =>
        services.GetRequiredService<IRepository<TEntity, TId>>();
}
