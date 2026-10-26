using Axis.Data.Records;

namespace Axis.Data.Tests;

public sealed class RecordImportXlsxTests
{
    [Fact]
    public void The_first_sheet_gives_its_strings_numbers_dates_and_booleans_as_text_and_other_sheets_are_ignored()
    {
        var first = XlsxFiles.Sheet(
            """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="inlineStr"><is><t>count</t></is></c><c r="C1" t="s"><v>1</v></c><c r="D1" t="s"><v>2</v></c><c r="E1" t="s"><v>3</v></c><c r="F1" t="s"><v>4</v></c></row>
            <row r="2"><c r="A2" t="s"><v>5</v></c><c r="B2"><v>42</v></c><c r="C2" s="1"><v>46321</v></c><c r="D2" s="2"><v>46321.5</v></c><c r="E2" t="b"><v>1</v></c><c r="F2" t="b"><v>0</v></c></row>
            <row r="3"><c r="A3" t="inlineStr"><is><r><t>two </t></r><r><t>runs</t></r><rPh><t>x</t></rPh></is></c><c r="B3" t="n"><v>1.5</v></c><c r="C3"><v>.25</v></c><c r="D3" t="str"><v>cached</v></c><c r="E3"><v>1E-05</v></c><c r="F3" t="s"><v>6</v></c></row>
            """);
        var second = XlsxFiles.Sheet(XlsxFiles.Row(1, ["other", "sheet"]) + XlsxFiles.Row(2, ["ignored", "9"]));
        var file = XlsxFiles.Workbook([first, second], ["name", "day", "moment", "yes", "no", "Desk", "Chair"]);

        var xlsx = RecordImportXlsx.Open(file);
        var rows = xlsx.Rows().ToList();

        Assert.Null(xlsx.HeaderError);
        Assert.Equal(["name", "count", "day", "moment", "yes", "no"], xlsx.Header);
        Assert.Equal(
            [(1, "Desk|42|2026-10-26|2026-10-26T12:00:00Z|true|false"), (2, "two runs|1.5|0.25|cached|1E-05|Chair")],
            rows.Select(row => (row.Number, string.Join('|', row.Cells!))));
    }

    [Fact]
    public void Gaps_are_empty_cells_blank_rows_are_skipped_and_a_row_number_is_the_sheet_row_minus_the_header_row()
    {
        var sheet = XlsxFiles.Sheet(
            """
            <row r="2"/>
            <row r="3"><c r="B3" t="inlineStr"><is><t>b</t></is></c><c r="D3" t="inlineStr"><is><t>d</t></is></c></row>
            <row r="4"><c r="B4" t="inlineStr"><is><t></t></is></c></row>
            <row r="5"><c r="A5"><v>1</v></c><c r="C5"><v>3</v></c></row>
            <row r="8"><c r="D8"><v>4</v></c></row>
            """);

        var xlsx = RecordImportXlsx.Open(XlsxFiles.Workbook([sheet]));
        var rows = xlsx.Rows().ToList();

        Assert.Equal(["", "b", "", "d"], xlsx.Header);
        Assert.Equal(
            [(2, "1||3|"), (5, "|||4")],
            rows.Select(row => (row.Number, string.Join('|', row.Cells!))));
    }

    [Fact]
    public void A_value_to_the_right_of_the_header_and_a_cell_that_holds_an_error_are_errors_of_their_row()
    {
        var sheet = XlsxFiles.Sheet(
            """
            <row r="1"><c r="A1" t="inlineStr"><is><t>a</t></is></c><c r="B1" t="inlineStr"><is><t>b</t></is></c></row>
            <row r="2"><c r="A2"><v>1</v></c><c r="B2"><v>2</v></c><c r="C2"><v>3</v></c></row>
            <row r="3"><c r="A3" t="e"><v>#DIV/0!</v></c></row>
            <row r="4"><c r="A4" s="1"><v>60</v></c></row>
            <row r="5"><c r="A5"><v>5</v></c><c r="D5" t="inlineStr"><is><t></t></is></c></row>
            """);

        var rows = RecordImportXlsx.Open(XlsxFiles.Workbook([sheet])).Rows().ToList();

        Assert.Equal(
            [
                (1, (string?)null, "Has a value in column C, beyond the header."),
                (2, null, "Cell A3 holds the error #DIV/0!."),
                (3, null, "Cell A4 holds a date that does not exist."),
                (4, "5|", null),
            ],
            rows.Select(row => (row.Number, row.Cells is null ? null : string.Join('|', row.Cells), row.Error)));
    }

    [Fact]
    public void Dates_count_from_the_base_of_the_workbook_and_a_time_is_rounded_to_whole_seconds()
    {
        var sheet = XlsxFiles.Sheet(
            """
            <row r="1"><c r="A1" t="inlineStr"><is><t>d</t></is></c></row>
            <row r="2"><c r="A2" s="1"><v>1</v></c></row>
            <row r="3"><c r="A3" s="1"><v>59</v></c></row>
            <row r="4"><c r="A4" s="1"><v>61</v></c></row>
            <row r="5"><c r="A5" s="2"><v>46321.00000001</v></c></row>
            <row r="6"><c r="A6" s="2"><v>46321.999999999</v></c></row>
            """);

        var one900 = RecordImportXlsx.Open(XlsxFiles.Workbook([sheet])).Rows().Select(row => row.Cells![0]).ToList();
        var one904 = RecordImportXlsx.Open(XlsxFiles.Workbook([sheet], date1904: true)).Rows().Select(row => row.Cells![0]).ToList();

        Assert.Equal(["1900-01-01", "1900-02-28", "1900-03-01", "2026-10-26", "2026-10-27"], one900);
        Assert.Equal(["1904-01-02", "1904-02-29", "1904-03-02", "2030-10-27", "2030-10-28"], one904);
    }

    [Fact]
    public void A_workbook_without_a_row_has_no_header_and_no_error()
    {
        var xlsx = RecordImportXlsx.Open(XlsxFiles.Workbook([XlsxFiles.Sheet("")]));

        Assert.Null(xlsx.Header);
        Assert.Null(xlsx.HeaderError);
        Assert.Empty(xlsx.Rows());
    }

    [Fact]
    public void Bytes_that_are_no_workbook_and_a_workbook_over_the_entry_limit_give_a_header_error_and_no_rows()
    {
        var garbage = RecordImportXlsx.Open("name,quantity\nDesk,1\n"u8.ToArray());
        var empty = RecordImportXlsx.Open(ReadOnlyMemory<byte>.Empty);
        var noWorkbook = RecordImportXlsx.Open(XlsxFiles.ZipWithEntries(3));
        var tooMany = RecordImportXlsx.Open(XlsxFiles.ZipWithEntries(1001));
        var broken = RecordImportXlsx.Open(XlsxFiles.Workbook(["<worksheet><sheetData><row"]));

        Assert.Equal("Is not a readable xlsx workbook.", garbage.HeaderError);
        Assert.Equal("Is not a readable xlsx workbook.", empty.HeaderError);
        Assert.Equal("Is not a readable xlsx workbook.", noWorkbook.HeaderError);
        Assert.Equal("Unpacks to more than the import limit.", tooMany.HeaderError);
        Assert.Equal("Is not a readable xlsx workbook.", broken.HeaderError);
        Assert.All([garbage, empty, noWorkbook, tooMany, broken], xlsx =>
        {
            Assert.Null(xlsx.Header);
            Assert.Empty(xlsx.Rows());
        });
    }

    [Fact]
    public void A_sheet_that_breaks_after_the_header_ends_with_one_row_error()
    {
        var sheet = XlsxFiles.Sheet("""<row r="1"><c r="A1" t="inlineStr"><is><t>a</t></is></c></row><row r="2"><c r="A2"><v>1</v></c></row><row r="3"><c r="A3"><v>2</v></c></row""");

        var xlsx = RecordImportXlsx.Open(XlsxFiles.Workbook([sheet]));
        var rows = xlsx.Rows().ToList();

        Assert.Null(xlsx.HeaderError);
        Assert.Equal(["1"], rows.Take(1).Select(row => row.Cells![0]));
        Assert.Equal("Is not a readable xlsx workbook.", rows[^1].Error);
    }
}
