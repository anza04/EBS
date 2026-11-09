using System.Linq.Expressions;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;
using Ebm.Domain.Staff;

namespace Ebm.Domain.Contracts;

/// <summary>
/// An employment contract between a club and exactly one person: a player or a staff member.
/// Contracts are never deleted: expired, terminated and renewed contracts are the contract history.
/// </summary>
/// <remarks>Lifecycle and rules: docs/CONTRACTS.md.</remarks>
public sealed class Contract : Entity<ContractId>
{
    /// <summary>Upper bound for an annual salary in euros, to catch data-entry errors.</summary>
    public const long MaxAnnualSalary = 100_000_000;

    /// <summary>Upper bound for a release clause in euros.</summary>
    public const long MaxReleaseClause = 1_000_000_000;

    /// <summary>Longest contract the database accepts (data sanity; negotiations offer at most <see cref="ContractNegotiator.MaxYears"/>).</summary>
    public const int MaxLengthYears = 10;

    /// <summary>An option may extend a contract by at most this many years.</summary>
    public const int MaxOptionYears = 3;

    private Contract(ContractId id, ClubId clubId, PlayerId? playerId, StaffMemberId? staffMemberId, ContractTerms terms)
        : base(id)
    {
        ClubId = Guard.Required(clubId, nameof(clubId));
        PlayerId = Guard.Optional(playerId, nameof(playerId));
        StaffMemberId = Guard.Optional(staffMemberId, nameof(staffMemberId));
        UpdateTerms(terms);
    }

    private Contract()
    {
    }

    public ClubId ClubId { get; private set; }

    /// <summary>Set for player contracts; exactly one of <see cref="PlayerId"/> and <see cref="StaffMemberId"/> is set.</summary>
    public PlayerId? PlayerId { get; private set; }

    /// <summary>Set for staff contracts; exactly one of <see cref="PlayerId"/> and <see cref="StaffMemberId"/> is set.</summary>
    public StaffMemberId? StaffMemberId { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>Last day of the contract as agreed (extended when an option is taken up). An early end is <see cref="TerminationDate"/>.</summary>
    public DateOnly EndDate { get; private set; }

    /// <summary>Gross annual salary in whole euros.</summary>
    public long AnnualSalary { get; private set; }

    public PaymentFrequency PaymentFrequency { get; private set; }

    /// <summary>One-off payment to the player on signing, in euros.</summary>
    public long SigningBonus { get; private set; }

    public PerformanceBonuses PerformanceBonuses { get; private set; } = PerformanceBonuses.None;

    /// <summary>Buy-out fee for other clubs, in euros; <c>null</c> when there is none.</summary>
    public long? ReleaseClause { get; private set; }

    /// <summary>Date the club may extend the contract to; <c>null</c> when there is no (longer a) club option.</summary>
    public DateOnly? ClubOptionUntil { get; private set; }

    /// <summary>Date the player may extend the contract to; <c>null</c> when there is no (longer a) player option.</summary>
    public DateOnly? PlayerOptionUntil { get; private set; }

    public ContractStatus Status { get; private set; }

    /// <summary>
    /// First day the contract no longer runs because it was terminated or renewed early; <c>null</c> otherwise.
    /// Equal to <see cref="StartDate"/> when the contract was cancelled before it started.
    /// </summary>
    public DateOnly? TerminationDate { get; private set; }

    /// <summary>Settlement the club paid the player when the contract was terminated, in euros.</summary>
    public long TerminationCompensation { get; private set; }

    /// <summary>Last day the contract runs: the day before an early end, else <see cref="EndDate"/>.</summary>
    public DateOnly EffectiveEndDate => TerminationDate is { } ended ? ended.AddDays(-1) : EndDate;

    /// <summary>The negotiable terms as stored.</summary>
    public ContractTerms Terms => new(
        StartDate, EndDate, AnnualSalary, PaymentFrequency, SigningBonus, PerformanceBonuses, ReleaseClause, ClubOptionUntil, PlayerOptionUntil);

    public static Contract ForPlayer(ContractId id, ClubId clubId, PlayerId playerId, ContractTerms terms) =>
        new(id, clubId, Guard.Required(playerId, nameof(playerId)), null, terms);

    public static Contract ForPlayer(ContractId id, ClubId clubId, PlayerId playerId, DateOnly startDate, DateOnly endDate, long annualSalary) =>
        ForPlayer(id, clubId, playerId, new ContractTerms(startDate, endDate, annualSalary));

    public static Contract ForStaff(ContractId id, ClubId clubId, StaffMemberId staffMemberId, DateOnly startDate, DateOnly endDate, long annualSalary) =>
        new(id, clubId, null, Guard.Required(staffMemberId, nameof(staffMemberId)), new ContractTerms(startDate, endDate, annualSalary));

    /// <summary>A contract that runs on <paramref name="date"/>, as a query the repositories can translate.</summary>
    public static Expression<Func<Contract, bool>> RunningOn(DateOnly date) =>
        c => c.StartDate <= date && c.EndDate >= date && (c.TerminationDate == null || c.TerminationDate > date);

    /// <summary>A contract that runs on <paramref name="date"/> or will run later, as a translatable query.</summary>
    public static Expression<Func<Contract, bool>> RunningOrUpcomingOn(DateOnly date) =>
        c => c.EndDate >= date && (c.TerminationDate == null || (c.TerminationDate > date && c.TerminationDate > c.StartDate));

    public bool IsRunningOn(DateOnly date) => StartDate <= date && date <= EffectiveEndDate;

    /// <summary>Signed but not started yet (and not cancelled).</summary>
    public bool IsUpcomingOn(DateOnly date) => date < StartDate && EffectiveEndDate >= StartDate;

    /// <summary>The status as of <paramref name="date"/>: an active contract past its end date is reported as expired.</summary>
    public ContractStatus StatusOn(DateOnly date) =>
        Status == ContractStatus.Active && date > EndDate ? ContractStatus.Expired : Status;

    /// <summary>Changes dates and salary and keeps the other terms. Club and person never change: a new deal is a new contract.</summary>
    public void UpdateTerms(DateOnly startDate, DateOnly endDate, long annualSalary) =>
        UpdateTerms(Terms with { StartDate = startDate, EndDate = endDate, AnnualSalary = annualSalary });

    /// <summary>Replaces every negotiable term (editor and import). Lifecycle changes go through the methods below.</summary>
    public void UpdateTerms(ContractTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        CheckMoney(terms.AnnualSalary, MaxAnnualSalary, "Annual salary");
        CheckMoney(terms.SigningBonus, MaxAnnualSalary, "Signing bonus");
        Guard.Defined(terms.PaymentFrequency, "Payment frequency");
        Guard.After(terms.EndDate, terms.StartDate, "Contract end date");
        if (terms.EndDate > terms.StartDate.AddYears(MaxLengthYears))
        {
            throw new DomainValidationException($"A contract can last at most {MaxLengthYears} years.");
        }

        if (terms.ReleaseClause is { } clause && clause is <= 0 or > MaxReleaseClause)
        {
            throw new DomainValidationException($"Release clause must be between 1 and {MaxReleaseClause:N0} euros.");
        }

        CheckOption(terms.ClubOptionUntil, terms.EndDate, "club");
        CheckOption(terms.PlayerOptionUntil, terms.EndDate, "player");
        if (TerminationDate is { } ended && (ended < terms.StartDate || ended > terms.EndDate.AddDays(1)))
        {
            throw new DomainValidationException($"This contract ended early on {ended:yyyy-MM-dd}; its dates must include that day.");
        }

        StartDate = terms.StartDate;
        EndDate = terms.EndDate;
        AnnualSalary = terms.AnnualSalary;
        PaymentFrequency = terms.PaymentFrequency;
        SigningBonus = terms.SigningBonus;
        PerformanceBonuses = terms.PerformanceBonuses;
        ReleaseClause = terms.ReleaseClause;
        ClubOptionUntil = terms.ClubOptionUntil;
        PlayerOptionUntil = terms.PlayerOptionUntil;
    }

    /// <summary>
    /// Ends the contract early from <paramref name="effectiveDate"/> (the first day it no longer runs). A contract that
    /// has not started is cancelled. <paramref name="compensation"/> is what the club pays the player.
    /// </summary>
    public void Terminate(DateOnly effectiveDate, long compensation)
    {
        RequireActive("terminated");
        if (effectiveDate > EndDate)
        {
            throw new DomainValidationException($"The contract already ran out on {EndDate:yyyy-MM-dd}.");
        }

        CheckMoney(compensation, MaxAnnualSalary * MaxLengthYears, "Compensation");
        TerminationDate = effectiveDate < StartDate ? StartDate : effectiveDate;
        TerminationCompensation = compensation;
        Status = ContractStatus.Terminated;
    }

    /// <summary>
    /// Marks the contract as replaced by a renewal with the same club starting on <paramref name="renewalStart"/>.
    /// A renewal starting before the end date ends this contract early; a renewal must follow on without a gap.
    /// </summary>
    public void Supersede(DateOnly renewalStart)
    {
        RequireActive("renewed");
        if (renewalStart <= StartDate || renewalStart > EndDate.AddDays(1))
        {
            throw new DomainValidationException(
                $"A renewal must start after {StartDate:yyyy-MM-dd} and no later than {EndDate.AddDays(1):yyyy-MM-dd}, the day after the current contract ends.");
        }

        if (renewalStart <= EndDate)
        {
            TerminationDate = renewalStart;
        }

        Status = ContractStatus.Renewed;
    }

    /// <summary>Records that the contract ran out; <paramref name="today"/> must be after the end date.</summary>
    public void Expire(DateOnly today)
    {
        RequireActive("expired");
        if (today <= EndDate)
        {
            throw new DomainValidationException($"The contract runs until {EndDate:yyyy-MM-dd}.");
        }

        Status = ContractStatus.Expired;
    }

    public DateOnly? OptionUntil(OptionHolder holder) => holder == OptionHolder.Club ? ClubOptionUntil : PlayerOptionUntil;

    /// <summary>Takes up an option: the contract is extended to the option date. Decided on or before the end date.</summary>
    public void ExerciseOption(OptionHolder holder, DateOnly today)
    {
        var until = RequireOption(holder, today);
        EndDate = until;
        ClearOption(holder);

        // An option that no longer extends the contract is spent.
        var other = holder == OptionHolder.Club ? OptionHolder.Player : OptionHolder.Club;
        if (OptionUntil(other) is { } otherUntil && otherUntil <= EndDate)
        {
            ClearOption(other);
        }
    }

    /// <summary>Declines an option: the contract keeps its end date.</summary>
    public void DeclineOption(OptionHolder holder, DateOnly today)
    {
        RequireOption(holder, today);
        ClearOption(holder);
    }

    private DateOnly RequireOption(OptionHolder holder, DateOnly today)
    {
        Guard.Defined(holder, "Option holder");
        RequireActive("extended");
        if (OptionUntil(holder) is not { } until)
        {
            throw new DomainValidationException($"The contract has no {holder.ToString().ToLowerInvariant()} option.");
        }

        if (today > EndDate)
        {
            throw new DomainValidationException($"The option had to be decided by {EndDate:yyyy-MM-dd}, when the contract ended.");
        }

        return until;
    }

    private void ClearOption(OptionHolder holder)
    {
        if (holder == OptionHolder.Club)
        {
            ClubOptionUntil = null;
        }
        else
        {
            PlayerOptionUntil = null;
        }
    }

    private void RequireActive(string action)
    {
        if (Status != ContractStatus.Active)
        {
            throw new DomainValidationException($"The contract is {Status.ToString().ToLowerInvariant()} and cannot be {action}.");
        }
    }

    private static void CheckMoney(long value, long max, string name)
    {
        if (value < 0 || value > max)
        {
            throw new DomainValidationException($"{name} must be between 0 and {max:N0} euros.");
        }
    }

    private static void CheckOption(DateOnly? until, DateOnly endDate, string holder)
    {
        if (until is not { } date)
        {
            return;
        }

        if (date <= endDate || date > endDate.AddYears(MaxOptionYears))
        {
            throw new DomainValidationException(
                $"The {holder} option must extend the contract past {endDate:yyyy-MM-dd} by at most {MaxOptionYears} years.");
        }
    }
}
