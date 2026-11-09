using Ebm.Domain.Common;
using Ebm.Domain.Countries;

namespace Ebm.Domain.Competitions;

/// <summary>
/// An organising league body (e.g. a national basketball league) that owns one or more divisions.
/// International leagues have no country.
/// </summary>
public sealed class League : Entity<LeagueId>
{
    public const int NameMaxLength = 100;
    public const int ShortNameMaxLength = 20;

    public League(LeagueId id, string name, string shortName, CountryId? countryId)
        : base(id)
    {
        UpdateDetails(name, shortName, countryId);
    }

    private League()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string ShortName { get; private set; } = string.Empty;

    public CountryId? CountryId { get; private set; }

    public void UpdateDetails(string name, string shortName, CountryId? countryId)
    {
        Name = Guard.Text(name, "League name", NameMaxLength);
        ShortName = Guard.Text(shortName, "League short name", ShortNameMaxLength);
        CountryId = Guard.Optional(countryId, "League country");
    }
}
