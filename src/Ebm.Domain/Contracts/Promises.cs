using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Players;

namespace Ebm.Domain.Contracts;

/// <summary>The role a player has, or is promised, in his club's squad (docs/CONTRACTS.md §4.3). Ordered from least to most.</summary>
public enum SquadRole
{
    Reserve,
    Prospect,
    Rotation,
    Starter,
    Star,
}

/// <summary>How a contract's promises are being kept (docs/CONTRACTS.md §4.6).</summary>
public enum PromiseStatus
{
    /// <summary>No promise, or not reviewed yet.</summary>
    None,
    Kept,
    AtRisk,
    Broken,
}

/// <summary>Playing-time promises made in a contract: a squad role, minutes per game, or both.</summary>
public sealed record ContractPromises(SquadRole? Role = null, int? MinutesPerGame = null)
{
    public const int MaxMinutes = 40;

    public bool IsEmpty => Role is null && MinutesPerGame is null;

    /// <summary>The minutes per game the player expects from these promises (the larger of the two).</summary>
    public int TargetMinutes => Math.Max(Role is { } role ? PromiseRules.Minutes(role) : 0, MinutesPerGame ?? 0);
}

/// <summary>Squad roles, the playing time they mean and how promises are judged (docs/CONTRACTS.md §4.3, §4.6).</summary>
public static class PromiseRules
{
    /// <summary>Days between two reviews of a contract's promises; also the window of games reviewed.</summary>
    public const int ReviewDays = 28;

    /// <summary>Fewest club games in the window for a review to count (off-season windows are skipped).</summary>
    public const int MinGames = 3;

    public const double KeptShare = 0.85;
    public const double BrokenShare = 0.6;

    /// <summary>Morale change after each review.</summary>
    public const int KeptMorale = 3;
    public const int AtRiskMorale = -3;
    public const int BrokenMorale = -8;

    /// <summary>Typical minutes per game of a role.</summary>
    public static int Minutes(SquadRole role) => role switch
    {
        SquadRole.Star => 30,
        SquadRole.Starter => 25,
        SquadRole.Rotation => 17,
        SquadRole.Prospect => 12,
        _ => 8,
    };

    /// <summary>
    /// The role a player of rank <paramref name="rank"/> (1 = best overall at the club, counting him) expects: 1–2 star,
    /// 3–5 starter, 6–8 rotation, then reserve; a player aged 21 or less below the starters is a prospect.
    /// </summary>
    public static SquadRole Expected(int rank, int age) => rank switch
    {
        <= 2 => SquadRole.Star,
        <= 5 => SquadRole.Starter,
        _ when age <= 21 => SquadRole.Prospect,
        <= 8 => SquadRole.Rotation,
        _ => SquadRole.Reserve,
    };

    /// <summary>Kept at 85% of the promised minutes or more, broken below 60%, at risk between.</summary>
    public static PromiseStatus Assess(int targetMinutes, double averageMinutes) =>
        targetMinutes <= 0 ? PromiseStatus.Kept
        : averageMinutes >= KeptShare * targetMinutes ? PromiseStatus.Kept
        : averageMinutes >= BrokenShare * targetMinutes ? PromiseStatus.AtRisk
        : PromiseStatus.Broken;

    public static int MoraleChange(PromiseStatus status) => status switch
    {
        PromiseStatus.Kept => KeptMorale,
        PromiseStatus.AtRisk => AtRiskMorale,
        PromiseStatus.Broken => BrokenMorale,
        _ => 0,
    };
}

public readonly record struct ContractNegotiationId(Guid Value) : IEntityId<ContractNegotiationId>
{
    public static ContractNegotiationId New() => new(Guid.CreateVersion7());

    public static ContractNegotiationId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}

public enum NegotiationStatus
{
    Open,
    Agreed,
    BrokenOff,
}

/// <summary>
/// Contract talks between a club and a player over several rounds (docs/CONTRACTS.md §4.5). Every offer is a round;
/// a counter-offer costs one point of patience, a rejection two. Out of patience, the player breaks off the talks
/// for <see cref="CoolingOffDays"/> days. Talks idle for <see cref="IdleDays"/> days start again from scratch.
/// </summary>
public sealed class ContractNegotiation : Entity<ContractNegotiationId>
{
    public const int CoolingOffDays = 30;
    public const int IdleDays = 30;

    public ContractNegotiation(ContractNegotiationId id, PlayerId playerId, ClubId clubId, ContractId? renewalOf, DateOnly today, int patience)
        : base(id)
    {
        PlayerId = Guard.Required(playerId, nameof(playerId));
        ClubId = Guard.Required(clubId, nameof(clubId));
        RenewalOfId = renewalOf;
        StartedOn = today;
        LastActivityOn = today;
        Patience = Guard.Range(patience, 1, 10, "Patience");
    }

    private ContractNegotiation()
    {
    }

    public PlayerId PlayerId { get; private set; }

    public ClubId ClubId { get; private set; }

    /// <summary>The contract being renewed; <c>null</c> for a new contract.</summary>
    public ContractId? RenewalOfId { get; private set; }

    public DateOnly StartedOn { get; private set; }

    public DateOnly LastActivityOn { get; private set; }

    public int Rounds { get; private set; }

    /// <summary>Rounds the player still tolerates without agreement.</summary>
    public int Patience { get; private set; }

    public NegotiationStatus Status { get; private set; }

    public DateOnly? ClosedOn { get; private set; }

    /// <summary>First day the player talks to the club again after breaking off.</summary>
    public DateOnly? BrokenOffUntil => Status == NegotiationStatus.BrokenOff && ClosedOn is { } closed ? closed.AddDays(CoolingOffDays) : null;

    /// <summary>Open and not idle on <paramref name="today"/>.</summary>
    public bool IsOpenOn(DateOnly today) => Status == NegotiationStatus.Open && LastActivityOn.AddDays(IdleDays) >= today;

    /// <summary>Broken off and still cooling off on <paramref name="today"/>.</summary>
    public bool RefusesTalksOn(DateOnly today) => BrokenOffUntil is { } until && today < until;

    /// <summary>Counts an offer and its answer; returns <c>true</c> when the player breaks off the talks.</summary>
    public bool RecordRound(DateOnly today, NegotiationOutcome outcome)
    {
        RequireOpen();
        Rounds++;
        LastActivityOn = today;
        Patience -= outcome switch
        {
            NegotiationOutcome.Rejected => 2,
            NegotiationOutcome.CounterOffer => 1,
            _ => 0,
        };
        if (Patience > 0)
        {
            return false;
        }

        Patience = 0;
        Status = NegotiationStatus.BrokenOff;
        ClosedOn = today;
        return true;
    }

    public void Agree(DateOnly today)
    {
        RequireOpen();
        Status = NegotiationStatus.Agreed;
        LastActivityOn = today;
        ClosedOn = today;
    }

    private void RequireOpen()
    {
        if (Status != NegotiationStatus.Open)
        {
            throw new DomainValidationException("These contract talks are over.");
        }
    }
}
