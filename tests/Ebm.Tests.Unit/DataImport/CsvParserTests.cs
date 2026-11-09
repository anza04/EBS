using Ebm.DataImport.Sources;
using Ebm.Services.Import;

namespace Ebm.Tests.Unit.DataImport;

public sealed class CsvParserTests
{
    [Fact]
    public void Parses_header_and_rows_with_line_numbers()
    {
        var rows = CsvParser.Parse("code,name\r\nITA,Italy\r\n\r\nESP,Spain\r\n");

        Assert.Equal(3, rows.Count);
        Assert.Equal(["code", "name"], rows[0].Fields);
        Assert.Equal(["ESP", "Spain"], rows[2].Fields);
        Assert.Equal(4, rows[2].Line);
    }

    [Fact]
    public void Handles_quotes_commas_escaped_quotes_and_line_breaks()
    {
        var rows = CsvParser.Parse("a,b,c\n\"x, y\",\"say \"\"hi\"\"\",\"two\nlines\"\nnext,,\n");

        Assert.Equal(["x, y", "say \"hi\"", "two\nlines"], rows[1].Fields);
        Assert.Equal(["next", string.Empty, string.Empty], rows[2].Fields);
        Assert.Equal(4, rows[2].Line);
    }

    [Theory]
    [InlineData("a,\"open\n")]
    [InlineData("a,b\"c\n")]
    [InlineData("a,\"b\"c\n")]
    public void Rejects_malformed_quotes(string text) =>
        Assert.Throws<FormatException>(() => CsvParser.Parse(text));

    [Theory]
    [InlineData("players.csv", ImportRecordKind.Player)]
    [InlineData("Players-2026.csv", ImportRecordKind.Player)]
    [InlineData("clubs_update.csv", ImportRecordKind.Club)]
    [InlineData("countries.csv", ImportRecordKind.Country)]
    public void Csv_record_type_comes_from_the_file_name(string file, ImportRecordKind kind) =>
        Assert.Equal(kind, ImportFields.ForCsvFile(file)?.Kind);

    [Theory]
    [InlineData("playersx.csv")]
    [InlineData("roster.csv")]
    public void Unknown_csv_file_names_have_no_record_type(string file) =>
        Assert.Null(ImportFields.ForCsvFile(file));
}
