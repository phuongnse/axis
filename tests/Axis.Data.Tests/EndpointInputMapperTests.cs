using System.Text;
using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Data.Records;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

public sealed class EndpointInputMapperTests
{
    private static readonly FieldModel _text = Field("text", FieldType.Text, required: true);

    private static readonly EntityModel _line = Entity(Guid.Parse("33333333-3333-4333-8333-333333333332"), "NoteLine", "entities/note-line.json", _text);

    private static readonly EntityModel _note = Entity(
        Guid.Parse("33333333-3333-4333-8333-333333333331"),
        "Note",
        "entities/note.json",
        Field("title", FieldType.Text, required: true),
        Field("code", FieldType.Text, maxLength: 3),
        Field("status", FieldType.Text),
        Field("lines", FieldType.ChildCollection, target: _line),
        Field("amount", FieldType.Decimal, precision: 10, scale: 2));

    private static readonly ApplicationModel _application = Application(_note, _line);

    private static readonly EndpointInputModel[] _input =
    [
        new("name", FieldType.Text, Required: false, MaxLength: null, Precision: null, Scale: null, Values: null, Target: null),
        new("price", FieldType.Decimal, Required: false, MaxLength: null, Precision: null, Scale: null, Values: null, Target: null),
        new("rows", FieldType.ChildCollection, Required: false, MaxLength: null, Precision: null, Scale: null, Values: null, Target: new EntityReference(_line.Id, _line.Name)),
    ];

    private static readonly EndpointModel _endpoint = new()
    {
        Id = Guid.Parse("33333333-3333-4333-8333-333333333333"),
        Name = "ImportNote",
        File = "endpoints/import-note.json",
        Entity = new EntityReference(_note.Id, _note.Name),
        Process = new ProcessReference(Guid.Parse("33333333-3333-4333-8333-333333333334"), "Close"),
        Input = _input,
        Values =
        [
            Value("title", "name"),
            Value("code", "name"),
            Value("status", "'open'"),
            new EndpointValueModel("lines", null, "rows"),
            Value("amount", "price * 2"),
        ],
    };

    [Fact]
    public void Each_mapped_field_gets_its_expression_over_the_input_in_the_entitys_order()
    {
        var result = Map("""{ "input": { "price": 1.25, "name": "Abc", "rows": [{ "text": "Legs" }] } }""");

        Assert.Null(result.Errors);
        Assert.Equal(
            [("title", (object?)"Abc"), ("code", "Abc"), ("status", "open"), ("amount", "2.50")],
            result.Values.Select(value => (value.Field.Name, value.Value)));
        var rows = Assert.Single(result.Rows);
        Assert.Same(_note.Fields[3], rows.Collection);
        Assert.Equal(_line, rows.Child);
        Assert.Equal("Legs", Assert.Single(Assert.Single(rows.Rows)).Value);
    }

    [Fact]
    public void A_required_field_that_maps_to_null_is_an_error_at_its_values_path()
    {
        var result = Map("""{ "input": { "price": 1 } }""");

        Assert.Equal(["/values/title"], result.Errors!.Keys);
        Assert.Equal(["Required."], result.Errors["/values/title"]);
        Assert.Empty(result.Values);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void A_value_that_does_not_fit_its_field_is_an_error_at_its_values_path()
    {
        var result = Map("""{ "input": { "name": "Abcd", "price": 123456789 } }""");

        Assert.Equal(["/values/amount", "/values/code"], result.Errors!.Keys);
        Assert.Equal(["Must be at most 3 characters."], result.Errors["/values/code"]);
        Assert.Equal(["Must have at most 8 integer digits."], result.Errors["/values/amount"]);
    }

    [Fact]
    public void An_input_that_cannot_be_held_exactly_is_an_error_at_its_input_path()
    {
        var result = Map("""{ "input": { "name": "Abc", "price": 0.12345678901234567890123456789012 } }""");

        Assert.Equal(["/input/price"], result.Errors!.Keys);
        Assert.Equal(["Cannot be evaluated exactly."], result.Errors["/input/price"]);
    }

    [Fact]
    public void The_input_parses_under_input_and_a_row_error_names_the_input_collection()
    {
        var parsed = Parse("""{ "input": { "name": 5, "rows": [{}] }, "values": {} }""");

        Assert.Equal(["/input/name", "/input/rows/0/text", "/values"], parsed.Errors!.Keys);
        Assert.Equal(["Unknown property."], parsed.Errors["/values"]);
    }

    [Fact]
    public void A_required_field_with_a_default_may_be_left_unmapped_but_not_mapped_to_null()
    {
        var kind = Field("kind", FieldType.Text, required: true) with
        {
            Default = ExpressionModel.Compile("'x'", ExpressionScopes.ForEntity([]), ExpressionScopes.TypeOf(Field("kind", FieldType.Text))!),
        };
        var entity = Entity(
            Guid.Parse("33333333-3333-4333-8333-333333333341"), "Tagged", "entities/tagged.json", Field("title", FieldType.Text), kind);
        var application = Application(entity);
        var unmapped = _endpoint with { Values = [Value("title", "name")] };
        var mapped = _endpoint with { Values = [Value("title", "name"), new EndpointValueModel("kind", ExpressionModel.Compile("name", ExpressionScopes.ForEntity([Field("name", FieldType.Text)]), ExpressionScopes.TypeOf(kind)!), null)] };
        var input = Parse("""{ "input": { "price": 1 } }""").Input!;

        var left = EndpointInputMapper.Map(application, entity, unmapped, input);
        var sentNull = EndpointInputMapper.Map(application, entity, mapped, input);

        Assert.Null(left.Errors);
        Assert.Equal(["title"], left.Values.Select(value => value.Field.Name));
        Assert.Equal(["/values/kind"], sentNull.Errors!.Keys);
        Assert.Equal(["Required."], sentNull.Errors["/values/kind"]);
    }

    private static EndpointMapResult Map(string body)
    {
        var parsed = Parse(body);
        Assert.Null(parsed.Errors);
        return EndpointInputMapper.Map(_application, _note, _endpoint, parsed.Input!);
    }

    private static RecordInputResult Parse(string body) =>
        RecordInputParser.ParseEndpointInput(Encoding.UTF8.GetBytes(body), _endpoint, _application);

    /// <summary>A mapped field whose expression reads the input fields, as the compiler builds it.</summary>
    private static EndpointValueModel Value(string field, string expression)
    {
        Assert.True(_note.TryGetField(field, out var model));
        var inputs = _input
            .Where(input => input.Type != FieldType.ChildCollection)
            .Select(input => Field(input.Name, input.Type))
            .ToArray();
        return new EndpointValueModel(field, ExpressionModel.Compile(expression, ExpressionScopes.ForEntity(inputs), ExpressionScopes.TypeOf(model)!), null);
    }
}
