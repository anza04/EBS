using System.Globalization;
using Ebm.DataImport.Sources;

namespace Ebm.DataImport.Validation;

/// <summary>
/// Reads typed values from a <see cref="RawRecord"/>. Problems are collected in <see cref="Errors"/> instead of
/// thrown, so one pass reports every invalid field of a record; invalid values come back as defaults.
/// </summary>
internal sealed class FieldReader
{
    public const string DateFormat = "yyyy-MM-dd";

    private readonly RawRecord _record;

    public FieldReader(RawRecord record)
    {
        _record = record;
        if (record.Error is not null)
        {
            Errors.Add(record.Error);
        }
    }

    public List<string> Errors { get; } = [];

    public bool HasErrors => Errors.Count > 0;

    /// <summary>The trimmed value, or <c>null</c> when the field is absent or blank.</summary>
    public string? Optional(string name) =>
        _record.Fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    public string Text(string name)
    {
        var value = Optional(name);
        if (value is null)
        {
            Errors.Add($"'{name}' is required.");
        }

        return value ?? string.Empty;
    }

    public int Int(string name) => OptionalInt(name, required: true) ?? 0;

    public int? OptionalInt(string name, bool required = false) =>
        Parse<int>(name, required, "a whole number", v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : (int?)null);

    public long Long(string name) =>
        Parse(name, true, "a whole number", v => long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : (long?)null) ?? 0;

    public DateOnly Date(string name) =>
        Parse(name, true, $"a date in the form YYYY-MM-DD", v =>
            DateOnly.TryParseExact(v, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var r) ? r : (DateOnly?)null) ?? default;

    public Guid? OptionalGuid(string name, bool required = false) =>
        Parse(name, required, "an ID (GUID, e.g. 0198c2d4-5e6f-7a8b-9c0d-1e2f3a4b5c6d)", v =>
            Guid.TryParse(v, out var r) && r != Guid.Empty ? r : (Guid?)null);

    /// <summary>Reads an enum value by name (case-insensitive) or by one of <paramref name="aliases"/>.</summary>
    public TEnum Enum<TEnum>(string name, IReadOnlyDictionary<string, TEnum>? aliases = null)
        where TEnum : struct, Enum
    {
        var value = Optional(name);
        if (value is null)
        {
            Errors.Add($"'{name}' is required.");
            return default;
        }

        if (aliases is not null && aliases.TryGetValue(value, out var alias))
        {
            return alias;
        }

        if (!value.All(char.IsAsciiDigit) && System.Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && System.Enum.IsDefined(parsed))
        {
            return parsed;
        }

        var allowed = System.Enum.GetNames<TEnum>().Concat(aliases?.Keys ?? []);
        Errors.Add($"'{name}' must be one of {string.Join(", ", allowed)} (got '{value}').");
        return default;
    }

    private T? Parse<T>(string name, bool required, string expected, Func<string, T?> parse)
        where T : struct
    {
        var value = Optional(name);
        if (value is null)
        {
            if (required)
            {
                Errors.Add($"'{name}' is required.");
            }

            return null;
        }

        var result = parse(value);
        if (result is null)
        {
            Errors.Add($"'{name}' must be {expected} (got '{value}').");
        }

        return result;
    }
}
