using System.Linq.Expressions;
using Ebm.Domain.Clubs;
using Ebm.Domain.Common;
using Ebm.Domain.Contracts;
using Ebm.Domain.Players;

namespace Ebm.Domain.Transfers;

/// <summary>How a player moved (docs/TRANSFERS.md §1).</summary>
public enum TransferKind
{
    /// <summary>A free agent signed a contract; no fee.</summary>
    FreeAgentSigning,

    /// <summary>Permanent move for a negotiated fee; the old contract ended as <see cref="ContractStatus.Transferred"/>.</summary>
    Transfer,

    /// <summary>Permanent move for the release clause of the old contract, which the selling club could not refuse.</summary>
    ReleaseClause,

    /// <summary>Temporary move until <see cref="Transfer.LoanEndDate"/>; the parent contract is <see cref="ContractStatus.OnLoan"/>.</summary>
    Loan,

    /// <summary>The player went back to his parent club at the end of a loan or when recalled.</summary>
    LoanReturn,
}

/// <summary>Loan limits shared by contracts and transfers.</summary>
public static class Loan
{
    /// <summary>Longest loan, in years.</summary>
    public const int MaxYears = 2;
}

/// <summary>
/// One player movement: a free-agent signing, a transfer, a loan or a loan return. Transfers are never deleted: they
/// are the transfer history of the player and of both clubs. Fees are in whole euros, paid by the receiving club to
/// the releasing club on the transfer date.
/// </summary>
/// <remarks>Lifecycle and rules: docs/TRANSFERS.md.</remarks>
public sealed class Transfer : Entity<TransferId>
{
    /// <summary>Upper bound for a fee in euros, to catch data-entry errors.</summary>
    public const long MaxFee = 1_000_000_000;

    private Transfer(TransferId id, PlayerId playerId, ClubId? fromClubId, ClubId toClubId, TransferKind kind, DateOnly date, long fee, ContractId? contractId)
        : base(id)
    {
        PlayerId = Guard.Required(playerId, nameof(playerId));
        FromClubId = Guard.Optional(fromClubId, nameof(fromClubId));
        ToClubId = Guard.Required(toClubId, nameof(toClubId));
        Kind = Guard.Defined(kind, "Transfer kind");
        Date = date;
        ContractId = Guard.Optional(contractId, nameof(contractId));
        if (fee < 0 || fee > MaxFee)
        {
            throw new DomainValidationException($"A transfer fee must be between 0 and {MaxFee:N0} euros.");
        }

        if (fromClubId == toClubId)
        {
            throw new DomainValidationException("A player cannot be transferred to the club he already plays for.");
        }

        Fee = fee;
    }

    private Transfer()
    {
    }

    public PlayerId PlayerId { get; private set; }

    /// <summary>Club the player left; <c>null</c> when he was a free agent.</summary>
    public ClubId? FromClubId { get; private set; }

    /// <summary>Club the player joined (for a loan return, the parent club).</summary>
    public ClubId ToClubId { get; private set; }

    public TransferKind Kind { get; private set; }

    /// <summary>Day the player joined <see cref="ToClubId"/>.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Fee paid by <see cref="ToClubId"/> to <see cref="FromClubId"/>, in euros; 0 for free agents and returns.</summary>
    public long Fee { get; private set; }

    /// <summary>The contract the player signed with <see cref="ToClubId"/> (for a loan, the loan contract); <c>null</c> for a loan return.</summary>
    public ContractId? ContractId { get; private set; }

    /// <summary>For loans and loan returns: the contract with the parent club that the player keeps.</summary>
    public ContractId? ParentContractId { get; private set; }

    /// <summary>For loans: the player's last day at the loan club as agreed.</summary>
    public DateOnly? LoanEndDate { get; private set; }

    /// <summary>For loans: the day the player went back to his parent club; <c>null</c> while he is out.</summary>
    public DateOnly? ReturnedOn { get; private set; }

    /// <summary>A loan whose player has not gone back yet.</summary>
    public bool IsOpenLoan => Kind == TransferKind.Loan && ReturnedOn is null;

    /// <summary>Loans whose player is still out, as a query the repositories can translate.</summary>
    public static Expression<Func<Transfer, bool>> OpenLoans => t => t.Kind == TransferKind.Loan && t.ReturnedOn == null;

    /// <summary>A free agent (or a player without a contract, leaving <paramref name="fromClubId"/>) signed with <paramref name="toClubId"/>.</summary>
    public static Transfer Signing(TransferId id, PlayerId playerId, ClubId? fromClubId, ClubId toClubId, DateOnly date, ContractId contractId) =>
        new(id, playerId, fromClubId, toClubId, TransferKind.FreeAgentSigning, date, 0, Guard.Required(contractId, nameof(contractId)));

    /// <summary>A permanent move for <paramref name="fee"/>; <paramref name="viaReleaseClause"/> when the fee was the old contract's release clause.</summary>
    public static Transfer Permanent(TransferId id, PlayerId playerId, ClubId fromClubId, ClubId toClubId, DateOnly date, long fee, ContractId contractId, bool viaReleaseClause) =>
        new(id, playerId, Guard.Required(fromClubId, nameof(fromClubId)), toClubId, viaReleaseClause ? TransferKind.ReleaseClause : TransferKind.Transfer, date, fee, Guard.Required(contractId, nameof(contractId)));

    /// <summary>A loan from <paramref name="date"/> to <paramref name="until"/> (last day at the loan club).</summary>
    public static Transfer OnLoan(TransferId id, PlayerId playerId, ClubId fromClubId, ClubId toClubId, DateOnly date, DateOnly until, long fee, ContractId loanContractId, ContractId parentContractId)
    {
        if (until <= date)
        {
            throw new DomainValidationException("A loan must end after the day it starts.");
        }

        if (until > date.AddYears(Loan.MaxYears))
        {
            throw new DomainValidationException($"A loan lasts at most {Loan.MaxYears} years.");
        }

        return new(id, playerId, Guard.Required(fromClubId, nameof(fromClubId)), toClubId, TransferKind.Loan, date, fee, Guard.Required(loanContractId, nameof(loanContractId)))
        {
            ParentContractId = Guard.Required(parentContractId, nameof(parentContractId)),
            LoanEndDate = until,
        };
    }

    /// <summary>The player went back from <paramref name="loanClubId"/> to <paramref name="parentClubId"/> on <paramref name="date"/>.</summary>
    public static Transfer LoanReturn(TransferId id, PlayerId playerId, ClubId loanClubId, ClubId parentClubId, DateOnly date, ContractId parentContractId) =>
        new(id, playerId, Guard.Required(loanClubId, nameof(loanClubId)), parentClubId, TransferKind.LoanReturn, date, 0, null)
        {
            ParentContractId = Guard.Required(parentContractId, nameof(parentContractId)),
        };

    /// <summary>Closes the loan: the player went back on <paramref name="date"/> (the day after the loan ended, or earlier when recalled).</summary>
    public void Return(DateOnly date)
    {
        if (Kind != TransferKind.Loan)
        {
            throw new DomainValidationException("Only a loan can be returned.");
        }

        if (ReturnedOn is not null)
        {
            throw new DomainValidationException($"The player already went back on {ReturnedOn:yyyy-MM-dd}.");
        }

        if (date <= Date)
        {
            throw new DomainValidationException($"A loan return cannot be before the loan started ({Date:yyyy-MM-dd}).");
        }

        ReturnedOn = date;
    }
}
