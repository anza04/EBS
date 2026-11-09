using System.Text.Json;
using Ebm.Services.Import;

namespace Ebm.DataImport.Sources;

/// <summary>One record read from a file, with its field values as text, before validation.</summary>
/// <param name="Error">Set when the record itself is malformed (wrong column count, not a JSON object).</param>
internal sealed record RawRecord(
    ImportRecordKind Kind,
    string Source,
    int Position,
    IReadOnlyDictionary<string, string?> Fields,
    string? Error = null);

/// <summary>Reads CSV and JSON import files into <see cref="RawRecord"/>s and file-level problems.</summary>
internal static class SourceReader
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly string[] JsonHeaderProperties = ["format", "version"];

    public static async Task ReadAsync(string path, List<RawRecord> records, List<string> problems, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(path);
        string text;
        try
        {
            text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problems.Add($"{name}: the file cannot be read ({ex.Message}).");
            return;
        }

        switch (Path.GetExtension(path).ToUpperInvariant())
        {
            case ".CSV":
                ReadCsv(name, text, records, problems);
                break;
            case ".JSON":
                ReadJson(name, text, records, problems);
                break;
            default:
                problems.Add($"{name}: unsupported file type. Use .csv or .json files.");
                break;
        }
    }

    private static void ReadCsv(string name, string text, List<RawRecord> records, List<string> problems)
    {
        var schema = ImportFields.ForCsvFile(name);
        if (schema is null)
        {
            problems.Add($"{name}: cannot tell the record type. Name CSV files after it: " +
                string.Join(", ", ImportFields.All.Select(s => s.Section + ".csv")) + ".");
            return;
        }

        IReadOnlyList<CsvParser.Row> rows;
        try
        {
            rows = CsvParser.Parse(text);
        }
        catch (FormatException ex)
        {
            problems.Add($"{name}: malformed CSV. {ex.Message} The file was skipped.");
            return;
        }

        if (rows.Count == 0)
        {
            problems.Add($"{name}: the file is empty.");
            return;
        }

        var header = rows[0].Fields.Select(h => h.Trim()).ToList();
        var missing = schema.Required.Where(r => !header.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0)
        {
            problems.Add($"{name}: missing required column(s) {string.Join(", ", missing)}. The file was skipped.");
            return;
        }

        ReportUnknownFields(name, schema, header, problems);
        foreach (var row in rows.Skip(1))
        {
            var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Count && i < row.Fields.Count; i++)
            {
                fields[header[i]] = row.Fields[i];
            }

            var error = row.Fields.Count == header.Count
                ? null
                : $"The row has {row.Fields.Count} values but the header has {header.Count} columns.";
            records.Add(new RawRecord(schema.Kind, name, row.Line, fields, error));
        }
    }

    private static void ReadJson(string name, string text, List<RawRecord> records, List<string> problems)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, JsonOptions);
        }
        catch (JsonException ex)
        {
            problems.Add($"{name}: malformed JSON ({ex.Message}). The file was skipped.");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{name}: the file must contain a JSON object with record sections. The file was skipped.");
                return;
            }

            if (root.TryGetProperty("version", out var version) && !(version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number == 1))
            {
                problems.Add($"{name}: unsupported dataset version {version.GetRawText()} (expected 1). The file was skipped.");
                return;
            }

            foreach (var property in root.EnumerateObject())
            {
                var schema = ImportFields.ForSection(property.Name);
                if (schema is null)
                {
                    if (!JsonHeaderProperties.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        problems.Add($"{name}: unknown section '{property.Name}' ignored.");
                    }

                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    problems.Add($"{name}: section '{property.Name}' must be an array. It was skipped.");
                    continue;
                }

                ReadJsonSection(name, schema, property.Value, records, problems);
            }
        }
    }

    private static void ReadJsonSection(string name, ImportFields.Schema schema, JsonElement items, List<RawRecord> records, List<string> problems)
    {
        var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        foreach (var item in items.EnumerateArray())
        {
            position++;
            var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (item.ValueKind != JsonValueKind.Object)
            {
                records.Add(new RawRecord(schema.Kind, $"{name}#{schema.Section}", position, fields, "The record must be a JSON object."));
                continue;
            }

            foreach (var field in item.EnumerateObject())
            {
                seenFields.Add(field.Name);
                fields[field.Name] = field.Value.ValueKind switch
                {
                    JsonValueKind.String => field.Value.GetString(),
                    JsonValueKind.Null => null,
                    JsonValueKind.Object or JsonValueKind.Array => field.Value.GetRawText(),
                    _ => field.Value.GetRawText(),
                };
            }

            records.Add(new RawRecord(schema.Kind, $"{name}#{schema.Section}", position, fields));
        }

        ReportUnknownFields($"{name}#{schema.Section}", schema, seenFields, problems);
    }

    private static void ReportUnknownFields(string source, ImportFields.Schema schema, IEnumerable<string> fields, List<string> problems)
    {
        var unknown = fields.Where(f => f.Length > 0 && !schema.IsKnown(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0)
        {
            problems.Add($"{source}: unknown field(s) {string.Join(", ", unknown)} ignored.");
        }
    }
}
