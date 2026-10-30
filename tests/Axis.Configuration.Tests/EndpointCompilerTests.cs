using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Expressions.Diagnostics;

namespace Axis.Configuration.Tests;

public sealed class EndpointCompilerTests
{
    private const string EndpointId = "83244ba2-b26f-4348-aa7a-0e39eb992fd3";
    private const string OtherEndpointId = "83244ba2-b26f-4348-aa7a-0e39eb992fd4";
    private const string ThirdEndpointId = "83244ba2-b26f-4348-aa7a-0e39eb992fd5";

    private const string NoteId = "11111111-1111-4111-8111-111111111111";
    private const string NoteLineId = "11111111-1111-4111-8111-111111111112";
    private const string PersonId = "11111111-1111-4111-8111-111111111113";
    private const string NoteCloseId = "77777777-7777-4777-8777-777777777701";

    private const string Note = $$"""
        { "id": "{{NoteId}}", "kind": "entity", "name": "Note", "formatVersion": 1, "displayField": "title",
          "fields": [
            { "name": "title", "type": "text", "required": true, "maxLength": 200 },
            { "name": "code", "type": "text", "maxLength": 50 },
            { "name": "amount", "type": "decimal", "precision": 18, "scale": 2 },
            { "name": "status", "type": "enum", "values": ["open", "closed"] },
            { "name": "person", "type": "reference", "target": "Person" },
            { "name": "lines", "type": "child-collection", "target": "NoteLine" },
            { "name": "total", "type": "decimal", "expression": "amount * 2" },
            { "name": "number", "type": "text", "sequence": "NoteNumber" },
            { "name": "createdBy", "type": "text", "creator": true }
          ] }
        """;

    private const string NoteLine = $$"""
        { "id": "{{NoteLineId}}", "kind": "entity", "name": "NoteLine", "formatVersion": 1,
          "fields": [ { "name": "text", "type": "text" } ] }
        """;

    private const string Person = $$"""
        { "id": "{{PersonId}}", "kind": "entity", "name": "Person", "formatVersion": 1, "displayField": "name",
          "fields": [ { "name": "name", "type": "text", "required": true } ] }
        """;

    [Fact]
    public void An_endpoint_with_output_and_wait_compiles_into_the_model()
    {
        using var folder = Folder().With("endpoints/import-note.json", Endpoint(
            "ImportNote",
            "note",
            "noteclose",
            """
            [
              { "name": "title", "type": "text", "required": true, "maxLength": 200 },
              { "name": "code", "type": "text", "maxLength": 50 },
              { "name": "amount", "type": "decimal", "precision": 18, "scale": 2 },
              { "name": "state", "type": "enum", "values": ["open"] },
              { "name": "who", "type": "reference", "target": "PERSON" },
              { "name": "rows", "type": "child-collection", "target": "NoteLine" }
            ]
            """,
            """{ "lines": "ROWS", "title": "title", "CODE": "code", "amount": "amount * 2", "status": "state", "person": "who" }""",
            """, "output": ["CODE", "status"], "wait": "PT10S" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var endpoint = Assert.Single(result.Model.Endpoints);
        Assert.Equal(
            (Guid.Parse(EndpointId), "ImportNote", "endpoints/import-note.json"),
            (endpoint.Id, endpoint.Name, endpoint.File));
        Assert.Equal(new EntityReference(Guid.Parse(NoteId), "Note"), endpoint.Entity);
        Assert.Equal(new ProcessReference(Guid.Parse(NoteCloseId), "NoteClose"), endpoint.Process);

        // Input targets resolve ignoring letter case to the declared names.
        Assert.Equal(
            [
                ("title", FieldType.Text, true, (int?)200, (int?)null, (int?)null, (EntityReference?)null),
                ("code", FieldType.Text, false, 50, null, null, null),
                ("amount", FieldType.Decimal, false, null, 18, 2, null),
                ("state", FieldType.Enum, false, null, null, null, null),
                ("who", FieldType.Reference, false, null, null, null, new EntityReference(Guid.Parse(PersonId), "Person")),
                ("rows", FieldType.ChildCollection, false, null, null, null, new EntityReference(Guid.Parse(NoteLineId), "NoteLine")),
            ],
            endpoint.Input.Select(input => (input.Name, input.Type, input.Required, input.MaxLength, input.Precision, input.Scale, input.Target)));
        Assert.Equal(["open"], endpoint.Input[3].Values);

        // The mapped fields come in the entity's order, named as the entity declares them. A child
        // collection names its input as the endpoint declares it.
        Assert.Equal(
            [
                ("title", "title", null),
                ("code", "code", null),
                ("amount", "amount * 2", null),
                ("status", "state", null),
                ("person", "who", null),
                ("lines", null, "rows"),
            ],
            endpoint.Values.Select(value => (value.Field, value.Expression?.Expression, value.CollectionInput)));
        Assert.Equal(["code", "status"], endpoint.Output);
        Assert.Equal(TimeSpan.FromSeconds(10), endpoint.Wait);
        Assert.Contains(result.Resources, resource => resource.Path == "endpoints/import-note.json");
    }

    [Fact]
    public void An_endpoint_without_output_or_wait_is_valid_and_does_not_wait()
    {
        using var folder = Folder().With("endpoints/import-note.json", Endpoint(
            "ImportNote", "Note", "NoteClose", """[{ "name": "title", "type": "text", "required": true }]""", """{ "title": "title", "status": "'open'" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var endpoint = Assert.Single(result.Model!.Endpoints);
        Assert.Equal((null, null), (endpoint.Output, endpoint.Wait));
        Assert.Equal(["title", "status"], endpoint.Values.Select(value => value.Field));
    }

    [Fact]
    public void An_endpoint_that_names_a_process_without_a_subject_is_reported_at_its_process()
    {
        using var folder = Folder()
            .With("processes/nightly-job.json", """
                { "id": "77777777-7777-4777-8777-777777777703", "kind": "process", "name": "NightlyJob", "formatVersion": 1,
                  "parameters": [{ "name": "days", "type": "integer" }],
                  "start": "done", "steps": [{ "name": "done", "type": "end" }] }
                """)
            .With("endpoints/import-note.json", Endpoint(
                "ImportNote", "Note", "NightlyJob", """[{ "name": "title", "type": "text", "required": true }]""", """{ "title": "title" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.EndpointProcessOverOtherEntity, "endpoints/import-note.json", "/process"),
            (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Contains("has no subject", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Entity_process_output_and_wait_mistakes_are_each_a_diagnostic_at_their_path_in_one_pass()
    {
        using var folder = Folder()
            .With("endpoints/a.json", Endpoint("A", "Nothing", "Nope", "[]", """{ "nope": "1" }""", """, "output": ["nope"], "wait": "PT31S" """))
            .With("endpoints/b.json", Endpoint("B", "Note", "PersonReview", "[]", "{}", """, "output": ["title", "nope"], "wait": "PT0S" """, OtherEndpointId))
            .With("endpoints/c.json", Endpoint("C", "NoteLine", "NoteClose", "[]", "{}", id: ThirdEndpointId));

        var result = ApplicationCompiler.Compile(folder.Path);

        // Without its entity, an endpoint's mapping and output are not checked.
        Assert.Equal(
            [
                (DiagnosticCodes.UnknownEndpointEntity, "endpoints/a.json", "/entity"),
                (DiagnosticCodes.UnknownEndpointProcess, "endpoints/a.json", "/process"),
                (DiagnosticCodes.InvalidEndpointWait, "endpoints/a.json", "/wait"),
                (DiagnosticCodes.UnknownEndpointOutputField, "endpoints/b.json", "/output/1"),
                (DiagnosticCodes.EndpointProcessOverOtherEntity, "endpoints/b.json", "/process"),
                (DiagnosticCodes.InvalidEndpointWait, "endpoints/b.json", "/wait"),
                (DiagnosticCodes.EndpointOverChildEntity, "endpoints/c.json", "/entity"),
                (DiagnosticCodes.EndpointProcessOverOtherEntity, "endpoints/c.json", "/process"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
        Assert.Equal(
            "The process 'PersonReview' is over the entity 'Person', but the endpoint creates records of 'Note'.",
            result.Diagnostics.First(diagnostic => diagnostic.Code == DiagnosticCodes.EndpointProcessOverOtherEntity).Message);
        Assert.Equal(
            "The wait 'PT0S' must be an ISO 8601 duration such as 'PT10S', more than zero and at most 'PT30S'.",
            result.Diagnostics.Single(diagnostic => diagnostic.Code == DiagnosticCodes.InvalidEndpointWait && diagnostic.File == "endpoints/b.json").Message);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("PT30S")]
    [InlineData("PT1S")]
    public void A_wait_up_to_thirty_seconds_is_valid(string wait)
    {
        using var folder = Folder().With("endpoints/import-note.json", Endpoint(
            "ImportNote", "Note", "NoteClose", "[]", """{ "title": "'x'" }""", $$""", "output": ["title"], "wait": "{{wait}}" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Value_mistakes_are_each_a_diagnostic_at_their_key_in_one_pass()
    {
        using var folder = Folder().With("endpoints/import-note.json", Endpoint(
            "ImportNote",
            "Note",
            "NoteClose",
            """[{ "name": "title", "type": "text" }, { "name": "rows", "type": "child-collection", "target": "NoteLine" }]""",
            """
            {
              "nope": "title",
              "total": "1",
              "number": "title",
              "createdBy": "title",
              "amount": "title",
              "title": "title",
              "TITLE": "title",
              "lines": "title",
              "code": "rows"
            }
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownEndpointValueField, "/values/TITLE"),
                (ExpressionDiagnosticCodes.ResultTypeMismatch, "/values/amount"),
                (ExpressionDiagnosticCodes.UnknownName, "/values/code"),
                (DiagnosticCodes.ReadOnlyEndpointValueField, "/values/createdBy"),
                (DiagnosticCodes.InvalidEndpointCollectionValue, "/values/lines"),
                (DiagnosticCodes.UnknownEndpointValueField, "/values/nope"),
                (DiagnosticCodes.ReadOnlyEndpointValueField, "/values/number"),
                (DiagnosticCodes.ReadOnlyEndpointValueField, "/values/total"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(("endpoints/import-note.json", Guid.Parse(EndpointId)), (diagnostic.File, diagnostic.ResourceId)));
        Assert.Equal(
            "The expression must be decimal, but it is text.",
            result.Diagnostics.Single(diagnostic => diagnostic.Path == "/values/amount").Message);
        Assert.Equal(
            "The field 'total' is computed, so the server sets it. An endpoint maps only fields it can write.",
            result.Diagnostics.Single(diagnostic => diagnostic.Path == "/values/total").Message);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("""{ "name": "state", "type": "enum", "values": ["open", "archived"] }""", "status", "state", ExpressionDiagnosticCodes.ResultTypeMismatch)]
    [InlineData("""{ "name": "who", "type": "reference", "target": "Note" }""", "person", "who", ExpressionDiagnosticCodes.ResultTypeMismatch)]
    [InlineData("""{ "name": "title", "type": "text" }""", "status", "'draft'", ExpressionDiagnosticCodes.UnknownEnumValue)]
    [InlineData("""{ "name": "title", "type": "text" }""", "title", "upper(title", ExpressionDiagnosticCodes.SyntaxError)]
    [InlineData("""{ "name": "who", "type": "reference", "target": "Person" }""", "code", "who.name", ExpressionDiagnosticCodes.UnknownName)]
    [InlineData("""{ "name": "rows", "type": "child-collection", "target": "Person" }""", "lines", "rows", DiagnosticCodes.InvalidEndpointInputTarget)]
    public void A_value_that_does_not_fit_its_field_is_reported(string input, string key, string value, string code)
    {
        using var folder = Folder().With("endpoints/import-note.json", Endpoint(
            "ImportNote", "Note", "NoteClose", $"[{input}]", $$"""{ "{{key}}": "{{value}}" }"""));

        var result = ApplicationCompiler.Compile(folder.Path);

        // An input with a diagnostic of its own is not matched against the mapping again.
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(code, diagnostic.Code);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Input_mistakes_are_each_a_diagnostic_at_their_path_in_one_pass()
    {
        using var folder = Folder().With("endpoints/import-note.json", Endpoint(
            "ImportNote",
            "Note",
            "NoteClose",
            """
            [
              { "name": "title", "type": "text" },
              { "name": "TITLE", "type": "text" },
              { "name": "rows", "type": "child-collection", "target": "Person" },
              { "name": "who", "type": "reference", "target": "Nope" },
              { "name": "state", "type": "enum" },
              { "name": "count", "type": "integer", "maxLength": 5 },
              { "name": "line", "type": "reference", "target": "NoteLine" }
            ]
            """,
            "{}"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.DuplicateEndpointInputName, "/input/1/name"),
                (DiagnosticCodes.InvalidEndpointInputTarget, "/input/2/target"),
                (DiagnosticCodes.UnknownReferenceTarget, "/input/3/target"),
                (DiagnosticCodes.MissingTypeProperty, "/input/4"),
                (DiagnosticCodes.InvalidConstraint, "/input/5/maxLength"),
                (DiagnosticCodes.ReferenceToChildEntity, "/input/6/target"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.Equal(
            "The input name 'TITLE' is already used by input 'title' of this endpoint.",
            result.Diagnostics[0].Message);
        Assert.Equal(
            "The target entity 'Person' is not a child entity. A child-collection input takes the rows of a child entity.",
            result.Diagnostics[1].Message);
        Assert.Null(result.Model);
    }

    [Fact]
    public void An_entity_or_process_file_that_did_not_load_is_not_reported_again()
    {
        using var folder = Folder()
            .With("entities/broken.json", """
                { "id": "11111111-1111-4111-8111-111111111119", "kind": "entity", "name": "Broken", "formatVersion": 1, "fields": "none" }
                """)
            .With("processes/broken.json", """
                { "id": "77777777-7777-4777-8777-777777777709", "kind": "process", "name": "BrokenReview", "formatVersion": 1 }
                """)
            .With("endpoints/import-note.json", Endpoint("ImportNote", "Broken", "BrokenReview", "[]", "{}"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.File == "endpoints/import-note.json");
    }

    [Theory]
    [InlineData(""" "input": [{ "name": "title", "type": "text", "label": { "textKey": "x" } }], "values": {} """)]
    [InlineData(""" "input": [{ "name": "title", "type": "text", "unique": true }], "values": {} """)]
    [InlineData(""" "input": [{ "name": "title", "type": "text", "expression": "'x'" }], "values": {} """)]
    [InlineData(""" "input": [{ "name": "title", "type": "text", "sequence": "NoteNumber" }], "values": {} """)]
    [InlineData(""" "input": [{ "name": "title", "type": "text", "creator": true }], "values": {} """)]
    [InlineData(""" "input": [], "values": { "title": "" } """)]
    [InlineData(""" "input": [], "values": {}, "output": ["title"] """)]
    [InlineData(""" "input": [], "values": {}, "wait": "PT10S" """)]
    [InlineData(""" "input": [], "values": {}, "event": { "name": "NoteClosed" } """)]
    [InlineData(""" "input": [] """)]
    public void An_endpoint_in_another_shape_is_a_schema_violation(string body)
    {
        using var folder = Folder().With("endpoints/import-note.json", $$"""
            { "id": "{{EndpointId}}", "kind": "endpoint", "name": "ImportNote", "formatVersion": 1, "entity": "Note", "process": "NoteClose", {{body}} }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, "endpoints/import-note.json"), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    private static string Endpoint(string name, string entity, string process, string input, string values, string rest = "", string id = EndpointId) => $$"""
        { "id": "{{id}}", "kind": "endpoint", "name": "{{name}}", "formatVersion": 1, "entity": "{{entity}}", "process": "{{process}}",
          "input": {{input}}, "values": {{values}} {{rest}} }
        """;

    /// <summary>
    /// An application with a root entity <c>Note</c>, which owns <c>NoteLine</c> rows, references a
    /// <c>Person</c> and has a computed, a sequence and a creator field, the process <c>NoteClose</c>
    /// over it, and the process <c>PersonReview</c> over <c>Person</c>.
    /// </summary>
    private static TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/note.json", Note)
            .With("entities/note-line.json", NoteLine)
            .With("entities/person.json", Person)
            .With("sequences/note-number.json", """
                { "id": "88888888-8888-4888-8888-888888888801", "kind": "sequence", "name": "NoteNumber", "formatVersion": 1, "format": "N-{n:5}" }
                """)
            .With("processes/note-close.json", $$"""
                { "id": "{{NoteCloseId}}", "kind": "process", "name": "NoteClose", "formatVersion": 1, "entity": "Note",
                  "start": "done", "steps": [{ "name": "done", "type": "end" }] }
                """)
            .With("processes/person-review.json", """
                { "id": "77777777-7777-4777-8777-777777777702", "kind": "process", "name": "PersonReview", "formatVersion": 1, "entity": "Person",
                  "start": "done", "steps": [{ "name": "done", "type": "end" }] }
                """);
}
