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
