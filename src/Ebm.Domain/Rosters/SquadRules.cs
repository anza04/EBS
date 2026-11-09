using Ebm.Domain.Common;

namespace Ebm.Domain.Rosters;

/// <summary>
/// Squad size and registration limits. <see cref="Default"/> applies to every club; a competition can set its own
/// in its rules JSON (<c>registration.squad</c>, docs/COMPETITION_FORMATS.md). Missing settings take the defaults.
/// </summary>
public sealed record SquadRules
{
    /// <summary>Players in a starting five.</summary>
    public const int Starters = 5;

    /// <summary>Upper bound of every squad count, to catch data-entry errors.</summary>
    public const int PlayerLimit = 30;

    public const int MinAgeLimit = 14;

    public static SquadRules Default { get; } = new();

    /// <summary>Fewest first-team (registered) players.</summary>
    public int MinPlayers { get; init; } = 10;

    /// <summary>Most first-team (registered) players.</summary>
    public int MaxPlayers { get; init; } = 18;

    /// <summary>Most players in the matchday squad (starters, rotation and bench).</summary>
    public int MatchdayPlayers { get; init; } = 12;

    /// <summary>Available starters plus rotation players below this number give a warning (short rotation).</summary>
    public int RecommendedRotation { get; init; } = 8;

    /// <summary>Most first-team players whose nationality differs from the club's country; <c>null</c> = no limit.</summary>
    public int? MaxForeignPlayers { get; init; }

    /// <summary>Fewest first-team players of the club's nationality.</summary>
    public int MinDomesticPlayers { get; init; }

    /// <summary>Youngest age allowed in the first team; <c>null</c> = no limit.</summary>
    public int? MinAge { get; init; }

    /// <summary>
    /// The rules a club must satisfy for every competition it plays in: the tightest limit of <paramref name="rules"/>
    /// for each setting (used by the AI recruiter, docs/TRANSFERS.md §6). <see cref="Default"/> when none are given.
    /// </summary>
    public static SquadRules Strictest(IEnumerable<SquadRules> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var all = rules.ToList();
        if (all.Count == 0)
        {
            return Default;
        }

        var foreign = all.Where(r => r.MaxForeignPlayers is not null).Select(r => r.MaxForeignPlayers!.Value).ToList();
        var ages = all.Where(r => r.MinAge is not null).Select(r => r.MinAge!.Value).ToList();
        var strictest = new SquadRules
        {
            MinPlayers = all.Max(r => r.MinPlayers),
            MaxPlayers = all.Min(r => r.MaxPlayers),
            MatchdayPlayers = all.Min(r => r.MatchdayPlayers),
            RecommendedRotation = all.Min(r => r.RecommendedRotation),
            MaxForeignPlayers = foreign.Count == 0 ? null : foreign.Min(),
            MinDomesticPlayers = all.Max(r => r.MinDomesticPlayers),
            MinAge = ages.Count == 0 ? null : ages.Max(),
        };

        // Contradictory competition rules fall back to the club defaults rather than failing the AI.
        return strictest.MinPlayers <= strictest.MaxPlayers && strictest.MatchdayPlayers <= strictest.MaxPlayers && strictest.RecommendedRotation <= strictest.MatchdayPlayers
            ? strictest
            : Default;
    }

    /// <summary>Checks the ranges; <paramref name="prefix"/> names the setting in messages.</summary>
    public SquadRules Validate(string prefix = "Squad rules")
    {
        Guard.Range(MinPlayers, Starters, PlayerLimit, $"{prefix}: minPlayers");
        Guard.Range(MaxPlayers, MinPlayers, PlayerLimit, $"{prefix}: maxPlayers");
        Guard.Range(MatchdayPlayers, Starters, MaxPlayers, $"{prefix}: matchdayPlayers");
        Guard.Range(RecommendedRotation, Starters, MatchdayPlayers, $"{prefix}: recommendedRotation");
        Guard.Range(MinDomesticPlayers, 0, MaxPlayers, $"{prefix}: minDomesticPlayers");
        if (MaxForeignPlayers is { } foreign)
        {
            Guard.Range(foreign, 0, MaxPlayers, $"{prefix}: maxForeignPlayers");
        }

        if (MinAge is { } age)
        {
            Guard.Range(age, MinAgeLimit, 40, $"{prefix}: minAge");
        }

        return this;
    }
}
