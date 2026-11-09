using System.Text.Json;
using System.Text.Json.Serialization;
using Ebm.Domain.Common;

namespace Ebm.Domain.Competitions.Rules;

/// <summary>
/// The configurable format of a league competition, stored as JSON in <see cref="Competition.RulesJson"/>.
/// Every property is optional; missing ones take the defaults below. Documented in docs/COMPETITION_FORMATS.md.
/// </summary>
public sealed record CompetitionRules
{
    public const int MaxStandingPoints = 10;

    public static readonly IReadOnlyList<TieBreaker> DefaultTieBreakers =
        [TieBreaker.HeadToHeadWins, TieBreaker.HeadToHeadPointDifference, TieBreaker.PointDifference, TieBreaker.PointsFor];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public int PointsForWin { get; init; } = 2;

    public int PointsForLoss { get; init; }

    public RegistrationRules Registration { get; init; } = new();

    public RegularSeasonRules RegularSeason { get; init; } = new();

    /// <summary>Applied in order to clubs level on standing points; the last resort is the club ID (stand-in for drawing lots).</summary>
    public IReadOnlyList<TieBreaker> TieBreakers { get; init; } = DefaultTieBreakers;

    /// <summary>Final stage after the regular season; <c>null</c> when the table decides everything.</summary>
    public PlayoffRules? Playoffs { get; init; }

    public PromotionRules Promotion { get; init; } = new();

    public RelegationRules Relegation { get; init; } = new();

    /// <summary>Parses and validates rules JSON. Errors are reported as <see cref="DomainValidationException"/>.</summary>
    public static CompetitionRules Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new DomainValidationException("Competition rules are required (use {} for the defaults).");
        }

        CompetitionRules? rules;
        try
        {
            rules = JsonSerializer.Deserialize<CompetitionRules>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new DomainValidationException($"Competition rules are not valid: {ex.Message}");
        }

        if (rules is null)
        {
            throw new DomainValidationException("Competition rules must be a JSON object.");
        }

        rules.Validate();
        return rules;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Checks the rules that depend on the number of clubs entered (before scheduling and at season end).</summary>
    public void ValidateForClubs(int clubs)
    {
        if (clubs < Registration.MinTeams || clubs > Registration.MaxTeams)
        {
            throw new DomainValidationException(
                $"The competition needs between {Registration.MinTeams} and {Registration.MaxTeams} clubs; {clubs} are entered.");
        }

        Require(Playoffs is null || Playoffs.LastPosition <= clubs,
            $"the playoffs need table positions {Playoffs?.FromPosition} to {Playoffs?.LastPosition}, but only {clubs} clubs are entered");
        Require(Promotion.Direct + Relegation.Direct <= clubs, "more promotion and relegation places than clubs");
        Require(!Promotion.PlayoffWinner || Playoffs!.LastPosition <= clubs - Relegation.Direct,
            "relegated positions cannot also play the promotion playoffs");
    }

    /// <summary>Checks the rules that do not depend on the number of registered clubs.</summary>
    private void Validate()
    {
        Guard.Range(PointsForWin, 0, MaxStandingPoints, "Rules: pointsForWin");
        Guard.Range(PointsForLoss, 0, MaxStandingPoints, "Rules: pointsForLoss");
        Require(PointsForWin > PointsForLoss, "pointsForWin must be greater than pointsForLoss");
        Require(Registration is not null && RegularSeason is not null && Promotion is not null && Relegation is not null && TieBreakers is not null,
            "registration, regularSeason, tieBreakers, promotion and relegation cannot be null");
        Guard.Range(Registration!.MinTeams, 2, RegistrationRules.TeamLimit, "Rules: registration.minTeams");
        Guard.Range(Registration.MaxTeams, Registration.MinTeams, RegistrationRules.TeamLimit, "Rules: registration.maxTeams");
        Guard.Range(RegularSeason!.Legs, 1, RegularSeasonRules.MaxLegs, "Rules: regularSeason.legs");
        Guard.Range(RegularSeason.DaysBetweenRounds, 1, 30, "Rules: regularSeason.daysBetweenRounds");
        foreach (var tieBreaker in TieBreakers!)
        {
            Guard.Defined(tieBreaker, "Rules: tie-breaker");
        }

        Require(TieBreakers.Distinct().Count() == TieBreakers.Count, "tieBreakers cannot repeat a criterion");
        Guard.Range(Promotion!.Direct, 0, RegistrationRules.TeamLimit, "Rules: promotion.direct");
        Guard.Range(Relegation!.Direct, 0, RegistrationRules.TeamLimit, "Rules: relegation.direct");

        if (Playoffs is { } playoffs)
        {
            Require(playoffs.Teams is 2 or 4 or 8 or 16 or 32, "playoffs.teams must be 2, 4, 8, 16 or 32");
            Guard.Range(playoffs.FromPosition, 1, RegistrationRules.TeamLimit, "Rules: playoffs.fromPosition");
            Guard.Range(playoffs.DaysBetweenGames, 1, 30, "Rules: playoffs.daysBetweenGames");
            Require(playoffs.BestOf is not null && playoffs.BestOf.Count == playoffs.Rounds,
                $"playoffs.bestOf must list one series length per round ({playoffs.Rounds} for {playoffs.Teams} teams)");
            Require(playoffs.BestOf!.All(b => b is >= 1 and <= PlayoffRules.MaxBestOf && b % 2 == 1),
                $"playoffs.bestOf values must be odd numbers from 1 to {PlayoffRules.MaxBestOf}");
            Require(!Promotion.PlayoffWinner || Promotion.Direct < playoffs.FromPosition,
                "with promotion.playoffWinner, directly promoted positions cannot also play the playoffs (raise playoffs.fromPosition)");
        }
        else
        {
            Require(!Promotion.PlayoffWinner, "promotion.playoffWinner needs a playoffs section");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainValidationException($"Competition rules: {message}.");
        }
    }
}

/// <summary>Who may enter the competition.</summary>
public sealed record RegistrationRules
{
    public const int TeamLimit = 40;

    public int MinTeams { get; init; } = 2;

    public int MaxTeams { get; init; } = 20;

    /// <summary>Clubs must come from the competition's country (ignored for international competitions).</summary>
    public bool SameCountry { get; init; } = true;
}

public sealed record RegularSeasonRules
{
    public const int MaxLegs = 4;

    /// <summary>How many times each pair of clubs meets; legs alternate home and away (2 = home and away).</summary>
    public int Legs { get; init; } = 2;

    public int DaysBetweenRounds { get; init; } = 7;
}

/// <summary>A single-elimination bracket of best-of series seeded from the regular-season table.</summary>
public sealed record PlayoffRules
{
    public const int MaxBestOf = 7;

    public int Teams { get; init; }

    /// <summary>Table position of the first seed (e.g. 2 when the leader is promoted directly).</summary>
    public int FromPosition { get; init; } = 1;

    /// <summary>Series length per round, first round first (e.g. [5, 5, 5] for 8 teams).</summary>
    public IReadOnlyList<int> BestOf { get; init; } = [];

    public int DaysBetweenGames { get; init; } = 2;

    [JsonIgnore]
    public int Rounds => int.Log2(Teams);

    [JsonIgnore]
    public int LastPosition => FromPosition + Teams - 1;
}

public sealed record PromotionRules
{
    /// <summary>Top table positions promoted directly.</summary>
    public int Direct { get; init; }

    /// <summary>The playoff champion is promoted too.</summary>
    public bool PlayoffWinner { get; init; }

    [JsonIgnore]
    public int Places => Direct + (PlayoffWinner ? 1 : 0);
}

public sealed record RelegationRules
{
    /// <summary>Bottom table positions relegated.</summary>
    public int Direct { get; init; }
}

/// <summary>Criteria separating clubs level on standing points. Head-to-head criteria count only games among the tied clubs.</summary>
public enum TieBreaker
{
    HeadToHeadWins,
    HeadToHeadPointDifference,
    HeadToHeadPointsFor,
    PointDifference,
    PointsFor,
    FewestPointsAgainst,
    Wins,
}
