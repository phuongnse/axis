using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Data.Records;
using Record = Axis.Data.Records.Record;

namespace Axis.Data.Tests;

public sealed class RecordValidatorTests
{
    private static readonly EntityModel _supplier = Models.Entity(
        Guid.Parse("33333333-3333-4333-8333-333333333333"),
        "Supplier",
        "entities/supplier.json",
        "name",
        Models.Field("name", FieldType.Text, required: true));

    private static readonly EntityModel _line = Validated(
        Models.Entity(Guid.Parse("22222222-2222-4222-8222-222222222222"), "Line", "entities/line.json", Models.Field("qty", FieldType.Integer)),
        ("qty is null or qty > 0", "line.qtyPositive", "qty"));

    private static readonly EntityModel _plainOrder = Models.Entity(
        Guid.Parse("11111111-1111-4111-8111-111111111111"),
        "Order",
        "entities/order.json",
        Models.Field("quantity", FieldType.Integer),
        Models.Field("price", FieldType.Decimal),
        Models.Field("active", FieldType.Boolean),
        Models.Field("status", FieldType.Enum, values: ["open", "closed"]),
        Models.Field("neededBy", FieldType.Date),
        Models.Field("orderedAt", FieldType.DateTime),
        Models.Field("supplier", FieldType.Reference, target: _supplier),
        Models.Field("lines", FieldType.ChildCollection, target: _line));

    private static readonly EntityModel _order = Validated(
        _plainOrder,
        ("quantity > 0", "order.quantityPositive", "quantity"),
        ("price is null or price >= 0", "order.priceNotNegative", "price"),
        ("status != 'closed' or active != true", "order.closedInactive", "status"),
        ("quantity is null or quantity < 100", "order.quantitySmall", "quantity"));

    private static readonly EntityModel _lineOrder = Validated(
        _plainOrder,
        ("count(lines) >= 1", "order.needsLine", "lines"),
        ("sum(lines, qty) <= 10", "order.smallTotal", "lines"));

    private static readonly ApplicationModel _application = Models.Application(_order, _line, _supplier);

    [Fact]
    public void Create_that_breaks_rules_fails_on_each_rule_field_with_its_text_key()
    {
        var failures = RecordValidator.ValidateCreate(_application, _order, Values(_order, ("quantity", 0L), ("price", "-1")), []);

        Assert.NotNull(failures);
        Assert.Equal(["/values/price", "/values/quantity"], failures.Keys);
        Assert.Equal(["order.priceNotNegative"], failures["/values/price"]);
        Assert.Equal(["order.quantityPositive"], failures["/values/quantity"]);
    }

    [Fact]
    public void First_failing_rule_in_declaration_order_wins_its_key()
    {
        var failures = RecordValidator.ValidateCreate(_application, _order, Values(_order, ("quantity", 100L)), []);

        // quantity > 0 passes, so the later rule on the same field is reported.
        Assert.NotNull(failures);
        Assert.Equal(["order.quantitySmall"], Assert.Single(failures).Value);
    }

    [Fact]
    public void Field_the_create_leaves_out_is_null_and_a_null_condition_fails()
    {
        var failures = RecordValidator.ValidateCreate(_application, _order, [], []);

        Assert.NotNull(failures);
        Assert.Equal(["order.quantityPositive"], Assert.Single(failures, pair => pair.Key == "/values/quantity").Value);
        Assert.Single(failures);
    }

    [Fact]
    public void Run_time_error_fails_the_rule_at_its_field()
    {
        var entity = Validated(_plainOrder, ("1 / quantity > 0", "order.quantityPositive", "quantity"));

        var failures = RecordValidator.ValidateCreate(_application, entity, Values(entity, ("quantity", 0L)), []);

        Assert.NotNull(failures);
        Assert.Equal(["order.quantityPositive"], failures["/values/quantity"]);
    }

    [Fact]
    public void Passing_create_has_no_failures()
    {
        var values = Values(_order, ("quantity", 3L), ("price", "1250.50"), ("status", "closed"), ("active", false));

        Assert.Null(RecordValidator.ValidateCreate(_application, _order, values, []));
    }

    [Fact]
    public void Child_rows_are_validated_at_their_row_path()
    {
        var lines = _order.Fields.Single(field => field.Name == "lines");
        var rows = new RecordRows(lines, _line, [Row(("qty", 1L)), Row(("qty", 0L)), Row(("qty", null))]);

        var failures = RecordValidator.ValidateCreate(_application, _order, Values(_order, ("quantity", 1L)), [rows]);

        Assert.NotNull(failures);
        Assert.Equal(["line.qtyPositive"], Assert.Single(failures, pair => pair.Key == "/values/lines/1/qty").Value);
        Assert.Single(failures);
    }

    [Fact]
    public void Update_of_an_owner_without_validations_still_validates_child_rows()
    {
        // The owner has no validations, so the update needs no stored record.
        var lines = _plainOrder.Fields.Single(field => field.Name == "lines");
        var rows = new RecordRows(lines, _line, [Row(("qty", 2L)), Row(("qty", 0L))]);

        var failures = RecordValidator.ValidateUpdate(_application, _plainOrder, null, Values(_plainOrder, ("quantity", 0L)), [rows]);

        Assert.NotNull(failures);
        var failure = Assert.Single(failures);
        Assert.Equal("/values/lines/1/qty", failure.Key);
        Assert.Equal(["line.qtyPositive"], failure.Value);
    }

    [Fact]
    public void Update_validates_the_stored_record_merged_with_the_changes()
    {
        // Every stored type is converted, so a rule over all of them can pass.
        var entity = Validated(
            _order,
            ("price == 1.5 and neededBy == date('2026-01-02') and orderedAt == dateTime('2026-01-02T03:04:05.123456Z') and supplier is not null",
                "order.storedValues", "neededBy"));
        var stored = Stored(new Dictionary<string, JsonNode?>
        {
            ["quantity"] = JsonValue.Create(2L),
            ["price"] = JsonNode.Parse("1.50"),
            ["active"] = JsonValue.Create(false),
            ["status"] = JsonValue.Create("closed"),
            ["neededBy"] = JsonValue.Create("2026-01-02"),
            ["orderedAt"] = JsonValue.Create("2026-01-02T03:04:05.123456Z"),
            ["supplier"] = JsonValue.Create("6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7"),
            ["lines"] = new JsonArray(),
        });

        Assert.Null(RecordValidator.ValidateUpdate(_application, entity, stored, Values(entity, ("quantity", 5L)), []));

        // Only active changes, but the rule on status reads the merged record.
        var failures = RecordValidator.ValidateUpdate(_application, entity, stored, Values(entity, ("active", true)), []);

        Assert.NotNull(failures);
        Assert.Equal(["order.closedInactive"], Assert.Single(failures).Value);
        Assert.Equal("/values/status", Assert.Single(failures).Key);
    }

    [Fact]
    public void Update_of_an_entity_with_validations_needs_the_stored_record()
    {
        Assert.Throws<ArgumentException>(() => RecordValidator.ValidateUpdate(_application, _order, null, [], []));
    }

    [Theory]
    [InlineData("1.0000000000000000000000000000001")]
    [InlineData("100000000000000000000000000000000")]
    public void Decimal_that_cannot_be_held_exactly_fails_at_its_field_and_skips_the_rules(string price)
    {
        var failures = RecordValidator.ValidateCreate(_application, _order, Values(_order, ("price", price)), []);

        // quantity is null and would fail, but no rule runs on a record that cannot be evaluated.
        Assert.NotNull(failures);
        var failure = Assert.Single(failures);
        Assert.Equal("/values/price", failure.Key);
        Assert.Equal(["Cannot be evaluated exactly."], failure.Value);
    }

    [Fact]
    public void Decimal_with_trailing_zeros_beyond_the_scale_is_still_exact()
    {
        var values = Values(_order, ("quantity", 1L), ("price", "-0.000000000000000000000000000000000"));

        Assert.Null(RecordValidator.ValidateCreate(_application, _order, values, []));
    }

    [Fact]
    public void Entity_without_validations_has_no_failures()
    {
        Assert.Null(RecordValidator.ValidateCreate(_application, _supplier, [], []));
        Assert.Null(RecordValidator.ValidateUpdate(_application, _supplier, null, [], []));
    }

    [Fact]
    public void Count_of_lines_fails_a_create_without_rows_at_the_collection()
    {
        var failures = RecordValidator.ValidateCreate(_application, _lineOrder, [], []);

        Assert.NotNull(failures);
        var failure = Assert.Single(failures);
        Assert.Equal(("/values/lines", "order.needsLine"), (failure.Key, Assert.Single(failure.Value)));
        Assert.Null(RecordValidator.ValidateCreate(_application, _lineOrder, [], [Lines(1L)]));
    }

    [Fact]
    public void Aggregate_reads_the_body_rows()
    {
        Assert.Null(RecordValidator.ValidateCreate(_application, _lineOrder, [], [Lines(4L, 6L)]));

        var failures = RecordValidator.ValidateCreate(_application, _lineOrder, [], [Lines(4L, null, 7L)]);

        Assert.NotNull(failures);
        Assert.Equal(["order.smallTotal"], Assert.Single(failures).Value);
    }

    [Fact]
    public void Update_that_sends_no_rows_fails_the_count()
    {
        var stored = Stored(new Dictionary<string, JsonNode?> { ["lines"] = new JsonArray(StoredLine(1L)) });

        var failures = RecordValidator.ValidateUpdate(_application, _lineOrder, stored, [], [Lines()]);

        Assert.NotNull(failures);
        Assert.Equal(["order.needsLine"], Assert.Single(failures, pair => pair.Key == "/values/lines").Value);
    }

    [Fact]
    public void Update_that_leaves_the_rows_out_reads_the_stored_rows()
    {
        var withLine = Stored(new Dictionary<string, JsonNode?> { ["lines"] = new JsonArray(StoredLine(1L)) });
        var withoutLines = Stored(new Dictionary<string, JsonNode?> { ["lines"] = new JsonArray() });
        var tooLarge = Stored(new Dictionary<string, JsonNode?> { ["lines"] = new JsonArray(StoredLine(6L), StoredLine(5L)) });

        Assert.Null(RecordValidator.ValidateUpdate(_application, _lineOrder, withLine, Values(_lineOrder, ("quantity", 2L)), []));
        var empty = RecordValidator.ValidateUpdate(_application, _lineOrder, withoutLines, [], []);
        var large = RecordValidator.ValidateUpdate(_application, _lineOrder, tooLarge, [], []);

        Assert.NotNull(empty);
        Assert.Equal(("/values/lines", "order.needsLine"), (Assert.Single(empty).Key, Assert.Single(Assert.Single(empty).Value)));
        Assert.NotNull(large);
        Assert.Equal(("/values/lines", "order.smallTotal"), (Assert.Single(large).Key, Assert.Single(Assert.Single(large).Value)));
    }

    private static EntityModel Validated(EntityModel entity, params (string Expression, string TextKey, string Field)[] rules) =>
        entity with
        {
            Validations =
            [
                .. entity.Validations,
                .. rules.Select(rule => ValidationModel.Compile(
                    rule.Expression,
                    ExpressionScopes.ForEntity(entity.Fields, childOf: field => field.Target?.Id == _line.Id ? _line : null),
                    new TextReference(rule.TextKey),
                    rule.Field)),
            ],
        };

    private static List<RecordValue> Values(EntityModel entity, params (string Name, object? Value)[] values) =>
        [.. values.Select(value => new RecordValue(entity.Fields.Single(field => field.Name == value.Name), value.Value))];

    private static List<RecordValue> Row(params (string Name, object? Value)[] values) => Values(_line, values);

    /// <summary>The lines a body sends, one row per quantity.</summary>
    private static RecordRows Lines(params long?[] quantities) =>
        new(_plainOrder.Fields.Single(field => field.Name == "lines"), _line, [.. quantities.Select(qty => Row(("qty", qty)))]);

    /// <summary>A line row as <see cref="RecordQueries"/> reads it.</summary>
    private static JsonObject StoredLine(long qty) => new() { ["qty"] = JsonValue.Create(qty) };

    private static Record Stored(Dictionary<string, JsonNode?> values) =>
        new(Guid.Parse("6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f8"), 1, values, new Dictionary<string, string>());
}
