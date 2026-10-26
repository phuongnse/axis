using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Data.Records;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class RecordDefaultsTests
{
    private static readonly DateTimeOffset _now = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    private static readonly EntityModel _entity = Entity(
        Guid.Parse("5a1d0c2e-7b3f-4e48-9c61-2b3c4d5e6f01"),
        "Reminder",
        "entities/reminder.json",
        Field("title", FieldType.Text, required: true),
        WithDefault(Field("state", FieldType.Enum, required: true, values: ["open", "closed"]), "'open'"),
        WithDefault(Field("priority", FieldType.Integer), "3"),
        WithDefault(Field("remindAt", FieldType.DateTime), "now()"),
        WithDefault(Field("note", FieldType.Text, maxLength: 3), "'toolong'"),
        WithDefault(Field("ratio", FieldType.Decimal, precision: 10, scale: 2), "1 / 0"),
        WithDefault(Field("owner", FieldType.Text, required: true), "null"));

    [Fact]
    public void A_left_out_field_gets_its_default_in_declaration_order()
    {
        var entity = Without("note", "ratio", "owner");

        var result = RecordDefaults.Apply(entity, [new RecordValue(entity.Fields[0], "Hi")], _now);

        Assert.Null(result.Errors);
        Assert.Equal(
            [("title", (object?)"Hi"), ("state", "open"), ("priority", 3L), ("remindAt", _now)],
            result.Values.Select(value => (value.Field.Name, value.Value)));
    }

    [Fact]
    public void A_field_the_body_sends_keeps_its_value_also_when_it_is_null()
    {
        var entity = Without("note", "ratio", "owner");
        var values = new[]
        {
            new RecordValue(entity.Fields[2], null),
            new RecordValue(entity.Fields[0], "Hi"),
            new RecordValue(entity.Fields[1], "closed"),
        };

        var result = RecordDefaults.Apply(entity, values, _now);

        Assert.Null(result.Errors);
        Assert.Equal(
            [("title", (object?)"Hi"), ("state", "closed"), ("priority", null), ("remindAt", _now)],
            result.Values.Select(value => (value.Field.Name, value.Value)));
    }

    [Fact]
    public void An_entity_with_no_defaults_to_fill_gets_its_values_unchanged()
    {
        var entity = Entity(Guid.Parse("5a1d0c2e-7b3f-4e48-9c61-2b3c4d5e6f02"), "Plain", "entities/plain.json", Field("title", FieldType.Text));
        IReadOnlyList<RecordValue> values = [];

        var result = RecordDefaults.Apply(entity, values, _now);

        Assert.Same(values, result.Values);
        Assert.Null(result.Errors);
    }

    [Fact]
    public void A_default_that_fails_at_run_time_is_could_not_be_computed_at_the_fields_pointer()
    {
        var entity = Without("note", "owner");

        var result = RecordDefaults.Apply(entity, [new RecordValue(entity.Fields[0], "Hi")], _now);

        Assert.Equal(["/values/ratio"], result.Errors!.Keys);
        Assert.Equal(["Could not be computed."], result.Errors["/values/ratio"]);
    }

    [Fact]
    public void A_default_that_does_not_fit_its_field_is_the_fields_message()
    {
        var entity = Without("ratio", "owner");

        var result = RecordDefaults.Apply(entity, [new RecordValue(entity.Fields[0], "Hi")], _now);

        Assert.Equal(["/values/note"], result.Errors!.Keys);
        Assert.Equal(["Must be at most 3 characters."], result.Errors["/values/note"]);
    }

    [Fact]
    public void A_null_default_for_a_required_field_is_required_at_the_fields_pointer()
    {
        var entity = Without("note", "ratio");

        var result = RecordDefaults.Apply(entity, [new RecordValue(entity.Fields[0], "Hi")], _now);

        Assert.Equal(["/values/owner"], result.Errors!.Keys);
        Assert.Equal(["Required."], result.Errors["/values/owner"]);
    }

    [Fact]
    public void A_default_that_fails_keeps_the_defaults_that_worked()
    {
        var entity = Without("note", "owner");

        var result = RecordDefaults.Apply(entity, [new RecordValue(entity.Fields[0], "Hi")], _now);

        Assert.Equal(["/values/ratio"], result.Errors!.Keys);
        Assert.Equal(
            [("title", (object?)"Hi"), ("state", "open"), ("priority", 3L), ("remindAt", _now)],
            result.Values.Select(value => (value.Field.Name, value.Value)));
    }

    /// <summary>The entity without the named fields, so one test reaches one default.</summary>
    private static EntityModel Without(params string[] names) =>
        _entity with { Fields = [.. _entity.Fields.Where(field => !names.Contains(field.Name))] };

    /// <summary>Gives <paramref name="field"/> a default, compiled as the compiler does for a scope with no fields.</summary>
    private static FieldModel WithDefault(FieldModel field, string expression) =>
        field with { Default = ExpressionModel.Compile(expression, ExpressionScopes.ForEntity([]), ExpressionScopes.TypeOf(field)!) };
}
