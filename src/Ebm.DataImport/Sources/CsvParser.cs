using System.Text;

namespace Ebm.DataImport.Sources;

/// <summary>
/// RFC 4180 CSV parser: comma separated, fields optionally in double quotes, <c>""</c> for a quote inside a
/// quoted field, line breaks allowed inside quotes. Blank lines are skipped.
/// </summary>
internal static class CsvParser
{
    /// <summary>A parsed row and the 1-based line number it starts on.</summary>
    public sealed record Row(int Line, IReadOnlyList<string> Fields);

    /// <exception cref="FormatException">A quoted field is not closed or a quote appears inside an unquoted field.</exception>
    public static IReadOnlyList<Row> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<Row>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var rowStart = 1;
        var inQuotes = false;
        var fieldQuoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0 && !fieldQuoted:
                    inQuotes = fieldQuoted = true;
                    break;
                case '"':
                    throw new FormatException($"Line {line}: a quote may only appear inside a quoted field.");
                case ',':
                    EndField();
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRow();
                    line++;
                    rowStart = line;
                    break;
                default:
                    if (fieldQuoted)
                    {
                        throw new FormatException($"Line {line}: unexpected text after a closing quote.");
                    }

                    field.Append(c);
                    break;
            }
        }

        if (inQuotes)
        {
            throw new FormatException($"Line {rowStart}: a quoted field is not closed.");
        }

        EndRow();
        return rows;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            fieldQuoted = false;
        }

        void EndRow()
        {
            EndField();
            if (fields.Count > 1 || fields[0].Trim().Length > 0)
            {
                rows.Add(new Row(rowStart, [.. fields]));
            }

            fields.Clear();
        }
    }
}
