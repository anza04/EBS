namespace Ebm.Domain.Contracts;

/// <summary>What the player's side of a negotiation knows about the player and the club.</summary>
/// <param name="Overall">Current overall rating, 1–100.</param>
/// <param name="Potential">Potential rating, 1–100.</param>
/// <param name="Reputation">Player reputation, 1–100.</param>
/// <param name="ClubReputation">Reputation of the club making the offer, 1–100.</param>
/// <param name="CurrentSalary">Annual salary of the contract being renewed or replaced, if any.</param>
public sealed record NegotiationContext(int Overall, int Potential, int Reputation, int Age, int ClubReputation, long? CurrentSalary = null);

public enum NegotiationOutcome
{
    Accepted,

    /// <summary>Close to what the player wants: <see cref="NegotiationResponse.CounterOffer"/> holds terms he would accept.</summary>
    CounterOffer,
    Rejected,
}

/// <param name="AskingSalary">The annual value the player wants (salary, or salary plus the yearly share of the signing bonus and other perks).</param>
/// <param name="OfferValue">The annual value of the offer as the player sees it.</param>
public sealed record NegotiationResponse(NegotiationOutcome Outcome, long AskingSalary, long OfferValue, ContractTerms? CounterOffer, string Message)
{
    public bool IsAccepted => Outcome == NegotiationOutcome.Accepted;
}

/// <summary>
/// The simplified contract negotiation of Step 07 (docs/CONTRACTS.md §4): the player has an asking salary from his
/// rating, reputation, age and the club's prestige, values an offer per year, and accepts, counters or rejects.
/// Deterministic: the same offer always gets the same answer. Transfers and AI clubs (Step 08) reuse it.
/// </summary>
public sealed class ContractNegotiator
{
    /// <summary>Longest contract a player signs in a negotiation.</summary>
    public const int MaxYears = 5;

    /// <summary>Lowest annual salary any player accepts, in euros.</summary>
    public const long MinimumSalary = 15_000;

    public static ContractNegotiator Default { get; } = new();

    /// <summary>Salary at overall 40; it grows by <see cref="GrowthPerPoint"/> (exponentially) per overall point.</summary>
    public long BaseSalary { get; init; } = 30_000;

    public double GrowthPerPoint { get; init; } = 0.08;

    /// <summary>Offers worth at least this share of the asking value get a counter-offer instead of a rejection.</summary>
    public double CounterThreshold { get; init; } = 0.75;

    /// <summary>Value of a player option (+) or a club option (−) as a share of the salary.</summary>
    public double OptionValue { get; init; } = 0.05;

    /// <summary>Share of the season bonuses a player counts as part of the salary (they are not guaranteed).</summary>
    public double BonusWeight { get; init; } = 0.25;

    /// <summary>The annual salary the player asks for, rounded to the nearest thousand euros.</summary>
    public long AskingSalary(NegotiationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var salary = BaseSalary * Math.Exp(GrowthPerPoint * (Clamp(context.Overall) - 40));

        // Better known players ask more (×0.85 to ×1.15); prestigious clubs pay less (×1.10 to ×0.90).
        salary *= 0.85 + (0.30 * Clamp(context.Reputation) / 100);
        salary *= 1.10 - (0.20 * Clamp(context.ClubReputation) / 100);

        // Young players price in their potential (up to +15%); veterans accept less from 32 (−5% a year, to −40%).
        if (context.Age <= 23 && context.Potential > context.Overall)
        {
            salary *= 1 + Math.Min(0.15, 0.005 * (context.Potential - context.Overall));
        }
        else if (context.Age >= 32)
        {
            salary *= Math.Max(0.6, 1 - (0.05 * (context.Age - 31)));
        }

        // A player rarely accepts a large pay cut on renewal: at least 90% of his current salary.
        if (context.CurrentSalary is { } current)
        {
            salary = Math.Max(salary, 0.9 * current);
        }

        return Math.Max(MinimumSalary, RoundThousands(salary));
    }

    /// <summary>The annual value of an offer to the player: salary, plus the signing bonus spread over the years, plus perks.</summary>
    public long OfferValue(ContractTerms offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        // Lengths are counted in quarter years, so a two-year deal spanning a leap day still counts as two years.
        var years = Math.Max(1.0, Math.Round(offer.Years * 4) / 4);
        var value = offer.AnnualSalary + (offer.SigningBonus / years) + (BonusWeight * offer.PerformanceBonuses.SeasonTotal);
        if (offer.PlayerOptionUntil is not null)
        {
            value += OptionValue * offer.AnnualSalary;
        }

        if (offer.ClubOptionUntil is not null)
        {
            value -= OptionValue * offer.AnnualSalary;
        }

        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    /// <summary>The player's answer to <paramref name="offer"/>.</summary>
    public NegotiationResponse Evaluate(NegotiationContext context, ContractTerms offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var asking = AskingSalary(context);
        var value = OfferValue(offer);
        if (offer.EndDate <= offer.StartDate || offer.Years > MaxYears + 0.01)
        {
            return new(NegotiationOutcome.Rejected, asking, value, null, $"The player signs for at most {MaxYears} years.");
        }

        if (value >= asking)
        {
            return new(NegotiationOutcome.Accepted, asking, value, null, "The player accepts the offer.");
        }

        if (value >= CounterThreshold * asking)
        {
            // Raise the salary until the offer is worth the asking value; the other terms stay as offered.
            var counter = offer with { AnnualSalary = offer.AnnualSalary + RoundUpThousands((asking - value) / ScaleOf(offer)) };
            return new(
                NegotiationOutcome.CounterOffer,
                asking,
                value,
                counter,
                $"The player wants more. He would sign for € {counter.AnnualSalary:N0} a year on the same terms.");
        }

        return new(NegotiationOutcome.Rejected, asking, value, null, $"The offer is far below what the player expects (about € {asking:N0} a year).");
    }

    /// <summary>
    /// Whether the player takes up his option: he does when the contract pays at least what he would ask on the market.
    /// </summary>
    public bool ExercisesPlayerOption(NegotiationContext context, long currentSalary) =>
        currentSalary >= AskingSalary(context with { CurrentSalary = null });

    // How much the annual value grows per euro of salary (options are a share of the salary).
    private double ScaleOf(ContractTerms offer) =>
        1 + (offer.PlayerOptionUntil is null ? 0 : OptionValue) - (offer.ClubOptionUntil is null ? 0 : OptionValue);

    private static int Clamp(int rating) => Math.Clamp(rating, 1, 100);

    private static long RoundThousands(double value) => (long)Math.Round(value / 1000, MidpointRounding.AwayFromZero) * 1000;

    private static long RoundUpThousands(double value) => (long)Math.Ceiling(value / 1000) * 1000;
}
