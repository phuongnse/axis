using System.Text;
using Axis.Configuration.Model;
using Axis.Data.Records;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class RecordImportCsvTests
{
    private static readonly EntityModel _entity = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1b01"),
        "Order",
        "entities/order.json",
        Field("name", FieldType.Text),
        Field("Count", FieldType.Integer),
        Field("price", FieldType.Decimal),
        Field("active", FieldType.Boolean),
        Field("attachment", FieldType.File),
        Field("lines", FieldType.ChildCollection, target: Entity(Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1b02"), "Line", "entities/line.json")));

    [Fact]
    public void Quoted_cells_keep_commas_doubled_quotes_and_line_breaks()
    {
        var csv = Open("a,b\n\"x,y\",\"say \"\"hi\"\"\"\n\"line1\nline2\",\"c\r\nd\"\n");

        var rows = csv.Rows().ToList();

        Assert.Equal(["a", "b"], csv.Header);
        Assert.Equal(
            [(1, "x,y|say \"hi\""), (2, "line1\nline2|c\r\nd")],
            rows.Select(row => (row.Number, string.Join('|', row.Cells!))));
    }

    [Fact]
    public void A_leading_byte_order_mark_is_skipped_and_both_line_endings_end_a_row()
    {
        var csv = Open("﻿a,b\r\n1,2\r\n3,4\n5,6\r\n");

        Assert.Equal(["a", "b"], csv.Header);
        Assert.Equal(["1|2", "3|4", "5|6"], csv.Rows().Select(row => string.Join('|', row.Cells!)));
    }

    [Fact]
    public void The_line_break_that_ends_the_last_row_is_no_row_and_empty_cells_are_kept()
    {
        var withBreak = Open("a,b\n1,\n,2\n");
        var without = Open("a,b\n1,");

        Assert.Equal(["1|", "|2"], withBreak.Rows().Select(row => string.Join('|', row.Cells!)));
        Assert.Equal(["1|"], without.Rows().Select(row => string.Join('|', row.Cells!)));
        Assert.Empty(Open("a,b\n").Rows());
    }

    [Fact]
    public void A_file_with_no_row_has_no_header()
    {
        Assert.Null(Open("").Header);
        Assert.Null(Open("﻿").Header);
    }

    [Fact]
    public void A_row_with_the_wrong_number_of_cells_is_an_error_that_names_its_row_and_the_next_row_is_read()
    {
        var rows = Open("a,b\n1\n2,3\n4,5,6\n").Rows().ToList();

        Assert.Equal([1, 2, 3], rows.Select(row => row.Number));
        Assert.Equal(["Has 1 cells, but the header has 2.", null, "Has 3 cells, but the header has 2."], rows.Select(row => row.Error));
        Assert.Equal(["2|3"], rows.Where(row => row.Error is null).Select(row => string.Join('|', row.Cells!)));
    }

    [Fact]
    public void A_quote_that_is_never_closed_ends_the_file_with_an_error_at_its_row()
    {
        var rows = Open("a,b\n1,2\n3,\"4\n5,6\n").Rows().ToList();

        Assert.Equal([1, 2], rows.Select(row => row.Number));
        Assert.Equal("Has a quote that is never closed.", rows[1].Error);
    }

    [Theory]
    [InlineData("a,b\n1,x\"y\n2,3\n", "Has a quote inside a cell that does not start with one.")]
    [InlineData("a,b\n1,\"x\"y\n2,3\n", "Has text after the closing quote of a cell.")]
    public void Broken_quoting_is_an_error_at_its_row_and_reading_goes_on_with_the_next_row(string file, string error)
    {
        var rows = Open(file).Rows().ToList();

        Assert.Equal([1, 2], rows.Select(row => row.Number));
        Assert.Equal(error, rows[0].Error);
        Assert.Null(rows[1].Error);
    }

    [Fact]
    public void Bytes_that_are_not_utf8_are_an_error_at_their_row()
    {
        byte[] file = [.. "a,b\n1,2\n"u8, 0x33, 0x2C, 0xFF, 0x0A, .. "5,6\n"u8];

        var rows = RecordImportCsv.Open(file).Rows().ToList();

        Assert.Equal(["1|2", "Is not valid UTF-8.", "5|6"], rows.Select(row => row.Cells is null ? row.Error : string.Join('|', row.Cells)));
    }

    [Fact]
    public void Header_names_are_matched_to_fields_ignoring_letter_case()
    {
        var csv = Open("NAME,count,Price,ACTIVE\nDesk,3,1.50,TRUE\n");

        var (columns, errors) = RecordImportColumns.Map(csv.Header!, _entity);

        Assert.Null(errors);
        var body = Encoding.UTF8.GetString(columns!.Body(csv.Rows().Single().Cells!));
        Assert.Equal("""{"values":{"name":"Desk","Count":3,"price":1.50,"active":true}}""", body);
    }

    [Fact]
    public void An_unknown_repeated_or_collection_column_is_an_error_at_its_position()
    {
        var (columns, errors) = RecordImportColumns.Map(["name", "nothing", "NAME", "lines", ""], _entity);

        Assert.Null(columns);
        Assert.Equal(["/columns/2", "/columns/3", "/columns/4", "/columns/5"], errors!.Keys);
        Assert.Equal("Is not a field of this entity.", errors["/columns/2"][0]);
        Assert.Equal("Is named more than once.", errors["/columns/3"][0]);
    }

    [Fact]
    public void A_file_column_is_an_error_at_its_position()
    {
        var (columns, errors) = RecordImportColumns.Map(["name", "attachment"], _entity);

        Assert.Null(columns);
        Assert.Equal(["/columns/2"], errors!.Keys);
        Assert.Equal("Is a file, which an import cannot set.", errors["/columns/2"][0]);
    }

    [Fact]
    public void A_cell_becomes_a_json_value_by_its_fields_type_and_an_empty_cell_is_left_out()
    {
        var (columns, _) = RecordImportColumns.Map(["name", "count", "price", "active"], _entity);

        Assert.Equal(
            """{"values":{"name":"12","Count":"abc","price":-1.5e3,"active":"yes"}}""",
            Encoding.UTF8.GetString(columns!.Body(["12", "abc", "-1.5e3", "yes"])));
        Assert.Equal(
            """{"values":{"Count":"01","active":false}}""",
            Encoding.UTF8.GetString(columns.Body(["", "01", "", "False"])));
    }

    private static RecordImportCsv Open(string text, bool withBom = false) =>
        RecordImportCsv.Open(withBom ? Encoding.UTF8.GetBytes(text.TrimStart('﻿')).Prepend((byte)0xEF).Prepend((byte)0xBB).Prepend((byte)0xBF).Reverse().ToArray() : Encoding.UTF8.GetBytes(text));
}
