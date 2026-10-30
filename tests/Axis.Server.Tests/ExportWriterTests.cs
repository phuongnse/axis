using System.Text;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Server.Exports;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Configuration;

namespace Axis.Server.Tests;

public sealed class ExportWriterTests
{
    private const string TargetId = "5b9e1c1e-0000-4000-8000-000000000001";
    private const string OtherId = "5b9e1c1e-0000-4000-8000-000000000002";

    private static readonly IReadOnlyDictionary<FieldType, (JsonNode Value, JsonNode? Label, string Text)> _samples =
        new Dictionary<FieldType, (JsonNode, JsonNode?, string)>
        {
            [FieldType.Text] = (JsonValue.Create("Hello, \"world\"")!, null, "Hello, \"world\""),
            [FieldType.Integer] = (JsonValue.Create(-5L)!, null, "-5"),
            [FieldType.Decimal] = (JsonNode.Parse("-1.50")!, null, "-1.50"),
            [FieldType.Boolean] = (JsonValue.Create(true)!, null, "true"),
            [FieldType.Date] = (JsonValue.Create("2026-01-31")!, null, "2026-01-31"),
            [FieldType.DateTime] = (JsonValue.Create("2026-01-31T10:00:00.000000Z")!, null, "2026-01-31T10:00:00.000000Z"),
            [FieldType.Enum] = (JsonValue.Create("open")!, null, "open"),
            [FieldType.Reference] = (JsonValue.Create(TargetId)!, JsonValue.Create("Alpha"), "Alpha"),
            [FieldType.References] = (new JsonArray(TargetId, OtherId), new JsonArray("A", "B"), "A; B"),
            [FieldType.File] = (new JsonObject { ["name"] = "invoice.pdf", ["size"] = 3 }, null, "invoice.pdf"),
            [FieldType.Money] = (JsonNode.Parse("12345678901234567890.10")!, null, "12345678901234567890.10"),
        };

    // Object and List are connector input types, refused by the entity schema.
    private static readonly FieldType[] _types = [.. Enum.GetValues<FieldType>().Where(type => type is not (FieldType.Object or FieldType.List))];

    public static TheoryData<FieldType> ColumnTypes() =>
        [.. _types.Where(type => type != FieldType.ChildCollection)];

    public static TheoryData<char> FormulaCharacters() => ['=', '+', '-', '@', '\t', '\r'];

    [Fact]
    public void Every_field_type_has_a_sample_or_is_the_child_collection()
    {
        Assert.Equal(
            _types.Where(type => type != FieldType.ChildCollection).Order(),
            _samples.Keys.Order());
    }

    [Fact]
    public void A_child_collection_is_never_a_column()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExportValues.Format(FieldType.ChildCollection, new JsonArray(), null));
    }

    [Theory]
    [MemberData(nameof(ColumnTypes))]
    public void A_value_has_its_machine_form(FieldType type)
    {
        var (value, label, text) = _samples[type];

        Assert.Equal(text, ExportValues.Format(type, value, label));
        Assert.Null(ExportValues.Format(type, null, label));
    }

    [Fact]
    public void A_reference_without_a_label_is_its_id()
    {
        Assert.Equal(TargetId, ExportValues.Format(FieldType.Reference, JsonValue.Create(TargetId), null));
        Assert.Equal($"A; {OtherId}", ExportValues.Format(FieldType.References, new JsonArray(TargetId, OtherId), new JsonArray("A")));
    }

    [Theory]
    [MemberData(nameof(ColumnTypes))]
    public void Csv_holds_the_header_and_the_value_of_every_field_type(FieldType type)
    {
        var text = ExportValues.Format(type, _samples[type].Value, _samples[type].Label);

        var rows = ReadCsv([new ExportColumn("Value", type)], [[text]]);

        Assert.Equal(["Value"], rows[0]);
        Assert.Equal([_samples[type].Text], rows[1]);
    }

    [Theory]
    [MemberData(nameof(ColumnTypes))]
    public void Xlsx_holds_the_header_and_the_value_of_every_field_type(FieldType type)
    {
        var text = ExportValues.Format(type, _samples[type].Value, _samples[type].Label);

        var rows = ReadXlsx([new ExportColumn("Value", type)], [[text]]);

        Assert.Equal(2, rows.Count);
        Assert.Equal(CellValues.InlineString, rows[0][0]!.DataType!.Value);
        Assert.Equal("Value", rows[0][0]!.InlineString!.Text!.Text);
        var cell = rows[1][0]!;
        Assert.Null(cell.CellFormula);
        switch (type)
        {
            case FieldType.Integer or FieldType.Decimal or FieldType.Money:
                Assert.Equal(CellValues.Number, cell.DataType!.Value);
                Assert.Equal(_samples[type].Text, cell.CellValue!.Text);
                break;
            case FieldType.Boolean:
                Assert.Equal(CellValues.Boolean, cell.DataType!.Value);
                Assert.Equal("1", cell.CellValue!.Text);
                break;
            default:
                Assert.Equal(CellValues.InlineString, cell.DataType!.Value);
                Assert.Equal(_samples[type].Text, cell.InlineString!.Text!.Text);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(FormulaCharacters))]
    public void A_text_cell_that_starts_with_a_formula_character_is_neutralised_in_csv(char first)
    {
        foreach (var type in new[] { FieldType.Text, FieldType.Enum, FieldType.Reference, FieldType.File })
        {
            var cell = first + "SUM(A1)";

            var rows = ReadCsv([new ExportColumn("Value", type)], [[cell]]);

            Assert.Equal(["'" + cell], rows[1]);
        }
    }

    [Theory]
    [MemberData(nameof(FormulaCharacters))]
    public void A_text_cell_that_starts_with_a_formula_character_is_text_in_xlsx(char first)
    {
        var cell = first + "SUM(A1)";

        var rows = ReadXlsx([new ExportColumn("Value", FieldType.Text)], [[cell]]);

        var written = rows[1][0]!;
        Assert.Equal(CellValues.InlineString, written.DataType!.Value);
        Assert.Null(written.CellFormula);
        Assert.Null(written.CellValue);
        Assert.EndsWith("SUM(A1)", written.InlineString!.Text!.Text, StringComparison.Ordinal);
        Assert.Equal(first, written.InlineString.Text.Text[0] == '\n' ? '\r' : written.InlineString.Text.Text[0]);
    }

    [Fact]
    public void A_number_is_never_prefixed_in_csv()
    {
        var rows = ReadCsv(
            [new ExportColumn("Integer", FieldType.Integer), new ExportColumn("Decimal", FieldType.Decimal), new ExportColumn("Money", FieldType.Money)],
            [["-5", "-1.50", "-0.10"]]);

        Assert.Equal(["-5", "-1.50", "-0.10"], rows[1]);
    }

    [Fact]
    public void A_missing_value_is_an_empty_cell_that_keeps_the_columns_in_line()
    {
        ExportColumn[] columns = [new("A", FieldType.Text), new("B", FieldType.Integer), new("C", FieldType.Text)];

        var csv = ReadCsv(columns, [["x", null, "z"]]);
        var xlsx = ReadXlsx(columns, [["x", null, "z"]]);

        Assert.Equal(["x", "", "z"], csv[1]);
        Assert.Equal("x", xlsx[1][0]!.InlineString!.Text!.Text);
        Assert.Null(xlsx[1][1]!.CellValue);
        Assert.Equal("z", xlsx[1][2]!.InlineString!.Text!.Text);
    }

    [Fact]
    public void Csv_starts_with_a_byte_order_mark_and_ends_rows_with_crlf()
    {
        using var stream = new MemoryStream();
        using (var writer = new CsvExportWriter(stream))
        {
            writer.WriteHeader([new ExportColumn("Name", FieldType.Text)]);
            writer.WriteRow(["Nguyễn, Văn\r\nA"]);
            writer.Complete();
        }

        var bytes = stream.ToArray();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("Name\r\n\"Nguyễn, Văn\r\nA\"\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void Xlsx_keeps_text_that_xml_cannot_hold_readable()
    {
        var rows = ReadXlsx([new ExportColumn("Value", FieldType.Text)], [["a\u0001b  c 😀"]]);

        Assert.Equal("a�b  c 😀", rows[1][0]!.InlineString!.Text!.Text);
    }

    [Fact]
    public void The_stream_stays_open_after_xlsx_completes()
    {
        using var stream = new MemoryStream();
        using (var writer = new XlsxExportWriter(stream))
        {
            writer.WriteHeader([new ExportColumn("Value", FieldType.Text)]);
            writer.Complete();
        }

        Assert.True(stream.CanSeek);
        Assert.True(stream.Length > 0);
    }

    [Theory]
    [InlineData("csv", "Csv")]
    [InlineData("xlsx", "Xlsx")]
    public void A_format_is_read_exactly(string text, string expected)
    {
        var errors = new SortedDictionary<string, string[]>();

        Assert.True(ExportFile.TryParseFormat(text, errors, out var format));
        Assert.Equal(expected, format.ToString());
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CSV")]
    [InlineData("xls")]
    [InlineData("csv,xlsx")]
    public void A_missing_or_unknown_format_is_an_error_at_format(string? text)
    {
        var errors = new SortedDictionary<string, string[]>();

        Assert.False(ExportFile.TryParseFormat(text, errors, out _));
        Assert.Equal(["format"], errors.Keys);
    }

    [Fact]
    public void The_row_limit_defaults_to_one_hundred_thousand()
    {
        Assert.Equal(100_000, Load(null).MaxRows);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("2147483646", int.MaxValue - 1)]
    public void The_row_limit_is_read(string value, int expected)
    {
        Assert.Equal(expected, Load(value).MaxRows);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483647")]
    [InlineData("many")]
    [InlineData("1.5")]
    public void A_row_limit_out_of_range_stops_the_start(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Load(value));

        Assert.Contains(ExportSettings.MaxRowsSetting, exception.Message, StringComparison.Ordinal);
    }

    private static ExportSettings Load(string? value) =>
        ExportSettings.Load(new ConfigurationBuilder()
            .AddInMemoryCollection(value is null ? [] : [new KeyValuePair<string, string?>(ExportSettings.MaxRowsSetting, value)])
            .Build());

    private static List<List<string>> ReadCsv(IReadOnlyList<ExportColumn> columns, string?[][] rows)
    {
        using var stream = new MemoryStream();
        using (var writer = new CsvExportWriter(stream))
        {
            writer.WriteHeader(columns);
            foreach (var row in rows)
            {
                writer.WriteRow(row);
            }

            writer.Complete();
        }

        return CsvText.Parse(Encoding.UTF8.GetString(stream.ToArray()).TrimStart('﻿'));
    }

    private static List<List<Cell?>> ReadXlsx(IReadOnlyList<ExportColumn> columns, string?[][] rows)
    {
        using var stream = new MemoryStream();
        using (var writer = new XlsxExportWriter(stream))
        {
            writer.WriteHeader(columns);
            foreach (var row in rows)
            {
                writer.WriteRow(row);
            }

            writer.Complete();
        }

        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbook = document.WorkbookPart!;
        var sheets = workbook.Workbook!.Sheets!;
        var sheet = Assert.Single(sheets.Elements<Sheet>());
        Assert.Equal("Export", sheet.Name!.Value);
        var data = ((WorksheetPart)workbook.GetPartById(sheet.Id!.Value!)).Worksheet!.GetFirstChild<SheetData>()!;
        return [.. data.Elements<Row>().Select(row => row.Elements<Cell>().Select(cell => (Cell?)cell).ToList())];
    }
}
