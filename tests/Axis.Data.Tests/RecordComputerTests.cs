using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Records;
using static Axis.Data.Tests.Models;
using Record = Axis.Data.Records.Record;

namespace Axis.Data.Tests;

public sealed class RecordComputerTests
{
    private static readonly FieldModel _quantity = Field("quantity", FieldType.Integer);

    private static readonly FieldModel _price = Field("price", FieldType.Decimal, precision: 20, scale: 2);

    private static readonly FieldModel _name = Field("name", FieldType.Text);

    private static readonly FieldModel _qty = Field("qty", FieldType.Integer);

    private static readonly EntityModel _line = Entity(
        Guid.Parse("22222222-2222-4222-8222-222222222222"),
        "Line",
        "entities/line.json",
        _qty,
        Computed(Field("doubled", FieldType.Integer), "qty * 2", [_qty]));

    private static readonly FieldModel _lines = Field("lines", FieldType.ChildCollection, target: _line);

    private static readonly EntityModel _order = Order(Computed(Field("total", FieldType.Decimal, precision: 20, scale: 2), "quantity * price", [_quantity, _price, _name]));

    /// <summary>An Order whose totals aggregate its lines, reading each line's computed <c>doubled</c>.</summary>
    private static readonly EntityModel _totalled = Order(
        Computed(Field("linesTotal", FieldType.Integer), "sum(lines, doubled)", [_quantity, _price, _name, _lines], ChildOf),
        Computed(Field("lineCount", FieldType.Integer), "count(lines)", [_quantity, _price, _name, _lines], ChildOf));

    private static readonly ApplicationModel _application = Application(_order, _line);

    [Fact]
    public void Create_computes_the_value_and_lists_it_in_declaration_order()
    {
        var result = RecordComputer.ComputeCreate(_application, _order, Values(_order, ("price", "1.50"), ("quantity", 2L)), []);

        Assert.Null(result.Errors);
        Assert.Equal(
            [("quantity", (object?)2L), ("price", "1.50"), ("total", "3.00")],
            result.Values.Select(value => (value.Field.Name, value.Value)));
    }

    [Fact]
    public void Null_input_computes_null()
    {
        var result = RecordComputer.ComputeCreate(_application, _order, Values(_order, ("quantity", 2L)), []);

        Assert.Null(result.Errors);
        Assert.Null(Assert.Single(result.Values, value => value.Field.Name == "total").Value);
    }

    [Fact]
    public void Update_computes_from_the_stored_record_merged_with_the_changes()
    {
        var stored = Stored(new Dictionary<string, JsonNode?>
        {
            ["quantity"] = JsonValue.Create(2L),
            ["price"] = JsonNode.Parse("1.50"),
            ["name"] = JsonValue.Create("Desk"),
            ["total"] = JsonNode.Parse("3.00"),
            ["lines"] = new JsonArray(),
        });

        var changed = RecordComputer.ComputeUpdate(_application, _order, stored, Values(_order, ("quantity", 3L)), []);
        var empty = RecordComputer.ComputeUpdate(_application, _order, stored, [], []);

        Assert.Null(changed.Errors);
        Assert.Equal([("quantity", (object?)3L), ("total", "4.50")], changed.Values.Select(value => (value.Field.Name, value.Value)));
        Assert.Null(empty.Errors);
        Assert.Equal([("total", (object?)"3.00")], empty.Values.Select(value => (value.Field.Name, value.Value)));
    }

    [Fact]
    public void Update_of_an_entity_with_computed_fields_needs_the_stored_record()
    {
        Assert.Throws<ArgumentException>(() => RecordComputer.ComputeUpdate(_application, _order, null, [], []));
    }

    [Fact]
    public void Integer_result_widens_to_a_decimal_field()
    {
        var entity = Order(Computed(Field("twice", FieldType.Decimal), "quantity * 2", [_quantity, _price, _name]));

        var result = RecordComputer.ComputeCreate(_application, entity, Values(entity, ("quantity", 2L)), []);

        Assert.Null(result.Errors);
        Assert.Equal("4", Assert.Single(result.Values, value => value.Field.Name == "twice").Value);
    }

    [Fact]
    public void Run_time_error_is_an_error_at_the_computed_field()
    {
        var entity = Order(Computed(Field("ratio", FieldType.Decimal), "1 / quantity", [_quantity, _price, _name]));

        var result = RecordComputer.ComputeCreate(_application, entity, Values(entity, ("quantity", 0L)), []);

        Assert.NotNull(result.Errors);
        var error = Assert.Single(result.Errors);
        Assert.Equal(("/values/ratio", "Could not be computed."), (error.Key, Assert.Single(error.Value)));
    }

    [Theory]
    [InlineData("price", "decimal", "1.25", "Must have at most 1 fraction digits.")]
    [InlineData("upper(name)", "text", "abcdef", "Must be at most 5 characters.")]
    [InlineData("concat(name, '\u0000')", "text", "ab", "Must not contain the null character.")]
    public void Value_that_does_not_fit_the_column_is_an_error_at_the_computed_field_and_is_never_rounded(
        string expression, string type, string input, string message)
    {
        var field = type == "decimal"
            ? Field("result", FieldType.Decimal, precision: 10, scale: 1)
            : Field("result", FieldType.Text, maxLength: 5);
        var entity = Order(Computed(field, expression, [_quantity, _price, _name]));
        var values = type == "decimal" ? Values(entity, ("price", input)) : Values(entity, ("name", input));

        var result = RecordComputer.ComputeCreate(_application, entity, values, []);

        Assert.NotNull(result.Errors);
        var error = Assert.Single(result.Errors);
        Assert.Equal(("/values/result", message), (error.Key, Assert.Single(error.Value)));
    }

    [Fact]
    public void Decimal_input_that_cannot_be_held_exactly_is_an_error_at_its_own_field()
    {
        var result = RecordComputer.ComputeCreate(_application, _order, Values(_order, ("quantity", 1L), ("price", "1.0000000000000000000000000000001")), []);

        Assert.NotNull(result.Errors);
        var error = Assert.Single(result.Errors);
        Assert.Equal(("/values/price", "Cannot be evaluated exactly."), (error.Key, Assert.Single(error.Value)));
    }

    [Fact]
    public void Each_child_row_gets_its_own_computed_value_and_its_errors_at_its_row_path()
    {
        var lines = _order.Fields.Single(field => field.Name == "lines");
        var rows = new RecordRows(lines, _line, [Row(2L), Row(null), Row(long.MaxValue)]);

        var result = RecordComputer.ComputeCreate(_application, _order, [], [rows]);

        Assert.NotNull(result.Errors);
        var error = Assert.Single(result.Errors);
        Assert.Equal(("/values/lines/2/doubled", "Could not be computed."), (error.Key, Assert.Single(error.Value)));
        var computed = Assert.Single(result.Rows);
        Assert.Equal(
            [
                [("qty", (object?)2L), ("doubled", 4L)],
                [("qty", null), ("doubled", null)],
            ],
            computed.Rows.Take(2).Select(row => row.Select(value => (value.Field.Name, value.Value)).ToList()));
    }

    [Fact]
    public void Entity_without_computed_fields_returns_the_same_lists()
    {
        var entity = Order();
        var values = Values(entity, ("quantity", 2L));
        var lines = entity.Fields.Single(field => field.Name == "lines");
        IReadOnlyList<RecordRows> rows = [new RecordRows(lines, Entity(_line.Id, "Line", "entities/line.json", _qty), [[new RecordValue(_qty, 1L)]])];

        var result = RecordComputer.ComputeCreate(_application, entity, values, rows);

        Assert.Null(result.Errors);
        Assert.Same(values, result.Values);
        Assert.Same(rows, result.Rows);
    }

    [Fact]
    public void Owner_aggregates_read_the_rows_computed_values()
    {
        var rows = new RecordRows(_lines, _line, [Row(2L), Row(null), Row(3L)]);

        var result = RecordComputer.ComputeCreate(_application, _totalled, [], [rows]);

        Assert.Null(result.Errors);
        Assert.Equal([("linesTotal", (object?)10L), ("lineCount", 3L)], Totals(result));
    }

    [Fact]
    public void Create_without_rows_aggregates_an_empty_list()
    {
        var result = RecordComputer.ComputeCreate(_application, _totalled, [], []);

        Assert.Null(result.Errors);
        Assert.Equal([("linesTotal", (object?)0L), ("lineCount", 0L)], Totals(result));
    }

    [Fact]
    public void Update_that_leaves_the_rows_out_aggregates_the_stored_rows()
    {
        var stored = Stored(new Dictionary<string, JsonNode?>
        {
            ["quantity"] = null,
            ["price"] = null,
            ["name"] = null,
            ["linesTotal"] = JsonValue.Create(0L),
            ["lineCount"] = JsonValue.Create(0L),
            ["lines"] = new JsonArray(StoredLine(1L, 2L), StoredLine(5L, 10L)),
        });

        var result = RecordComputer.ComputeUpdate(_application, _totalled, stored, [], []);

        Assert.Null(result.Errors);
        Assert.Equal([("linesTotal", (object?)12L), ("lineCount", 2L)], Totals(result));
    }

    [Fact]
    public void Update_that_sends_rows_aggregates_them_instead_of_the_stored_rows()
    {
        var stored = Stored(new Dictionary<string, JsonNode?>
        {
            ["quantity"] = null,
            ["price"] = null,
            ["name"] = null,
            ["linesTotal"] = JsonValue.Create(12L),
            ["lineCount"] = JsonValue.Create(2L),
            ["lines"] = new JsonArray(StoredLine(1L, 2L), StoredLine(5L, 10L)),
        });
        var rows = new RecordRows(_lines, _line, [Row(4L)]);

        var result = RecordComputer.ComputeUpdate(_application, _totalled, stored, [], [rows]);

        Assert.Null(result.Errors);
        Assert.Equal([("linesTotal", (object?)8L), ("lineCount", 1L)], Totals(result));
    }

    [Fact]
    public void Stored_row_decimal_that_cannot_be_held_exactly_leaves_the_owner_uncomputed()
    {
        var amount = Field("amount", FieldType.Decimal);
        var line = Entity(_line.Id, "Line", "entities/line.json", amount);
        var lines = Field("lines", FieldType.ChildCollection, target: line);
        var entity = Order(Computed(
            Field("linesTotal", FieldType.Decimal), "sum(lines, amount)", [_quantity, lines], field => field.Target?.Id == line.Id ? line : null));
        var stored = Stored(new Dictionary<string, JsonNode?>
        {
            ["quantity"] = null,
            ["price"] = null,
            ["name"] = null,
            ["linesTotal"] = null,
            ["lines"] = new JsonArray(new JsonObject { ["amount"] = JsonNode.Parse("1.0000000000000000000000000000001") }),
        });

        var result = RecordComputer.ComputeUpdate(Application(entity, line), entity, stored, [], []);

        Assert.NotNull(result.Errors);
        var error = Assert.Single(result.Errors);
        Assert.Equal(("/values/lines/0/amount", "Cannot be evaluated exactly."), (error.Key, Assert.Single(error.Value)));
        Assert.DoesNotContain(result.Values, value => value.Field.Name == "linesTotal");
    }

    /// <summary>An Order with quantity, price and name, the given computed fields, and lines.</summary>
    private static EntityModel Order(params FieldModel[] computed) =>
        Entity(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            "Order",
            "entities/order.json",
            [_quantity, _price, _name, .. computed, _lines]);

    private static EntityModel? ChildOf(FieldModel field) => field.Target?.Id == _line.Id ? _line : null;

    private static List<(string Name, object? Value)> Totals(RecordComputeResult result) =>
        [.. result.Values.Where(value => value.Field.IsComputed).Select(value => (value.Field.Name, value.Value))];

    /// <summary>A line row as <see cref="RecordQueries"/> reads it.</summary>
    private static JsonObject StoredLine(long qty, long doubled) =>
        new() { ["qty"] = JsonValue.Create(qty), ["doubled"] = JsonValue.Create(doubled) };

    private static List<RecordValue> Values(EntityModel entity, params (string Name, object? Value)[] values) =>
        [.. values.Select(value => new RecordValue(entity.Fields.Single(field => field.Name == value.Name), value.Value))];

    /// <summary>A line row as the parser gives it: every column, the computed one null.</summary>
    private static List<RecordValue> Row(long? qty) =>
        [new RecordValue(_line.Fields[0], qty), new RecordValue(_line.Fields[1], null)];

    private static Record Stored(Dictionary<string, JsonNode?> values) =>
        new(Guid.Parse("6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f8"), 1, values, new Dictionary<string, string>());
}
