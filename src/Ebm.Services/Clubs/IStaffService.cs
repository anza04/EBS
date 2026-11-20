using Ebm.Domain.Clubs;
using Ebm.Domain.Contracts;
using Ebm.Domain.Staff;

namespace Ebm.Services.Clubs;

/// <summary>
/// Coaching and staff management (Step 28, docs/STAFF.md): a club's staff and responsibilities, the staff market,
/// hiring and dismissal, the market's supply and the AI clubs' head-coach decisions.
/// </summary>
public interface IStaffService
{
    /// <summary>Free staff per role on the market: 6 + one per 10 clubs.</summary>
    static int MarketSizeFor(int clubs) => 6 + (Math.Max(0, clubs) / 10);

    /// <summary>The club's staff under contract and who is responsible for what; <c>null</c> for an unknown club.</summary>
    Task<ClubStaff?> GetClubStaffAsync(ClubId clubId, CancellationToken cancellationToken = default);

    /// <summary>Free staff (optionally of one role), best first, with what each asks of <paramref name="clubId"/> and whether he would join it.</summary>
    Task<IReadOnlyList<StaffCandidate>> GetMarketAsync(ClubId clubId, StaffRole? role = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hires a free staff member from today for <paramref name="years"/> (1–5) at <paramref name="annualSalary"/>. He
    /// refuses a club too far below his reputation or an offer below his demand; a club employs at most
    /// <see cref="StaffRules.MaxPerRole"/> of a role.
    /// </summary>
    Task<Contract> HireAsync(ClubId clubId, StaffMemberId staffMemberId, long annualSalary, int years, CancellationToken cancellationToken = default);

    /// <summary>Dismisses a staff member from today; the club pays the remaining salary (booked as a settlement). Returns the amount paid.</summary>
    Task<long> DismissAsync(ContractId contractId, CancellationToken cancellationToken = default);

    /// <summary>Tops up the staff market (<see cref="MarketSizeFor"/>). Returns the number of staff added.</summary>
    Task<int> SupplyMarketAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// End of season: every AI club that missed its board objectives (<paramref name="objectivesMet"/> false) dismisses
    /// its head coach; the coaches are then rehired from the market. Returns the number dismissed.
    /// </summary>
    Task<int> ReviewHeadCoachesAsync(IReadOnlyDictionary<ClubId, bool> objectivesMet, IReadOnlyCollection<ClubId> managedClubs, CancellationToken cancellationToken = default);
}

/// <summary>A staff member under contract at a club.</summary>
public sealed record StaffLine(StaffMember Member, Contract Contract, int Age);

/// <summary>
/// A responsibility of the staff (docs/STAFF.md §5): the role in charge, the member leading it (the best one; <c>null</c>
/// when the club has none), the rating the game uses and what it influences.
/// </summary>
public sealed record StaffDuty(string Duty, StaffRole Role, StaffMember? Lead, int Rating, string Influence);

public sealed record ClubStaff(Club Club, IReadOnlyList<StaffLine> Members, IReadOnlyList<StaffDuty> Duties, long AnnualWages);

/// <summary>A free staff member as a club sees him.</summary>
public sealed record StaffCandidate(StaffMember Member, int Age, long Demand, bool WillJoin);
