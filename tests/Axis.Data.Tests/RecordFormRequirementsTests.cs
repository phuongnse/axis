using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Data.Records;
using Axis.Expressions.Typing;
using Record = Axis.Data.Records.Record;

namespace Axis.Data.Tests;

public sealed class RecordFormRequirementsTests
{
    private static readonly EntityModel _line = Models.Entity(
        Guid.Parse("22222222-2222-4222-8222-222222222222"), "Line", "entities/line.json", Models.Field("qty", FieldType.Integer));

    private static readonly EntityModel _order = Models.Entity(
        Guid.Parse("11111111-1111-4111-8111-111111111111"),
        "Order",
        "entities/order.json",
        Models.Field("quantity", FieldType.Integer),
        Models.Field("price", FieldType.Decimal),
        Models.Field("status", FieldType.Enum, values: ["open", "closed"]),
        Models.Field("neededBy", FieldType.Date),
        Models.Field("lines", FieldType.ChildCollection, target: _line),
        Models.Field("number", FieldType.Text) with { Sequence = new SequenceModel(Guid.NewGuid(), "orders", "ORD-{0}") });

    private static readonly ApplicationModel _application = Models.Application(_order, _line);

    [Fact]
    public void Visible_field_whose_condition_holds_and_value_is_null_is_required()
    {
        var form = Form(Section(Field("price", requiredWhen: "status == 'closed'")));

        var failures = Check(form, Values(("status", "closed")));

        Assert.NotNull(failures);
        var failure = Assert.Single(failures);
        Assert.Equal("/values/price", failure.Key);
        Assert.Equal(["Required."], failure.Value);
    }

    [Fact]
    public void Field_with_a_value_is_not_required()
    {
        var form = Form(Section(Field("price", requiredWhen: "status == 'closed'")));

        Assert.Null(Check(form, Values(("status", "closed"), ("price", "1.5"))));
    }

    [Fact]
    public void Field_whose_condition_is_false_is_not_required()
    {
        var form = Form(Section(Field("price", requiredWhen: "status == 'closed'")));

        Assert.Null(Check(form, Values(("status", "open"))));
    }

    [Fact]
    public void Field_whose_condition_is_null_or_fails_is_not_required()
    {
        var form = Form(Section(Field("price", requiredWhen: "quantity > 0"), Field("neededBy", requiredWhen: "10 / quantity > 1")));

        // quantity is null, so the first condition is null. 10 / 0 stops the second with a run-time error.
        Assert.Null(Check(form, []));
        Assert.Null(Check(form, Values(("quantity", 0L))));
    }

    [Fact]
    public void Field_hidden_by_its_own_condition_is_not_required()
    {
        var form = Form(Section(Field("price", visibleWhen: "quantity > 0", requiredWhen: "status == 'closed'")));

        Assert.Null(Check(form, Values(("status", "closed"), ("quantity", 0L))));
        Assert.Null(Check(form, Values(("status", "closed"))));
        Assert.NotNull(Check(form, Values(("status", "closed"), ("quantity", 1L))));
    }

    [Fact]
    public void Field_hidden_by_its_section_is_not_required()
    {
        var form = Form(Section(visibleWhen: "quantity > 0", Field("price", requiredWhen: "status == 'closed'")));

        Assert.Null(Check(form, Values(("status", "closed"), ("quantity", 0L))));
        Assert.Null(Check(form, Values(("status", "closed"))));
        Assert.NotNull(Check(form, Values(("status", "closed"), ("quantity", 1L))));
    }

    [Fact]
    public void Update_checks_the_stored_record_merged_with_the_changes()
    {
        var form = Form(Section(Field("price", requiredWhen: "status == 'closed'")));
        var stored = Stored(new Dictionary<string, JsonNode?> { ["status"] = JsonValue.Create("closed"), ["price"] = JsonNode.Parse("2.5") });

        // The stored price satisfies the form until the body clears it.
        Assert.Null(RecordFormRequirements.Check(_application, _order, form, stored, [], [], null));
        var cleared = RecordFormRequirements.Check(_application, _order, form, stored, Values(("price", null)), [], null);
        Assert.NotNull(cleared);
        Assert.Equal("/values/price", Assert.Single(cleared).Key);

        // A stored null becomes required when the body closes the record.
        var open = Stored(new Dictionary<string, JsonNode?> { ["status"] = JsonValue.Create("open") });
        Assert.Null(RecordFormRequirements.Check(_application, _order, form, open, [], [], null));
        Assert.NotNull(RecordFormRequirements.Check(_application, _order, form, open, Values(("status", "closed")), [], null));
    }

    [Fact]
    public void Condition_can_read_the_rows_of_a_collection()
    {
        var form = Form(Section(Field("price", requiredWhen: "count(lines) > 0")));
        var rows = new RecordRows(
            _order.Fields.Single(field => field.Name == "lines"),
            _line,
            [[new RecordValue(_line.Fields.Single(), 1L)]]);

        Assert.Null(RecordFormRequirements.Check(_application, _order, form, null, [], [], null));
        var failures = RecordFormRequirements.Check(_application, _order, form, null, [], [rows], null);
        Assert.NotNull(failures);
        Assert.Equal("/values/price", Assert.Single(failures).Key);
    }

    [Fact]
    public void Child_collection_and_numbered_field_are_never_required()
    {
        var form = Form(Section(Field("lines", requiredWhen: "true"), Field("number", requiredWhen: "true")));

        Assert.Null(Check(form, []));
    }

    [Fact]
    public void Field_listed_twice_is_required_when_any_entry_says_so()
    {
        var form = Form(Section(Field("price", requiredWhen: "false")), Section(Field("price", requiredWhen: "true")));

        Assert.Equal("/values/price", Assert.Single(Check(form, [])!).Key);
    }

    [Fact]
    public void Form_without_requirements_is_not_checked()
    {
        var form = Form(Section(Field("price")));

        Assert.False(form.HasRequiredWhen);
        Assert.Null(Check(form, []));
    }

    [Fact]
    public void Value_that_cannot_be_held_exactly_is_not_evaluable_and_no_condition_runs()
    {
        var form = Form(Section(Field("neededBy", requiredWhen: "true")));

        var failures = Check(form, Values(("price", "1.12345678901234567890123456789")));

        Assert.NotNull(failures);
        var failure = Assert.Single(failures);
        Assert.Equal("/values/price", failure.Key);
        Assert.Equal(["Cannot be evaluated exactly."], failure.Value);
    }

    [Fact]
    public void Merge_keeps_required_over_a_validation_message_on_the_same_key()
    {
        var form = new SortedDictionary<string, string[]> { ["/values/a"] = ["Required."] };
        var validation = new SortedDictionary<string, string[]> { ["/values/a"] = ["order.a"], ["/values/b"] = ["order.b"] };

        var merged = RecordFormRequirements.Merge(form, validation);

        Assert.NotNull(merged);
        Assert.Equal(["/values/a", "/values/b"], merged.Keys);
        Assert.Equal(["Required."], merged["/values/a"]);
        Assert.Equal(["order.b"], merged["/values/b"]);
        Assert.Null(RecordFormRequirements.Merge(null, null));
        Assert.Same(validation, RecordFormRequirements.Merge(null, validation));
    }

    private static SortedDictionary<string, string[]>? Check(FormModel form, List<RecordValue> values) =>
        RecordFormRequirements.Check(_application, _order, form, null, values, [], null);

    private static FormModel Form(params FormSectionModel[] sections) =>
        new()
        {
            Id = Guid.Parse("33333333-3333-4333-8333-333333333333"),
            Name = "Requirements",
            File = "forms/requirements.json",
            Entity = new EntityReference(_order.Id, _order.Name),
            Sections = sections,
        };

    private static FormSectionModel Section(params FormFieldModel[] fields) => Section(null, fields);

    private static FormSectionModel Section(string? visibleWhen, params FormFieldModel[] fields) =>
        new(new TextReference("section"), fields, visibleWhen is null ? null : "section", Condition(visibleWhen));

    private static FormFieldModel Field(string name, string? visibleWhen = null, string? requiredWhen = null) =>
        new(name, false, Condition(visibleWhen), Condition(requiredWhen));

    private static ExpressionModel? Condition(string? expression) =>
        expression is null
            ? null
            : ExpressionModel.Compile(
                expression,
                ExpressionScopes.ForEntity(_order.Fields, childOf: field => field.Target?.Id == _line.Id ? _line : null),
                ExpressionType.Boolean);

    private static List<RecordValue> Values(params (string Name, object? Value)[] values) =>
        [.. values.Select(value => new RecordValue(_order.Fields.Single(field => field.Name == value.Name), value.Value))];

    private static Record Stored(Dictionary<string, JsonNode?> values) =>
        new(Guid.Parse("6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f8"), 1, values, new Dictionary<string, string>());
}
