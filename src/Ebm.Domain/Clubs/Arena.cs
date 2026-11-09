using Ebm.Domain.Common;
using Ebm.Domain.Countries;

namespace Ebm.Domain.Clubs;

/// <summary>A venue where clubs play home matches. Several clubs may share one arena.</summary>
public sealed class Arena : Entity<ArenaId>
{
    public const int NameMaxLength = 100;
    public const int CityMaxLength = 100;
    public const int MaxCapacity = 100_000;

    public Arena(ArenaId id, string name, string city, CountryId countryId, int capacity)
        : base(id)
    {
        UpdateDetails(name, city, countryId, capacity);
    }

    private Arena()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public CountryId CountryId { get; private set; }

    public int Capacity { get; private set; }

    public void UpdateDetails(string name, string city, CountryId countryId, int capacity)
    {
        Name = Guard.Text(name, "Arena name", NameMaxLength);
        City = Guard.Text(city, "Arena city", CityMaxLength);
        CountryId = Guard.Required(countryId, "Arena country");
        Capacity = Guard.Range(capacity, 1, MaxCapacity, "Arena capacity");
    }
}
