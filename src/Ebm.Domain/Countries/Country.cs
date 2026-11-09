using Ebm.Domain.Common;

namespace Ebm.Domain.Countries;

/// <summary>A country: nationality of people and home of clubs, arenas and domestic leagues.</summary>
public sealed class Country : Entity<CountryId>
{
    /// <summary>ISO 3166-1 alpha-3 codes have exactly three letters.</summary>
    public const int CodeLength = 3;
    public const int NameMaxLength = 100;

    public Country(CountryId id, string code, string name)
        : base(id)
    {
        Code = NormaliseCode(code);
        Rename(name);
    }

    private Country()
    {
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Validates an ISO 3166-1 alpha-3 code and returns it in upper case.</summary>
    /// <exception cref="DomainValidationException">The code is not three ASCII letters.</exception>
    public static string NormaliseCode(string? code)
    {
        var normalised = Guard.Text(code, "Country code", CodeLength).ToUpperInvariant();
        if (normalised.Length != CodeLength || !normalised.All(char.IsAsciiLetterUpper))
        {
            throw new DomainValidationException($"Country code must be {CodeLength} letters (ISO 3166-1 alpha-3).");
        }

        return normalised;
    }

    /// <summary>Changes the display name. The code identifies the country in data files and never changes.</summary>
    public void Rename(string name) => Name = Guard.Text(name, "Country name", NameMaxLength);
}
