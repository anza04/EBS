namespace Ebm.Domain.Contracts;

/// <summary>What the player's side of a negotiation knows about the player and the club.</summary>
/// <param name="Overall">Current overall rating, 1–100.</param>
/// <param name="Potential">Potential rating, 1–100.</param>
/// <param name="Reputation">Player reputation, 1–100.</param>
/// <param name="ClubReputation">Reputation of the club making the offer, 1–100.</param>
/// <param name="CurrentSalary">Annual salary of the contract being renewed or replaced, if any.</param>
/// <param name="ExpectedRole">The role he expects at the club from his rank in its squad; <c>null</c> when unknown (promises are then worth nothing).</param>
/// <param name="AgentToughness">His agent's toughness, 1–100 (50 = average, also for players without an agent).</param>
/// <param name="Relationship">His agent's relationship with the club, −100…100.</param>
/// <param name="CompetingOffers">Other clubs bidding for him (docs/CONTRACTS.md §4.4).</param>
/// <param name="BrokenPromise">The club broke a promise in his current contract.</param>
/// <param name="Round">The negotiation round this offer is (1 = first).</param>
public sealed record NegotiationContext(
    int Overall,
    int Potential,
    int Reputation,
    int Age,
    int ClubReputation,
    long? CurrentSalary = null,
    SquadRole? ExpectedRole = null,
    int AgentToughness = 50,
    int Relationship = 0,
    int CompetingOffers = 0,
    bool BrokenPromise = false,
    int Round = 1);

/// <summary>
/// What the player wants from a club (docs/CONTRACTS.md §4.2).
/// </summary>
/// <param name="AskingSalary">Annual value he wants: the market salary with the premiums for competing offers, the agent relationship and broken promises.</param>
/// <param name="MarketSalary">His market salary at the club (<see cref="ContractNegotiator.AskingSalary"/>).</param>
/// <param name="MinimumValue">The least annual value he accepts in this round (rounds bring small concessions).</param>
/// <param name="MinYears">Shortest length he accepts without a penalty.</param>
/// <param name="MaxYears">Longest length he accepts without a penalty.</param>
/// <param name="RequiredRole">A role he insists on being promised, or <c>null</c>.</param>
/// <param name="MaxReleaseClause">He insists on a release clause of at most this fee, or <c>null</c>.</param>
/// <param name="RejectBelow">Offers worth less than this are rejected outright instead of countered.</param>
/// <param name="Patience">Rounds he tolerates in new talks.</param>
public sealed record PlayerDemands(
    long AskingSalary,
    long MarketSalary,
    long MinimumValue,
    int MinYears,
    int MaxYears,
    SquadRole? ExpectedRole,
    SquadRole? RequiredRole,
    long? MaxReleaseClause,
    long RejectBelow,
    int Patience);

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

    /// <summary>What the player wants (Step 25); <c>null</c> for answers that do not come from the contract negotiation.</summary>
    public PlayerDemands? Demands { get; init; }

    /// <summary>The state of the talks, when the answer was a counted round (docs/CONTRACTS.md §4.5).</summary>
    public NegotiationTalks? Talks { get; init; }
}

/// <summary>Who represents the player and how the talks stand.</summary>
/// <param name="Round">Rounds so far (0 before the first offer).</param>
/// <param name="Patience">Rounds left before the player breaks off.</param>
/// <param name="BrokenOffUntil">Set when the player refuses to talk until that day.</param>
public sealed record NegotiationTalks(
    string? Agent,
    int AgentToughness,
    int Relationship,
    int Round,
    int Patience,
    DateOnly? BrokenOffUntil,
    IReadOnlyList<CompetingOffer> CompetingOffers);

/// <summary>A rival club bidding for the player, with the annual salary it would offer him.</summary>
public sealed record CompetingOffer(Clubs.ClubId ClubId, string Club, long AnnualSalary);

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

    /// <summary>Premium on the asking value per competing offer, at most <see cref="MaxCompetingOffers"/> offers.</summary>
    public double CompetitionPremium { get; init; } = 0.04;

    public const int MaxCompetingOffers = 3;

    /// <summary>Asking value change at the extreme relationships (−5% trusted … +5% hostile).</summary>
    public double RelationshipEffect { get; init; } = 0.05;

    /// <summary>Premium a player asks a club that broke a promise to him.</summary>
    public double BrokenPromisePremium { get; init; } = 0.10;

    /// <summary>Concession per round after the first (at most three rounds).</summary>
    public double RoundConcession { get; init; } = 0.01;

    /// <summary>Value penalty, as a share of the asking value, per year outside the preferred lengths.</summary>
    public double LengthPenalty { get; init; } = 0.07;

    /// <summary>Value of a promise per role level above the expected role, and per minute above its minutes; capped at <see cref="MaxPromiseValue"/>.</summary>
    public double RolePromiseValue { get; init; } = 0.04;

    public double MinutePromiseValue { get; init; } = 0.005;

    public double MaxPromiseValue { get; init; } = 0.10;

    /// <summary>Players from this reputation insist on being promised their expected role when it is starter or star.</summary>
    public int RoleDemandReputation { get; init; } = 60;

    /// <summary>Players aged 30 or less whose reputation exceeds the club's by this much insist on a release clause.</summary>
    public int ClauseDemandGap { get; init; } = 25;

    /// <summary>The highest release clause such a player accepts, in years of asking value.</summary>
    public double ClauseMultiple { get; init; } = 20;

    /// <summary>Patience of new talks with an average agent and a neutral relationship.</summary>
    public int BasePatience { get; init; } = 4;

    /// <summary>The player's demands to <paramref name="context"/>'s club (docs/CONTRACTS.md §4.2).</summary>
    public PlayerDemands Demands(NegotiationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var market = AskingSalary(context);
        var factor = 1
            + (Math.Clamp(context.CompetingOffers, 0, MaxCompetingOffers) * CompetitionPremium)
            - (RelationshipEffect * Math.Clamp(context.Relationship, AgentRelationship.Min, AgentRelationship.Max) / 100)
            + (context.BrokenPromise ? BrokenPromisePremium : 0);
        var asking = Math.Max(MinimumSalary, RoundThousands(market * factor));
        var minimum = (long)Math.Ceiling(asking * (1 - (RoundConcession * Math.Clamp(context.Round - 1, 0, 3))));
        var (minYears, maxYears) = PreferredYears(context);
        SquadRole? required = context.ExpectedRole is { } role && role >= SquadRole.Starter && context.Reputation >= RoleDemandReputation ? role : null;
        long? clause = context.Age <= 30 && context.Reputation >= context.ClubReputation + ClauseDemandGap ? RoundUpThousands(ClauseMultiple * asking) : null;
        var toughness = Math.Clamp(context.AgentToughness, 1, 100);
        var threshold = Math.Clamp(CounterThreshold + (0.10 * (toughness - 50) / 50), 0.6, 0.9);
        var patience = BasePatience - (toughness >= 70 ? 1 : 0) + (context.Relationship >= 40 ? 1 : 0) - (context.Relationship <= -40 ? 1 : 0);
        return new(asking, market, minimum, minYears, maxYears, context.ExpectedRole, required, clause, (long)Math.Ceiling(threshold * minimum), Math.Max(1, patience));
    }

    /// <summary>
    /// Contract lengths the player accepts without a penalty: improving players aged 23 or less want 1–3 years (to cash
    /// in later), players aged 31–34 want security (2–5), the others take 1–5.
    /// </summary>
    public static (int Min, int Max) PreferredYears(NegotiationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Age switch
        {
            <= 23 when context.Potential > context.Overall + 5 => (1, 3),
            >= 31 and <= 34 => (2, MaxYears),
            _ => (1, MaxYears),
        };
    }

    /// <summary>
    /// The annual value of <paramref name="offer"/> to a player with <paramref name="demands"/>: <see cref="OfferValue(ContractTerms)"/>
    /// plus the promises above his expected role, plus a release clause, minus the length penalty (docs/CONTRACTS.md §4.2).
    /// </summary>
    public long OfferValue(ContractTerms offer, PlayerDemands demands)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(demands);
        double asking = demands.AskingSalary;
        var value = (double)OfferValue(offer);
        if (demands.ExpectedRole is { } expected && offer.Promises is { } promises)
        {
            var levels = promises.Role is { } role ? Math.Max(0, (int)role - (int)expected) : 0;
            var baseMinutes = PromiseRules.Minutes(promises.Role is { } r && r > expected ? r : expected);
            var minutes = Math.Max(0, (promises.MinutesPerGame ?? 0) - baseMinutes);
            value += asking * Math.Min(MaxPromiseValue, (RolePromiseValue * levels) + (MinutePromiseValue * minutes));
        }

        if (offer.ReleaseClause is { } clause)
        {
            value += clause <= 10 * asking ? 0.04 * asking : clause <= ClauseMultiple * asking ? 0.02 * asking : 0;
        }

        var years = Math.Round(offer.Years * 4) / 4;
        var outside = years < demands.MinYears ? demands.MinYears - years : years > demands.MaxYears ? years - demands.MaxYears : 0;
        value -= LengthPenalty * asking * outside;
        return (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    /// <summary>The player's answer to <paramref name="offer"/>.</summary>
    public NegotiationResponse Evaluate(NegotiationContext context, ContractTerms offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var demands = Demands(context);
        var value = OfferValue(offer, demands);
        if (offer.EndDate <= offer.StartDate || offer.Years > MaxYears + 0.01)
        {
            return new(NegotiationOutcome.Rejected, demands.AskingSalary, value, null, $"The player signs for at most {MaxYears} years.") { Demands = demands };
        }

        var unmet = Unmet(offer, demands);
        if (unmet.Count == 0 && value >= demands.MinimumValue)
        {
            return new(NegotiationOutcome.Accepted, demands.AskingSalary, value, null, "The player accepts the offer.") { Demands = demands };
        }

        if (value >= demands.RejectBelow)
        {
            var counter = Meet(offer, demands);
            var wants = unmet.Count == 0 ? string.Empty : $" He insists on {string.Join(" and ", unmet)}.";
            return new(
                NegotiationOutcome.CounterOffer,
                demands.AskingSalary,
                value,
                counter,
                $"The player wants more.{wants} He would sign for € {counter.AnnualSalary:N0} a year on the terms of his counter-offer.")
            { Demands = demands };
        }

        var insists = unmet.Count == 0 ? string.Empty : $" He insists on {string.Join(" and ", unmet)}.";
        return new(NegotiationOutcome.Rejected, demands.AskingSalary, value, null, $"The offer is far below what the player expects (about € {demands.AskingSalary:N0} a year).{insists}")
        { Demands = demands };
    }

    /// <summary>
    /// <paramref name="offer"/> changed as little as possible to satisfy <paramref name="demands"/>: the length moved into
    /// his preferred range, the role he insists on promised, a release clause he accepts, then the salary raised (to the
    /// next € 1,000) until the value reaches his minimum. The result is always accepted.
    /// </summary>
    public ContractTerms Meet(ContractTerms offer, PlayerDemands demands)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(demands);
        var terms = offer;
        var years = Math.Round(offer.Years * 4) / 4;
        if (years < demands.MinYears || years > demands.MaxYears)
        {
            var end = offer.StartDate.AddYears(years < demands.MinYears ? demands.MinYears : demands.MaxYears).AddDays(-1);
            terms = terms with
            {
                EndDate = end,
                ClubOptionUntil = ValidOption(offer.ClubOptionUntil, end),
                PlayerOptionUntil = ValidOption(offer.PlayerOptionUntil, end),
            };
        }

        if (demands.RequiredRole is { } role && !RoleMet(terms.Promises, role))
        {
            terms = terms with { Promises = (terms.Promises ?? new ContractPromises()) with { Role = role } };
        }

        if (demands.MaxReleaseClause is { } maxClause && (terms.ReleaseClause is not { } clause || clause > maxClause))
        {
            terms = terms with { ReleaseClause = maxClause };
        }

        var shortfall = demands.MinimumValue - OfferValue(terms, demands);
        if (shortfall > 0)
        {
            terms = terms with { AnnualSalary = terms.AnnualSalary + RoundUpThousands(shortfall / ScaleOf(terms)) };
        }

        return terms;
    }

    private static List<string> Unmet(ContractTerms offer, PlayerDemands demands)
    {
        var unmet = new List<string>();
        if (demands.RequiredRole is { } role && !RoleMet(offer.Promises, role))
        {
            unmet.Add($"being promised a {role.ToString().ToLowerInvariant()} role");
        }

        if (demands.MaxReleaseClause is { } max && (offer.ReleaseClause is not { } clause || clause > max))
        {
            unmet.Add($"a release clause of at most € {max:N0}");
        }

        return unmet;
    }

    private static bool RoleMet(ContractPromises? promises, SquadRole role) =>
        promises is not null && (promises.Role >= role || promises.TargetMinutes >= PromiseRules.Minutes(role));

    private static DateOnly? ValidOption(DateOnly? until, DateOnly end) =>
        until is { } date && date > end && date <= end.AddYears(Contract.MaxOptionYears) ? date : null;

    /// <summary>
    /// Whether the player takes up his option: he does when the contract pays at least what he would ask on the market.
    /// </summary>
    public bool ExercisesPlayerOption(NegotiationContext context, long currentSalary) =>
        currentSalary >= AskingSalary(context with { CurrentSalary = null });

    /// <summary>Least share of the remaining salary a player accepts to end his contract by mutual agreement.</summary>
    public double MinBuyoutShare { get; init; } = 0.3;

    /// <summary>Most share of the remaining salary a player asks to leave (one who would earn much less elsewhere).</summary>
    public double MaxBuyoutShare { get; init; } = 0.9;

    /// <summary>
    /// The share of the remaining salary the player asks to agree to a buyout of his contract (docs/CONTRACTS.md §3):
    /// <see cref="MinBuyoutShare"/> when he would earn at least his salary on the market (an average club,
    /// <paramref name="context"/> without current salary), rising with the pay cut he would face, at most <see cref="MaxBuyoutShare"/>.
    /// </summary>
    public double BuyoutShare(NegotiationContext context, long currentSalary)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (currentSalary <= 0)
        {
            return MinBuyoutShare;
        }

        var market = AskingSalary(context with { CurrentSalary = null });
        var cut = Math.Max(0, 1 - ((double)market / currentSalary));
        return Math.Clamp(MinBuyoutShare + cut, MinBuyoutShare, MaxBuyoutShare);
    }

    /// <summary>The least settlement (whole thousands) the player accepts for <paramref name="remainingSalary"/> still owed to him.</summary>
    public long MinimumBuyout(NegotiationContext context, long currentSalary, long remainingSalary) =>
        Math.Min(remainingSalary, RoundUpThousands(Math.Max(0, remainingSalary) * BuyoutShare(context, currentSalary)));

    // How much the annual value grows per euro of salary (options are a share of the salary).
    private double ScaleOf(ContractTerms offer) =>
        1 + (offer.PlayerOptionUntil is null ? 0 : OptionValue) - (offer.ClubOptionUntil is null ? 0 : OptionValue);

    private static int Clamp(int rating) => Math.Clamp(rating, 1, 100);

    private static long RoundThousands(double value) => (long)Math.Round(value / 1000, MidpointRounding.AwayFromZero) * 1000;

    private static long RoundUpThousands(double value) => (long)Math.Ceiling(value / 1000) * 1000;
}
