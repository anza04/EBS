using Ebm.Domain.Clubs;
using Ebm.Domain.Common;

namespace Ebm.Domain.Competitions.Engine;

/// <summary>Registration rules checked whenever a club is entered into a competition season (import or game services).</summary>
public static class CompetitionRegistration
{
    /// <param name="registeredClubs">Clubs already entered in this competition season.</param>
    /// <param name="inAnotherLeague">The club already plays another league competition this season.</param>
    /// <param name="clubCountryCode">ISO alpha-3 code of the club's country, checked against <c>registration.countries</c>.</param>
    public static void Check(Competition competition, Club club, int registeredClubs, bool inAnotherLeague, string? clubCountryCode = null)
    {
        ArgumentNullException.ThrowIfNull(competition);
        ArgumentNullException.ThrowIfNull(club);
        var rules = competition.ParseRules().Registration;
        if (rules.SameCountry && competition.CountryId is { } country && club.CountryId != country)
        {
            throw new DomainValidationException($"{club.Name} cannot enter {competition.Name}: only clubs from the competition's country may enter.");
        }

        if (rules.Countries.Count > 0 && !rules.Countries.Contains(clubCountryCode ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            throw new DomainValidationException($"{club.Name} cannot enter {competition.Name}: only clubs from {string.Join(", ", rules.Countries)} may enter.");
        }

        if (registeredClubs >= rules.MaxTeams)
        {
            throw new DomainValidationException($"{competition.Name} is full ({rules.MaxTeams} clubs).");
        }

        if (competition.Type == CompetitionType.League && inAnotherLeague)
        {
            throw new DomainValidationException($"{club.Name} already plays another league competition this season.");
        }
    }
}
