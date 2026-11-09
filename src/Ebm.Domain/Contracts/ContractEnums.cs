namespace Ebm.Domain.Contracts;

/// <summary>How often the salary is paid. Each instalment covers the period that ends on its due date.</summary>
public enum PaymentFrequency
{
    Monthly,
    Quarterly,
    Annually,
}

/// <summary>
/// Stored lifecycle state of a contract (docs/CONTRACTS.md §2). Whether an active contract is upcoming, running or
/// past its end date follows from its dates; see <see cref="Contract.StatusOn"/>.
/// </summary>
public enum ContractStatus
{
    /// <summary>Signed and not ended early. It runs from its start date to its end date.</summary>
    Active,

    /// <summary>Ran to its end date; the player became a free agent unless he had another contract.</summary>
    Expired,

    /// <summary>Ended early by the club or by mutual agreement; see <see cref="Contract.TerminationDate"/>.</summary>
    Terminated,

    /// <summary>Replaced by a renewal with the same club. If the renewal started early, it ended on the renewal's start date.</summary>
    Renewed,
}

/// <summary>Who decides whether an option year is taken up.</summary>
public enum OptionHolder
{
    Club,
    Player,
}

/// <summary>How a contract is ended early.</summary>
public enum TerminationKind
{
    /// <summary>The club releases the player and pays the remaining salary in full.</summary>
    ClubRelease,

    /// <summary>Club and player agree on a settlement (by default half the remaining salary).</summary>
    MutualAgreement,
}
