using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ebm.Domain.Common;
using Ebm.Domain.Rosters;

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

    /// <summary>Optional second round robin among groups formed from the first-phase table; <c>null</c> = one phase.</summary>
    public SecondPhaseRules? SecondPhase { get; init; }

    /// <summary>Applied in order to clubs level on standing points; the last resort is the club ID (stand-in for drawing lots).</summary>
    public IReadOnlyList<TieBreaker> TieBreakers { get; init; } = DefaultTieBreakers;

    /// <summary>Final stage after the regular season (and second phase); <c>null</c> when the table decides everything.</summary>
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

        Require(Playoffs is null || Playoffs.LastQualifyingPosition <= clubs,
            $"the playoffs need table positions {Playoffs?.FromPosition} to {Playoffs?.LastQualifyingPosition}, but only {clubs} clubs are entered");
        Require(SecondPhase is null || SecondPhase.ByCountry || SecondPhase.Groups[^1].To <= clubs,
            $"the second-phase groups need table positions up to {SecondPhase?.Groups[^1].To}, but only {clubs} clubs are entered");
        Require(Promotion.Direct + Relegation.Direct <= clubs, "more promotion and relegation places than clubs");
        Require(Promotion.PlayoffPromotions == 0 || Playoffs!.LastQualifyingPosition <= clubs - Relegation.Direct,
            "relegated positions cannot also play the promotion playoffs");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainValidationException($"Competition rules: {message}.");
        }
    }

    /// <summary>Checks the rules that do not depend on the number of registered clubs.</summary>
    private void Validate()
    {
        Guard.Range(PointsForWin, 0, MaxStandingPoints, "Rules: pointsForWin");
        Guard.Range(PointsForLoss, 0, MaxStandingPoints, "Rules: pointsForLoss");
        Require(PointsForWin > PointsForLoss, "pointsForWin must be greater than pointsForLoss");
        Require(Registration is not null && RegularSeason is not null && Promotion is not null && Relegation is not null && TieBreakers is not null,
            "registration, regularSeason, tieBreakers, promotion and relegation cannot be null");
        Require(Registration!.Squad is not null, "registration.squad cannot be null");
        Registration.Squad!.Validate("Rules: registration.squad");
        Guard.Range(Registration.MinTeams, 2, RegistrationRules.TeamLimit, "Rules: registration.minTeams");
        Guard.Range(Registration.MaxTeams, Registration.MinTeams, RegistrationRules.TeamLimit, "Rules: registration.maxTeams");
        Require(Registration.Countries is not null && Registration.Countries.All(c => c is { Length: 3 } && c.All(char.IsAsciiLetter)),
            "registration.countries must list ISO 3166-1 alpha-3 codes");
        Guard.Range(RegularSeason!.Legs, 1, RegularSeasonRules.MaxLegs, "Rules: regularSeason.legs");
        Guard.Range(RegularSeason.DaysBetweenRounds, 1, 30, "Rules: regularSeason.daysBetweenRounds");
        Guard.Range(RegularSeason.StartOffsetDays, 0, RegularSeasonRules.MaxStartOffsetDays, "Rules: regularSeason.startOffsetDays");
        SecondPhase?.Validate();
        foreach (var tieBreaker in TieBreakers!)
        {
            Guard.Defined(tieBreaker, "Rules: tie-breaker");
        }

        Require(TieBreakers.Distinct().Count() == TieBreakers.Count, "tieBreakers cannot repeat a criterion");
        Guard.Range(Promotion!.Direct, 0, RegistrationRules.TeamLimit, "Rules: promotion.direct");
        Guard.Range(Relegation!.Direct, 0, RegistrationRules.TeamLimit, "Rules: relegation.direct");
        Require(!(Promotion.PlayoffWinner && Promotion.PlayoffPlaces > 0), "use either promotion.playoffWinner or promotion.playoffPlaces");
        Require(Promotion.PlayoffPlaces is 0 or 1 or 2 or 4, "promotion.playoffPlaces must be 0, 1 (champion), 2 (finalists) or 4 (semi-finalists)");

        if (Playoffs is { } playoffs)
        {
            Guard.Range(playoffs.Teams, 2, PlayoffRules.MaxTeams, "Rules: playoffs.teams");
            Guard.Range(playoffs.FromPosition, 1, RegistrationRules.TeamLimit, "Rules: playoffs.fromPosition");
            Guard.Range(playoffs.DaysBetweenGames, 1, 30, "Rules: playoffs.daysBetweenGames");
            Require(playoffs.BestOf is not null && playoffs.BestOf.Count == playoffs.Rounds,
                $"playoffs.bestOf must list one series length per round ({playoffs.Rounds} for {playoffs.Teams} teams; the top seeds get byes when the bracket is not full)");
            Require(playoffs.BestOf!.All(b => b is >= 1 and <= PlayoffRules.MaxBestOf && b % 2 == 1),
                $"playoffs.bestOf values must be odd numbers from 1 to {PlayoffRules.MaxBestOf}");
            Require(Promotion.PlayoffPromotions == 0 || Promotion.Direct < playoffs.FromPosition,
                "with playoff promotion, directly promoted positions cannot also play the playoffs (raise playoffs.fromPosition)");
            Require(Promotion.PlayoffPromotions <= Math.Max(1, playoffs.BracketSize / 2),
                "promotion.playoffPlaces cannot exceed half the playoff bracket");
        }
        else
        {
            Require(Promotion.PlayoffPromotions == 0, "promotion.playoffWinner and promotion.playoffPlaces need a playoffs section");
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

    /// <summary>
    /// ISO alpha-3 codes of the countries whose clubs may enter (cross-border leagues); empty = any country. Checked
    /// in addition to <see cref="SameCountry"/>, which a competition without a country ignores.
    /// </summary>
    public IReadOnlyList<string> Countries { get; init; } = [];

    /// <summary>Player registration limits of each club's squad (docs/ROSTER_RULES.md).</summary>
    public SquadRules Squad { get; init; } = SquadRules.Default;
}

public sealed record RegularSeasonRules
{
    public const int MaxLegs = 4;
    public const int MaxStartOffsetDays = 120;

    /// <summary>How many times each pair of clubs meets; legs alternate home and away (2 = home and away).</summary>
    public int Legs { get; init; } = 2;

    public int DaysBetweenRounds { get; init; } = 7;

    /// <summary>Days after the season start of the first round when no date is given (the league's calendar).</summary>
    public int StartOffsetDays { get; init; }
}

/// <summary>How first-phase results count in the second-phase tables.</summary>
public enum CarryOver
{
    /// <summary>Every first-phase game counts (the season goes on in groups).</summary>
    All,

    /// <summary>Only first-phase games between clubs of the same group count.</summary>
    WithinGroup,

    /// <summary>The groups start from zero.</summary>
    None,
}

/// <summary>
/// A second round robin after the regular season, among groups taken from the first-phase table (e.g. a
/// championship round of positions 1-6 and a relegation round of 7-12). The final table lists the groups in
/// order, then clubs in no group by their first-phase position; playoffs, promotion and relegation use it.
/// </summary>
public sealed record SecondPhaseRules
{
    public IReadOnlyList<PhaseGroup> Groups { get; init; } = [];

    /// <summary>Group positions count within each club's country (cross-border leagues: e.g. the top five of each country).</summary>
    public bool ByCountry { get; init; }

    public int Legs { get; init; } = 2;

    public int DaysBetweenRounds { get; init; } = 7;

    public CarryOver CarryOver { get; init; } = CarryOver.All;

    internal void Validate()
    {
        CompetitionRules.Require(Groups is { Count: > 0 }, "secondPhase.groups needs at least one group");
        Guard.Range(Legs, 1, RegularSeasonRules.MaxLegs, "Rules: secondPhase.legs");
        Guard.Range(DaysBetweenRounds, 1, 30, "Rules: secondPhase.daysBetweenRounds");
        Guard.Defined(CarryOver, "Rules: secondPhase.carryOver");
        var previous = 0;
        foreach (var group in Groups)
        {
            CompetitionRules.Require(group is not null && !string.IsNullOrWhiteSpace(group.Name), "every second-phase group needs a name");
            // Per country a single position still makes a group of one club per country.
            CompetitionRules.Require(group!.From > previous && (ByCountry ? group.To >= group.From : group.To > group.From) && group.To <= RegistrationRules.TeamLimit,
                "second-phase group positions must be ascending, non-overlapping ranges of at least two clubs");
            previous = group.To;
        }
    }
}

/// <summary>A second-phase group: first-phase table positions <see cref="From"/> to <see cref="To"/>.</summary>
public sealed record PhaseGroup
{
    public string Name { get; init; } = string.Empty;

    public int From { get; init; }

    public int To { get; init; }
}

/// <summary>
/// A single-elimination bracket of best-of series seeded from the final table. When <see cref="Teams"/> is not a
/// power of two the top seeds get first-round byes.
/// </summary>
public sealed record PlayoffRules
{
    public const int MaxBestOf = 7;
    public const int MaxTeams = 32;

    public int Teams { get; init; }

    /// <summary>Table position of the first seed (e.g. 2 when the leader is promoted directly).</summary>
    public int FromPosition { get; init; } = 1;

    /// <summary>Series length per round, first round first (e.g. [5, 5, 5] for 8 teams).</summary>
    public IReadOnlyList<int> BestOf { get; init; } = [];

    public int DaysBetweenGames { get; init; } = 2;

    /// <summary>
    /// The last two seeds come from a play-in among the next four table positions (7-10 for 8 teams):
    /// A = 7 v 8, B = 9 v 10; the winner of A is seed 7, the winner of loser A v winner B is seed 8.
    /// Single games, the better table position at home.
    /// </summary>
    public bool PlayIn { get; init; }

    /// <summary>Clubs in the first round including byes: the next power of two.</summary>
    [JsonIgnore]
    public int BracketSize => (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(Teams, 2));

    [JsonIgnore]
    public int Rounds => int.Log2(BracketSize);

    /// <summary>Last seeded table position; with the play-in its last two seeds come from the play-in.</summary>
    [JsonIgnore]
    public int LastPosition => FromPosition + Teams - 1;

    /// <summary>Last table position with a chance to reach the playoffs (including the play-in).</summary>
    [JsonIgnore]
    public int LastQualifyingPosition => LastPosition + (PlayIn ? 2 : 0);
}

public sealed record PromotionRules
{
    /// <summary>Top table positions promoted directly.</summary>
    public int Direct { get; init; }

    /// <summary>The playoff champion is promoted too (same as <see cref="PlayoffPlaces"/> = 1).</summary>
    public bool PlayoffWinner { get; init; }

    /// <summary>Clubs promoted from the playoffs: 1 = the champion, 2 = both finalists, 4 = the semi-finalists.</summary>
    public int PlayoffPlaces { get; init; }

    [JsonIgnore]
    public int PlayoffPromotions => PlayoffWinner ? 1 : PlayoffPlaces;

    [JsonIgnore]
    public int Places => Direct + PlayoffPromotions;
}

public sealed record RelegationRules
{
    /// <summary>Bottom positions of the final table relegated.</summary>
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
