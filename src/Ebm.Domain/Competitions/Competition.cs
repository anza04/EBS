using System.Text.Json;
using Ebm.Domain.Common;
using Ebm.Domain.Countries;

namespace Ebm.Domain.Competitions;

/// <summary>
/// A competition clubs take part in (a division's championship, a cup, an international tournament).
/// Its rules are configurable data stored as a JSON object, never code.
/// </summary>
public sealed class Competition : Entity<CompetitionId>
{
    public const int NameMaxLength = 100;
    public const int ShortNameMaxLength = 20;
    public const string EmptyRules = "{}";

    public Competition(
        CompetitionId id,
        string name,
        string shortName,
        CompetitionType type,
        CountryId? countryId,
        DivisionId? divisionId,
        string rulesJson = EmptyRules)
        : base(id)
    {
        UpdateDetails(name, shortName, type, countryId, divisionId);
        UpdateRules(rulesJson);
    }

    private Competition()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string ShortName { get; private set; } = string.Empty;

    public CompetitionType Type { get; private set; }

    /// <summary>Country of a domestic competition; <c>null</c> for international ones.</summary>
    public CountryId? CountryId { get; private set; }

    /// <summary>Division whose championship this is; <c>null</c> for cups and tournaments.</summary>
    public DivisionId? DivisionId { get; private set; }

    /// <summary>
    /// Competition rules as a JSON object. The rule schema is defined in Step 04; this step only
    /// guarantees the value is a well-formed JSON object.
    /// </summary>
    public string RulesJson { get; private set; } = EmptyRules;

    public void UpdateDetails(string name, string shortName, CompetitionType type, CountryId? countryId, DivisionId? divisionId)
    {
        Name = Guard.Text(name, "Competition name", NameMaxLength);
        ShortName = Guard.Text(shortName, "Competition short name", ShortNameMaxLength);
        Type = Guard.Defined(type, "Competition type");
        CountryId = Guard.Optional(countryId, "Competition country");
        DivisionId = Guard.Optional(divisionId, "Competition division");
    }

    public void UpdateRules(string rulesJson)
    {
        if (string.IsNullOrWhiteSpace(rulesJson))
        {
            throw new DomainValidationException("Competition rules are required (use {} for none).");
        }

        try
        {
            using var document = JsonDocument.Parse(rulesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new DomainValidationException("Competition rules must be a JSON object.");
            }
        }
        catch (JsonException ex)
        {
            throw new DomainValidationException($"Competition rules are not valid JSON: {ex.Message}");
        }

        RulesJson = rulesJson;
    }
}
