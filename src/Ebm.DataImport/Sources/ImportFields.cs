using Ebm.Domain.Players;
using Ebm.Services.Import;

namespace Ebm.DataImport.Sources;

/// <summary>
/// The import format: record type names (CSV file names and JSON section names) and the fields of each type.
/// Field names are matched case-insensitively. Documented in docs/IMPORT_FORMATS.md.
/// </summary>
internal static class ImportFields
{
    public sealed record Schema(ImportRecordKind Kind, string Section, IReadOnlyList<string> Required, IReadOnlyList<string> Optional)
    {
        public bool IsKnown(string field) =>
            Required.Contains(field, StringComparer.OrdinalIgnoreCase) || Optional.Contains(field, StringComparer.OrdinalIgnoreCase);
    }

    public static readonly IReadOnlyList<Schema> All =
    [
        new(ImportRecordKind.Country, "countries", ["code", "name"], ["id"]),
        new(ImportRecordKind.Arena, "arenas", ["name", "city", "country", "capacity"], ["id"]),
        new(ImportRecordKind.Club, "clubs", ["name", "shortName", "country"], ["id", "homeArenaId", "foundedYear"]),
        new(ImportRecordKind.League, "leagues", ["name", "shortName"], ["id", "country"]),
        new(ImportRecordKind.Division, "divisions", ["leagueId", "name", "level"], ["id"]),
        new(ImportRecordKind.Competition, "competitions", ["name", "shortName", "type"], ["id", "country", "divisionId", "rules"]),
        new(ImportRecordKind.Season, "seasons", ["name", "startDate", "endDate"], ["id"]),
        new(ImportRecordKind.Participant, "participants", ["competitionId", "seasonId", "clubId"], ["id"]),
        new(
            ImportRecordKind.Player,
            "players",
            ["firstName", "lastName", "dateOfBirth", "nationality", "heightCm", "weightKg", "position", .. PlayerCatalog.Attributes.Select(PlayerCatalog.FieldName)],
            ["id", "wingspanCm", "secondaryPositions", "experience", "reputation", "potential", "fitness", "morale", "injuryStatus", "injuryReturnDate",
             .. PlayerCatalog.Tendencies.Select(PlayerCatalog.FieldName)]),
        new(ImportRecordKind.Contract, "contracts", ["clubId", "playerId", "startDate", "endDate", "annualSalary"], ["id"]),
    ];

    public static Schema For(ImportRecordKind kind) => All.Single(s => s.Kind == kind);

    public static Schema? ForSection(string section) =>
        All.FirstOrDefault(s => string.Equals(s.Section, section, StringComparison.OrdinalIgnoreCase));

    /// <summary>Record type of a CSV file: <c>players.csv</c>, <c>players-2026.csv</c> or <c>players_update.csv</c>.</summary>
    public static Schema? ForCsvFile(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        return All.FirstOrDefault(s =>
            stem.Equals(s.Section, StringComparison.OrdinalIgnoreCase)
            || (stem.Length > s.Section.Length
                && stem.StartsWith(s.Section, StringComparison.OrdinalIgnoreCase)
                && stem[s.Section.Length] is '-' or '_' or '.'));
    }
}
