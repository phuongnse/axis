using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Data.Forms;
using Axis.Data.Records;
using Axis.Expressions.Typing;
using Record = Axis.Data.Records.Record;

namespace Axis.Data.Tests;

public sealed class FormStateEvaluatorTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    private static readonly EntityModel _plainItem = Models.Entity(
        Guid.Parse("8a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c01"),
        "Item",
        "entities/item.json",
        Models.Field("name", FieldType.Text, required: true, maxLength: 10),
        Models.Field("quantity", FieldType.Integer),
        Models.Field("price", FieldType.Decimal, precision: 10, scale: 2),
        Models.Field("note", FieldType.Text));

    private static readonly EntityModel _item = WithComputedTotal(WithValidations(_plainItem));

    private static readonly ApplicationModel _application = Models.Application(_item);

    private static readonly FormModel _form = Form(
        new FormSectionModel(
            new TextReference("main"),
            [new FormFieldModel("name", false), new FormFieldModel("quantity", false)],
            Name: "main"),
        new FormSectionModel(
            new TextReference("pricing"),
            [
                new FormFieldModel("price", false, RequiredWhen: Condition("quantity > 10")),
                new FormFieldModel("total", false),
                new FormFieldModel("note", false, VisibleWhen: Condition("quantity > 1000 / 0"), ReadOnlyWhen: Condition("quantity > 5")),
            ],
            Name: "pricing",
            VisibleWhen: Condition("quantity > 0")));

    [Fact]
    public void Hidden_section_hides_its_fields_and_requires_none()
    {
        var state = Evaluate(Draft(("name", "Desk"), ("quantity", 0L)), touched: ["price"]);

        Assert.False(Assert.Single(state.Sections, section => section.Name == "pricing").Visible);
        Assert.True(state.Fields["name"].Visible);
        Assert.All(["price", "total", "note"], name => Assert.Equal(new FormFieldState(false, false, state.Fields[name].ReadOnly), state.Fields[name]));
        Assert.Empty(state.Errors);
    }

    [Fact]
    public void Required_when_gives_a_message_only_for_a_touched_field()
    {
        var draft = Draft(("name", "Desk"), ("quantity", 20L));

        var touched = Evaluate(draft, touched: ["price"]);
        var untouched = Evaluate(draft, touched: []);

        Assert.Equal(new FormFieldState(true, true, false), touched.Fields["price"]);
        Assert.Equal(["Required."], touched.Errors["/values/price"]);
        Assert.Equal(new FormFieldState(true, true, false), untouched.Fields["price"]);
        Assert.Empty(untouched.Errors);
    }

    [Fact]
    public void Entity_required_field_is_required_and_a_null_for_it_is_reported_once()
    {
        var state = Evaluate(Draft(("name", null), ("quantity", 1L)), touched: ["name"]);

        Assert.True(state.Fields["name"].Required);
        Assert.Equal(["Required."], Assert.Single(state.Errors).Value);
        Assert.Equal("/values/name", Assert.Single(state.Errors).Key);
    }

    [Fact]
    public void Condition_that_is_null_or_fails_counts_as_false()
    {
        // quantity is null, so "quantity > 0" is null and the section is hidden; the note's condition divides by zero.
        var unset = Evaluate(Draft(("name", "Desk")), touched: []);
        var failing = Evaluate(Draft(("name", "Desk"), ("quantity", 3L)), touched: []);

        Assert.False(Assert.Single(unset.Sections, section => section.Name == "pricing").Visible);
        Assert.True(Assert.Single(failing.Sections, section => section.Name == "pricing").Visible);
        Assert.False(failing.Fields["note"].Visible);
        Assert.False(failing.Fields["note"].ReadOnly);
    }

    [Fact]
    public void Read_only_covers_form_rules_and_computed_fields()
    {
        var state = Evaluate(Draft(("name", "Desk"), ("quantity", 7L)), touched: []);

        Assert.True(state.Fields["total"].ReadOnly);
        Assert.True(state.Fields["note"].ReadOnly);
        Assert.False(state.Fields["price"].ReadOnly);
    }

    [Fact]
    public void Validation_message_is_answered_only_for_a_touched_field()
    {
        var draft = Draft(("name", "Desk"), ("quantity", -1L));

        var touched = Evaluate(draft, touched: ["quantity"]);
        var untouched = Evaluate(draft, touched: ["name"]);

        Assert.Equal(["item.quantityPositive"], touched.Errors["/values/quantity"]);
        Assert.Empty(untouched.Errors);
    }

    [Fact]
    public void Computed_field_is_calculated_on_the_draft_before_the_conditions_run()
    {
        var form = Form(new FormSectionModel(
            new TextReference("main"),
            [new FormFieldModel("quantity", false), new FormFieldModel("price", false), new FormFieldModel("note", false, VisibleWhen: Condition("total > 100"))]));

        var small = Evaluate(Draft(("name", "A"), ("quantity", 2L), ("price", "10")), touched: [], form: form);
        var large = Evaluate(Draft(("name", "A"), ("quantity", 20L), ("price", "10")), touched: [], form: form);

        Assert.False(small.Fields["note"].Visible);
        Assert.True(large.Fields["note"].Visible);
    }

    [Fact]
    public void Update_overlays_the_draft_on_the_stored_record()
    {
        var stored = Stored(new Dictionary<string, JsonNode?>
        {
            ["name"] = JsonValue.Create("Desk"),
            ["quantity"] = JsonValue.Create(20L),
            ["price"] = null,
            ["total"] = null,
        });

        var kept = Evaluate(Draft(), touched: ["price"], stored: stored);
        var changed = Evaluate(Draft(("quantity", 2L)), touched: ["price"], stored: stored);

        Assert.True(kept.Fields["price"].Required);
        Assert.Equal(["Required."], kept.Errors["/values/price"]);
        Assert.False(changed.Fields["price"].Required);
        Assert.Empty(changed.Errors);
    }

    [Fact]
    public void Cleared_required_field_of_a_stored_record_is_null_in_the_draft()
    {
        var stored = Stored(new Dictionary<string, JsonNode?> { ["name"] = JsonValue.Create("Desk"), ["quantity"] = JsonValue.Create(1L) });

        var state = Evaluate(Draft(("name", null)), touched: ["name"], stored: stored);

        Assert.Equal(["Required."], state.Errors["/values/name"]);
    }

    [Fact]
    public void Readable_trimming_leaves_out_unreadable_fields_empty_sections_and_their_messages()
    {
        var draft = Draft(("name", "Desk"), ("quantity", -1L));

        var state = Evaluate(draft, touched: ["name", "quantity"], readable: new HashSet<string> { "name" });

        Assert.Equal(["name"], state.Fields.Keys);
        Assert.Equal(["main"], state.Sections.Select(section => section.Name));
        Assert.Empty(state.Errors);
    }

    [Fact]
    public void Parse_draft_keeps_the_valid_values_next_to_the_errors()
    {
        using var values = JsonDocument.Parse("""{ "name": "Desk", "quantity": "many", "price": 1.234, "total": 5, "bogus": 1 }""");

        var result = RecordInputParser.ParseDraft(values.RootElement, _item, _application);

        Assert.NotNull(result);
        Assert.Equal(["name"], result.Input.Values.Select(value => value.Field.Name));
        Assert.Equal(["/values/bogus", "/values/price", "/values/quantity", "/values/total"], result.Errors.Keys);
    }

    [Fact]
    public void Parse_draft_does_not_report_a_required_field_it_leaves_out()
    {
        using var values = JsonDocument.Parse("""{ "quantity": 2, "name": null }""");

        var result = RecordInputParser.ParseDraft(values.RootElement, _item, _application);

        Assert.NotNull(result);
        Assert.Empty(result.Errors);
        Assert.Contains(result.Input.Values, value => value.Field.Name == "name" && value.Value is null);
    }

    [Fact]
    public void Body_with_valid_shape_gives_the_draft_the_touched_names_and_the_record_id()
    {
        var id = Guid.Parse("0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b");

        var result = Body($$"""{ "recordId": "{{id}}", "values": { "quantity": "x" }, "touched": ["quantity", "price"] }""");

        Assert.Null(result.Malformed);
        Assert.Equal(id, result.RecordId);
        Assert.Equal(["/values/quantity"], result.Draft!.Errors.Keys);
        Assert.Equal(new HashSet<string> { "quantity", "price" }, result.Touched);
    }

    [Theory]
    [InlineData("""{ "recordId": 5, "values": {}, "touched": [] }""", "/recordId")]
    [InlineData("""{ "recordId": "0192f4a73b1c7e2d9a4f1c2d3e4f5a6b", "values": {}, "touched": [] }""", "/recordId")]
    [InlineData("""{ "values": {}, "touched": "name" }""", "/touched")]
    [InlineData("""{ "values": {}, "touched": ["name", 1] }""", "/touched/1")]
    [InlineData("""{ "values": [], "touched": [] }""", "/values")]
    [InlineData("""{ "touched": [] }""", "/values")]
    [InlineData("""{ "values": {} }""", "/touched")]
    [InlineData("""{ "values": {}, "touched": [], "extra": 1 }""", "/extra")]
    [InlineData("""{ "values": {}, "values": {}, "touched": [] }""", "/values")]
    public void Body_with_a_wrong_shape_is_malformed_at_its_key(string json, string key)
    {
        var result = Body(json);

        Assert.Null(result.Draft);
        Assert.Contains(key, result.Malformed!.Keys);
    }

    [Fact]
    public void Left_out_field_of_a_new_record_gets_its_default_and_conditions_see_it()
    {
        var form = Form(
            new FormSectionModel(
                new TextReference("main"),
                [new FormFieldModel("name", false), new FormFieldModel("quantity", false), new FormFieldModel("price", false, RequiredWhen: Condition("quantity > 2"))],
                Name: "main"));

        var state = EvaluateDefaulted(Draft(("name", "Desk")), touched: ["price"], form: form);
        var withoutDefault = Evaluate(Draft(("name", "Desk")), touched: ["price"], form: form);

        Assert.Equal(3L, state.Defaults["quantity"]!.GetValue<long>());
        Assert.Equal(["quantity"], state.Defaults.Keys);
        Assert.True(state.Fields["price"].Required);
        Assert.Equal(["Required."], state.Errors["/values/price"]);
        Assert.False(withoutDefault.Fields["price"].Required);
    }

    [Fact]
    public void Field_sent_with_a_value_that_does_not_parse_gets_no_default()
    {
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal) { ["/values/quantity"] = ["Must be a whole number."] };
        var draft = new RecordDraftResult(new RecordInput { Values = [new RecordValue(_item.Fields[0], "Desk")] }, errors);

        var state = EvaluateDefaulted(draft, touched: ["quantity"]);

        Assert.Empty(state.Defaults);
        Assert.Equal(["Must be a whole number."], state.Errors["/values/quantity"]);
    }

    [Fact]
    public void Stored_record_has_no_defaults()
    {
        var stored = Stored(new Dictionary<string, JsonNode?> { ["name"] = "Desk" });

        var state = EvaluateDefaulted(Draft(), touched: [], stored);

        Assert.Empty(state.Defaults);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("not json")]
    public void Body_that_is_not_an_object_is_malformed_at_the_root(string json)
    {
        Assert.Equal([""], Body(json).Malformed!.Keys);
    }

    [Fact]
    public void Body_rejects_a_record_id_when_the_form_names_the_record_elsewhere()
    {
        var result = FormStateBody.Parse(Encoding.UTF8.GetBytes("""{ "recordId": null, "values": {}, "touched": [] }"""), _item, _application, allowRecordId: false);

        Assert.Contains("/recordId", result.Malformed!.Keys);
    }

    private static FormStateBodyResult Body(string json) =>
        FormStateBody.Parse(Encoding.UTF8.GetBytes(json), _item, _application, allowRecordId: true);

    private static FormState Evaluate(
        RecordDraftResult draft,
        string[] touched,
        Record? stored = null,
        IReadOnlySet<string>? readable = null,
        FormModel? form = null) =>
        FormStateEvaluator.Evaluate(_application, form ?? _form, _item, stored, draft, new HashSet<string>(touched), readable, "anna", _now);

    private static readonly EntityModel _defaulted = _item with
    {
        Fields = [.. _item.Fields.Select(field => field.Name == "quantity" ? field with { Default = ExpressionModel.Compile("3", ExpressionScopes.ForEntity([]), ExpressionType.Integer) } : field)],
    };

    private static FormState EvaluateDefaulted(RecordDraftResult draft, string[] touched, Record? stored = null, FormModel? form = null) =>
        FormStateEvaluator.Evaluate(Models.Application(_defaulted), form ?? _form, _defaulted, stored, draft, new HashSet<string>(touched), null, "anna", _now);

    private static RecordDraftResult Draft(params (string Name, object? Value)[] values) =>
        new(
            new RecordInput { Values = [.. values.Select(value => new RecordValue(_item.Fields.Single(field => field.Name == value.Name), value.Value))] },
            new SortedDictionary<string, string[]>(StringComparer.Ordinal));

    private static ExpressionModel Condition(string expression) =>
        ExpressionModel.Compile(expression, ExpressionScopes.ForEntity(_item.Fields), ExpressionType.Boolean);

    private static FormModel Form(params FormSectionModel[] sections) =>
        new()
        {
            Id = Guid.Parse("8a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c02"),
            Name = "ItemForm",
            File = "forms/item-form.json",
            Entity = new EntityReference(_item.Id, _item.Name),
            Sections = sections,
        };

    private static EntityModel WithValidations(EntityModel entity) =>
        entity with
        {
            Validations =
            [
                ValidationModel.Compile(
                    "quantity is null or quantity > 0",
                    ExpressionScopes.ForEntity(entity.Fields),
                    new TextReference("item.quantityPositive"),
                    "quantity"),
            ],
        };

    private static EntityModel WithComputedTotal(EntityModel entity)
    {
        var total = Models.Computed(
            Models.Field("total", FieldType.Decimal, precision: 12, scale: 2),
            "quantity * price",
            [.. entity.Fields]);
        return entity with { Fields = [.. entity.Fields, total] };
    }

    private static Record Stored(Dictionary<string, JsonNode?> values) =>
        new(Guid.Parse("6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f8"), 1, values, new Dictionary<string, JsonNode>());
}
