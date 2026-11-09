using Ebm.Domain.Common;
using Ebm.Domain.Countries;

namespace Ebm.Domain.Clubs;

/// <summary>A basketball club. The divisions and competitions it plays in are recorded per season as participants.</summary>
public sealed class Club : Entity<ClubId>
{
    public const int NameMaxLength = 100;
    public const int ShortNameMaxLength = 20;
    public const int MinFoundedYear = 1850;
    public const int MaxFoundedYear = 2100;

    public Club(ClubId id, string name, string shortName, CountryId countryId, ArenaId? homeArenaId = null, int? foundedYear = null)
        : base(id)
    {
        UpdateDetails(name, shortName, countryId, homeArenaId, foundedYear);
    }

    private Club()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string ShortName { get; private set; } = string.Empty;

    public CountryId CountryId { get; private set; }

    public ArenaId? HomeArenaId { get; private set; }

    public int? FoundedYear { get; private set; }

    /// <summary>Changes the club's details. The ID, history and contracts are unaffected.</summary>
    public void UpdateDetails(string name, string shortName, CountryId countryId, ArenaId? homeArenaId, int? foundedYear)
    {
        Name = Guard.Text(name, "Club name", NameMaxLength);
        ShortName = Guard.Text(shortName, "Club short name", ShortNameMaxLength);
        CountryId = Guard.Required(countryId, "Club country");
        ChangeHomeArena(homeArenaId);
        FoundedYear = foundedYear is { } year ? Guard.Range(year, MinFoundedYear, MaxFoundedYear, "Founded year") : null;
    }

    public void ChangeHomeArena(ArenaId? arenaId) => HomeArenaId = Guard.Optional(arenaId, "Home arena");
}
