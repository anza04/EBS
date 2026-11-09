using Ebm.Domain.Contracts;
using Ebm.Domain.Rosters;

namespace Ebm.Domain.Transfers;

/// <summary>What the selling club's side of a transfer negotiation knows (docs/TRANSFERS.md §2).</summary>
/// <param name="Overall">Current overall rating, 1–100.</param>
/// <param name="Potential">Potential rating, 1–100.</param>
/// <param name="Reputation">Player reputation, 1–100.</param>
/// <param name="ContractYearsLeft">Years left on the current contract (0 for a free agent).</param>
/// <param name="Role">The player's squad role at his club; <c>null</c> when unknown or without a club.</param>
/// <param name="ReleaseClause">Release clause of the current contract, if any.</param>
/// <param name="ClubInDebt">The selling club's balance is negative.</param>
/// <param name="ClubOverWageBudget">The selling club's wage bill exceeds its wage budget.</param>
public sealed record ValuationContext(
    int Overall,
    int Potential,
    int Reputation,
    int Age,
    double ContractYearsLeft,
    RosterRole? Role = null,
    long? ReleaseClause = null,
    bool ClubInDebt = false,
    bool ClubOverWageBudget = false);

/// <summary>How the market prices a player.</summary>
/// <param name="MarketValue">What the player is worth on the market, in euros.</param>
/// <param name="AskingFee">What his club wants for a permanent transfer (0 for a free agent).</param>
/// <param name="ReleaseClause">Fee the club cannot refuse, if the contract has one.</param>
/// <param name="AvailableForLoan">Whether his club would loan him out (not a starter or rotation player).</param>
/// <param name="LoanFeePerYear">Loan fee the club asks per year of loan.</param>
public sealed record PlayerValuation(long MarketValue, long AskingFee, long? ReleaseClause, bool AvailableForLoan, long LoanFeePerYear)
{
    /// <summary>The fee that completes a transfer: the release clause when it is below the asking fee.</summary>
    public long BuyNowFee => ReleaseClause is { } clause && clause < AskingFee ? clause : AskingFee;
}

public enum TransferOutcome
{
    Accepted,

    /// <summary>Close to what the club wants: <see cref="TransferResponse.CounterFee"/> is the fee it would accept.</summary>
    CounterOffer,
    Rejected,
}

/// <param name="AskingFee">The fee the club wants.</param>
/// <param name="CounterFee">For a counter-offer: the fee the club would accept.</param>
public sealed record TransferResponse(TransferOutcome Outcome, long AskingFee, long? CounterFee, string Message)
{
    public bool IsAccepted => Outcome == TransferOutcome.Accepted;
}

/// <summary>
/// The simplified transfer negotiation of Step 08 (docs/TRANSFERS.md §2): a player's market value follows his
/// market salary, age, potential and remaining contract; his club asks more for important players and less when
/// it needs money, and accepts, counters or rejects a fee. Deterministic: the same offer always gets the same answer.
/// </summary>
public sealed class TransferNegotiator
{
    public TransferNegotiator(ContractNegotiator salaries)
    {
        ArgumentNullException.ThrowIfNull(salaries);
        Salaries = salaries;
    }

    public static TransferNegotiator Default { get; } = new(ContractNegotiator.Default);

    /// <summary>Reputation of the hypothetical average club used to price a player's salary on the market.</summary>
    public const int NeutralClubReputation = 50;

    /// <summary>Lowest market value and asking fee, in euros.</summary>
    public const long MinimumValue = 10_000;

    /// <summary>The salary model the market value is based on.</summary>
    public ContractNegotiator Salaries { get; }

    /// <summary>Market value as a multiple of the player's market salary (before age, potential and contract factors).</summary>
    public double SalaryMultiple { get; init; } = 2.5;

    /// <summary>Offers worth at least this share of the asking fee get a counter-offer instead of a rejection.</summary>
    public double CounterThreshold { get; init; } = 0.8;

    /// <summary>Loan fee per year of loan, as a share of the market value.</summary>
    public double LoanFeeShare { get; init; } = 0.10;

    /// <summary>Extra asked for a starter (×1.5) and a rotation player (×1.25); an inactive player goes for less (×0.8).</summary>
    public double StarterPremium { get; init; } = 1.5;

    public double RotationPremium { get; init; } = 1.25;

    public double InactiveDiscount { get; init; } = 0.8;

    /// <summary>Discount a club in debt or over its wage budget accepts (each).</summary>
    public double NeedDiscount { get; init; } = 0.85;

    /// <summary>The player's market value, rounded to € 10,000.</summary>
    public long MarketValue(ValuationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var salary = Salaries.AskingSalary(new NegotiationContext(context.Overall, context.Potential, context.Reputation, context.Age, NeutralClubReputation));
        var value = salary * SalaryMultiple * AgeFactor(context.Age) * PotentialFactor(context) * ContractFactor(context.ContractYearsLeft);
        return Math.Max(MinimumValue, RoundTo(value, 10_000));
    }

    /// <summary>Prices the player: market value, what his club asks, and his loan terms.</summary>
    public PlayerValuation Value(ValuationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var market = MarketValue(context);
        var importance = context.Role switch
        {
            RosterRole.Starter => StarterPremium,
            RosterRole.Rotation => RotationPremium,
            RosterRole.Inactive => InactiveDiscount,
            _ => 1.0,
        };
        var need = (context.ClubInDebt ? NeedDiscount : 1.0) * (context.ClubOverWageBudget ? NeedDiscount : 1.0);
        var asking = Math.Max(MinimumValue, RoundTo(market * importance * need, 10_000));
        var loanable = context.Role is not (RosterRole.Starter or RosterRole.Rotation);
        return new PlayerValuation(market, asking, context.ReleaseClause, loanable, Math.Max(1_000, RoundTo(market * LoanFeeShare, 1_000)));
    }

    /// <summary>The selling club's answer to a transfer fee. A fee that meets the release clause cannot be refused.</summary>
    public TransferResponse EvaluateFee(PlayerValuation valuation, long fee)
    {
        ArgumentNullException.ThrowIfNull(valuation);
        if (valuation.ReleaseClause is { } clause && fee >= clause)
        {
            return new(TransferOutcome.Accepted, valuation.AskingFee, null, $"The fee meets the release clause of € {clause:N0}; the club cannot refuse.");
        }

        return Answer(valuation.AskingFee, fee, "transfer fee");
    }

    /// <summary>The parent club's answer to a loan request for <paramref name="loanYears"/> years at <paramref name="fee"/>.</summary>
    public TransferResponse EvaluateLoan(PlayerValuation valuation, long fee, double loanYears)
    {
        ArgumentNullException.ThrowIfNull(valuation);
        var asking = Math.Max(1_000, RoundTo(valuation.LoanFeePerYear * Math.Max(0.25, loanYears), 1_000));
        if (!valuation.AvailableForLoan)
        {
            return new(TransferOutcome.Rejected, asking, null, "The club will not loan out a player in its starting five or rotation.");
        }

        return Answer(asking, fee, "loan fee");
    }

    /// <summary>Years of loan from <paramref name="from"/> to <paramref name="until"/> inclusive.</summary>
    public static double LoanYears(DateOnly from, DateOnly until) => (until.DayNumber - from.DayNumber + 1) / 365.25;

    private TransferResponse Answer(long asking, long fee, string what)
    {
        if (fee >= asking)
        {
            return new(TransferOutcome.Accepted, asking, null, $"The club accepts the {what}.");
        }

        if (fee >= CounterThreshold * asking)
        {
            return new(TransferOutcome.CounterOffer, asking, asking, $"The club wants more: it would accept a {what} of € {asking:N0}.");
        }

        return new(TransferOutcome.Rejected, asking, null, $"The offer is far below what the club wants (about € {asking:N0}).");
    }

    private static double AgeFactor(int age) => age switch
    {
        <= 22 => 1.4,
        <= 25 => 1.25,
        <= 28 => 1.1,
        <= 30 => 1.0,
        <= 32 => 0.75,
        <= 34 => 0.55,
        _ => 0.4,
    };

    private static double PotentialFactor(ValuationContext context) => 1 + Math.Min(0.3, 0.01 * Math.Max(0, context.Potential - context.Overall));

    /// <summary>Half a year left: ×0.625; one year: ×0.75; two or more: ×1.</summary>
    private static double ContractFactor(double yearsLeft) => 0.5 + (0.25 * Math.Clamp(yearsLeft, 0, 2));

    private static long RoundTo(double value, long unit) => (long)Math.Round(value / unit, MidpointRounding.AwayFromZero) * unit;
}
