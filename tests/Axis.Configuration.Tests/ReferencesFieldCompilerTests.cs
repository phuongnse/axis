using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class ReferencesFieldCompilerTests
{
    private const string RequestId = "11111111-1111-4111-8111-111111111111";

    private const string SoftwareId = "22222222-2222-4222-8222-222222222222";

    private const string LineId = "33333333-3333-4333-8333-333333333333";

    private const string ProcessId = "44444444-4444-4444-8444-444444444444";

    private const string Software = """{ "name": "software", "type": "references", "target": "Software" }""";

    public static TheoryData<string, string, string, string> RefusedFields => new()
    {
        { """{ "name": "software", "type": "references", "target": "Software", "unique": true }""", DiagnosticCodes.InvalidConstraint, "/fields/0/unique", "'unique' does not apply to references fields." },
        { """{ "name": "software", "type": "references", "target": "Software", "expression": "1" }""", DiagnosticCodes.InvalidConstraint, "/fields/0/expression", "'expression' applies only to" },
        { """{ "name": "software", "type": "references", "target": "Software", "default": "1" }""", DiagnosticCodes.InvalidConstraint, "/fields/0/default", "'default' applies only to" },
        { """{ "name": "software", "type": "references" }""", DiagnosticCodes.MissingTypeProperty, "/fields/0", "A references field must name its target entity in 'target'." },
        { """{ "name": "software", "type": "references", "target": "Line" }""", DiagnosticCodes.ReferenceToChildEntity, "/fields/0/target", "A child entity cannot be the target of a references field." },
        { """{ "name": "software", "type": "references", "target": "Plain" }""", DiagnosticCodes.ReferenceTargetWithoutDisplayField, "/fields/0/target", "'displayField'" },
        { """{ "name": "software", "type": "references", "target": "Missing" }""", DiagnosticCodes.UnknownReferenceTarget, "/fields/0/target", "'Missing' was not found" },
        { """{ "name": "software", "type": "text", "target": "Software" }""", DiagnosticCodes.InvalidConstraint, "/fields/0/target", "reference, references and child-collection" },
    };

    [Fact]
    public void Field_with_required_and_sensitive_compiles_without_a_column()
    {
        using var folder = Folder("""{ "name": "software", "type": "references", "target": "Software", "required": true, "sensitive": true }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetEntity("Request", out var request));
        Assert.True(request.TryGetField("software", out var field));
        Assert.Equal(FieldType.References, field.Type);
        Assert.Equal(new EntityReference(Guid.Parse(SoftwareId), "Software"), field.Target);
        Assert.Equal("name", field.TargetDisplayField);
        Assert.True(field.Required);
        Assert.True(field.Sensitive);
        Assert.False(field.HasColumn);
    }

    [Theory]
    [MemberData(nameof(RefusedFields))]
    public void Refused_field_is_reported_at_its_path_and_gives_no_model(string field, string code, string path, string messagePart)
    {
        using var folder = Folder(field);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "entities/request.json", path), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains(messagePart, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Child_entity_cannot_have_a_references_field()
    {
        using var folder = Folder(Software)
            .With("entities/line.json", Entity("Line", LineId, """{ "name": "software", "type": "references", "target": "Software" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ChildEntityWithChildCollection, "entities/line.json", "/fields/0/type"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("a references field", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_source_cannot_project_a_references_field()
    {
        using var folder = Folder(Software)
            .With("data-sources/requests.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "dataSource", "name": "Requests", "formatVersion": 1,
                  "entity": "Request", "fields": [{ "name": "software", "path": "software" }] }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidDataSourceFieldPath, "data-sources/requests.json", "/fields/0/path"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("references field", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_source_filter_refuses_an_aggregate_over_a_references_field()
    {
        using var folder = Folder(Software)
            .With("data-sources/requests.json", """
                { "id": "55555555-5555-4555-8555-555555555555", "kind": "dataSource", "name": "Requests", "formatVersion": 1,
                  "entity": "Request", "fields": [{ "name": "title", "path": "title" }],
                  "filter": "any(software, name == 'Office')" }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((ExpressionDiagnosticCodes.OutsideSqlSubset, "/filter"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void Validation_can_aggregate_over_the_field()
    {
        using var folder = Folder([Software], Validation("any(software, name == 'Office')"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
    }

    [Fact]
    public void Validation_refuses_the_field_outside_an_aggregate()
    {
        using var folder = Folder([Software], Validation("software == null"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((ExpressionDiagnosticCodes.TypeMismatch, "/validations/0/expression"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("list<Software>", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_item_expression_sees_the_target_fields_only()
    {
        using var folder = Folder([Software], Validation("any(software, title == 'Office')"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((ExpressionDiagnosticCodes.UnknownName, "/validations/0/expression"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("'title'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_condition_can_aggregate_over_the_field()
    {
        using var folder = Folder(Software)
            .With("processes/review.json", Process("any(software, name == 'Office')"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.True(result.Model.TryGetProcess("review", out var process));
        Assert.NotNull(process.StartCondition);
    }

    [Fact]
    public void Process_cannot_set_the_field()
    {
        using var folder = Folder(Software)
            .With("processes/review.json", $$"""
                { "id": "{{ProcessId}}", "kind": "process", "name": "Review", "formatVersion": 1, "entity": "Request", "start": "set",
                  "steps": [
                    { "name": "set", "type": "operation", "operation": "updateRecord", "set": { "software": "null" }, "next": "done" },
                    { "name": "done", "type": "end" }
                  ] }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.ReadOnlySetField, "/steps/0/set/software"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("is a references field", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Computed_field_cannot_read_the_field()
    {
        using var folder = Folder(
            Software,
            """{ "name": "softwareCount", "type": "integer", "expression": "count(software)" }""");

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((ExpressionDiagnosticCodes.UnknownName, "/fields/1/expression"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("'software'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Endpoint_cannot_map_the_field()
    {
        using var folder = Folder(Software)
            .With("processes/review.json", Process(null))
            .With("endpoints/submit.json", $$"""
                { "id": "66666666-6666-4666-8666-666666666666", "kind": "endpoint", "name": "Submit", "formatVersion": 1,
                  "entity": "Request", "process": "Review", "input": [{ "name": "name", "type": "text" }],
                  "values": { "software": "name" } }
                """);

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.ReadOnlyEndpointValueField, "/values/software"), (diagnostic.Code, diagnostic.Path));
        Assert.Contains("references field", diagnostic.Message, StringComparison.Ordinal);
    }

    private const string Texts = """
        { "id": "77777777-7777-4777-8777-777777777777", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "request.rule": "Not allowed." } }
        """;

    private static string Validation(string expression) =>
        $$"""[{ "expression": "{{expression}}", "message": { "textKey": "request.rule" }, "field": "title" }]""";

    private static string Process(string? startCondition) =>
        $$"""
        { "id": "{{ProcessId}}", "kind": "process", "name": "Review", "formatVersion": 1, "entity": "Request",
          {{(startCondition is null ? "" : $$""" "startCondition": { "expression": "{{startCondition}}", "message": { "textKey": "request.rule" } }, """)}}
          "start": "done", "steps": [{ "name": "done", "type": "end" }] }
        """;

    private static string Entity(string name, string id, string fields, string? displayField = null, string? validations = null) =>
        $$"""
        { "id": "{{id}}", "kind": "entity", "name": "{{name}}", "formatVersion": 1,
          {{(displayField is null ? "" : $"\"displayField\": \"{displayField}\", ")}}
          "fields": [{{fields}}]{{(validations is null ? "" : $", \"validations\": {validations}")}} }
        """;

    /// <summary>
    /// A Request entity with <paramref name="fields"/>, then a text field <c>title</c>. A Software entity
    /// with a display field, a Plain entity without one, and a Line entity that a Holder entity owns as a
    /// child collection.
    /// </summary>
    private static TemporaryFolder Folder(params string[] fields) => Folder(fields, validations: null);

    private static TemporaryFolder Folder(string[] fields, string? validations) =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("texts/en.json", Texts)
            .With("entities/request.json", Entity("Request", RequestId, string.Join(", ", [.. fields, """{ "name": "title", "type": "text" }"""]), validations: validations))
            .With("entities/software.json", Entity("Software", SoftwareId, """{ "name": "name", "type": "text", "required": true }""", displayField: "name"))
            .With("entities/plain.json", Entity("Plain", "88888888-8888-4888-8888-888888888888", """{ "name": "name", "type": "text" }"""))
            .With("entities/holder.json", Entity("Holder", "99999999-9999-4999-8999-999999999999", """{ "name": "lines", "type": "child-collection", "target": "Line" }"""))
            .With("entities/line.json", Entity("Line", LineId, """{ "name": "quantity", "type": "integer" }"""));
}
