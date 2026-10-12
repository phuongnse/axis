using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

public sealed class EventCompilerTests
{
    private const string EventId = "5c1e7a2b-3d4f-4a6b-8c9d-0e1f2a3b4c5d";

    private const string NoteId = "11111111-1111-4111-8111-111111111111";

    private const string Note = $$"""
        { "id": "{{NoteId}}", "kind": "entity", "name": "Note", "formatVersion": 1,
          "fields": [
            { "name": "title", "type": "text" },
            { "name": "lines", "type": "child-collection", "target": "NoteLine" }
          ] }
        """;

    private const string NoteLine = """
        { "id": "11111111-1111-4111-8111-111111111112", "kind": "entity", "name": "NoteLine", "formatVersion": 1,
          "fields": [ { "name": "text", "type": "text" } ] }
        """;

    [Fact]
    public void An_event_compiles_into_the_model_with_its_entity_and_typed_fields()
    {
        using var folder = Folder().With("events/note-closed.json", Event("NoteClosed", "NOTE", """
            [
              { "name": "closedBy", "type": "text" },
              { "name": "lineCount", "type": "integer" },
              { "name": "total", "type": "decimal" },
              { "name": "urgent", "type": "boolean" },
              { "name": "dueOn", "type": "date" },
              { "name": "closedAt", "type": "date-time" }
            ]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var @event = Assert.Single(result.Model.Events);
        Assert.Equal(
            (Guid.Parse(EventId), "NoteClosed", "events/note-closed.json", new EntityReference(Guid.Parse(NoteId), "Note")),
            (@event.Id, @event.Name, @event.File, @event.Entity));
        Assert.Equal(
            [
                new EventFieldModel("closedBy", FieldType.Text),
                new EventFieldModel("lineCount", FieldType.Integer),
                new EventFieldModel("total", FieldType.Decimal),
                new EventFieldModel("urgent", FieldType.Boolean),
                new EventFieldModel("dueOn", FieldType.Date),
                new EventFieldModel("closedAt", FieldType.DateTime),
            ],
            @event.Fields);
        Assert.Contains(result.Resources, resource => resource.Path == "events/note-closed.json");
    }

    [Fact]
    public void An_event_without_payload_fields_is_valid()
    {
        using var folder = Folder().With("events/note-closed.json", Event("NoteClosed", "Note", "[]"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(Assert.Single(result.Model!.Events).Fields);
    }

    [Fact]
    public void Event_mistakes_are_each_a_diagnostic_at_their_path_in_one_pass()
    {
        using var folder = Folder()
            .With("events/a.json", Event("NoteClosed", "Nothing", """
                [
                  { "name": "closedBy", "type": "text" },
                  { "name": "CLOSEDBY", "type": "text" },
                  { "name": "status", "type": "enum" }
                ]
                """))
            .With("events/b.json", Event("LineAdded", "NoteLine", """
                [{ "name": "note", "type": "reference" }]
                """, "5c1e7a2b-3d4f-4a6b-8c9d-0e1f2a3b4c5e"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownEventEntity, "events/a.json", "/entity"),
                (DiagnosticCodes.DuplicateEventFieldName, "events/a.json", "/fields/1/name"),
                (DiagnosticCodes.InvalidEventFieldType, "events/a.json", "/fields/2/type"),
                (DiagnosticCodes.EventOverChildEntity, "events/b.json", "/entity"),
                (DiagnosticCodes.InvalidEventFieldType, "events/b.json", "/fields/0/type"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
        Assert.Equal(
            "The entity 'NoteLine' is a child entity owned by 'Note.lines'. An event is about a record of a root entity.",
            result.Diagnostics.Single(diagnostic => diagnostic.Code == DiagnosticCodes.EventOverChildEntity).Message);
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData("enum")]
    [InlineData("reference")]
    [InlineData("child-collection")]
    public void A_field_type_that_is_not_scalar_is_not_allowed(string type)
    {
        using var folder = Folder().With("events/note-closed.json", Event("NoteClosed", "Note", $$"""
            [{ "name": "value", "type": "{{type}}" }]
            """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InvalidEventFieldType, "/fields/0/type", $"The field type '{type}' is not allowed. An event field is text, integer, decimal, boolean, date or date-time."),
            (diagnostic.Code, diagnostic.Path, diagnostic.Message));
    }

    [Fact]
    public void An_entity_file_that_did_not_load_is_not_reported_again()
    {
        using var folder = Folder()
            .With("entities/broken.json", """
                { "id": "11111111-1111-4111-8111-111111111113", "kind": "entity", "name": "Broken", "formatVersion": 1, "fields": "none" }
                """)
            .With("events/note-closed.json", Event("NoteClosed", "Broken", "[]"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("entities/broken.json", diagnostic.File));
    }

    [Fact]
    public void Two_events_with_the_same_name_in_another_letter_case_are_a_duplicate()
    {
        using var folder = Folder()
            .With("events/a.json", Event("NoteClosed", "Note", "[]"))
            .With("events/b.json", Event("NOTECLOSED", "Note", "[]", "5c1e7a2b-3d4f-4a6b-8c9d-0e1f2a3b4c5e"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.DuplicateName, "events/b.json", "/name"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Theory]
    [InlineData(""" "entity": "Note", "fields": [{ "name": "amount", "type": "money" }] """)]
    [InlineData(""" "entity": "Note", "fields": [{ "name": "amount", "type": "decimal", "required": true }] """)]
    [InlineData(""" "entity": "Note" """)]
    public void An_event_in_another_shape_is_a_schema_violation(string body)
    {
        using var folder = Folder().With("events/note-closed.json", $$"""
            { "id": "{{EventId}}", "kind": "event", "name": "NoteClosed", "formatVersion": 1, {{body}} }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, "events/note-closed.json"), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    private static string Event(string name, string entity, string fields, string id = EventId) => $$"""
        { "id": "{{id}}", "kind": "event", "name": "{{name}}", "formatVersion": 1, "entity": "{{entity}}",
          "fields": {{fields}} }
        """;

    private static TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/note.json", Note)
            .With("entities/note-line.json", NoteLine);
}
