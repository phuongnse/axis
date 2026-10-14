using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Tests;

public sealed class TriggerCompilerTests
{
    private const string TriggerId = "e348a793-af68-42ce-83b5-336a9b10c7f6";
    private const string OtherTriggerId = "e348a793-af68-42ce-83b5-336a9b10c7f7";
    private const string ThirdTriggerId = "e348a793-af68-42ce-83b5-336a9b10c7f8";

    private const string NoteId = "11111111-1111-4111-8111-111111111111";
    private const string PersonId = "11111111-1111-4111-8111-111111111113";
    private const string NoteClosedId = "f86ca48b-4afc-4060-9e40-c423522ea71d";
    private const string NoteReviewId = "77777777-7777-4777-8777-777777777701";

    private const string Note = $$"""
        { "id": "{{NoteId}}", "kind": "entity", "name": "Note", "formatVersion": 1,
          "fields": [
            { "name": "title", "type": "text" },
            { "name": "priority", "type": "integer" },
            { "name": "summary", "type": "text", "expression": "title" }
          ] }
        """;

    private const string Person = $$"""
        { "id": "{{PersonId}}", "kind": "entity", "name": "Person", "formatVersion": 1,
          "fields": [ { "name": "name", "type": "text" } ] }
        """;

    [Fact]
    public void An_event_trigger_compiles_into_the_model_with_its_event_process_and_condition()
    {
        using var folder = Folder().With("triggers/review.json", Trigger("ReviewClosedNotes", "noteclosed", "NOTEREVIEW", """, "when": "priority >= 3", "mode": "async" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var trigger = Assert.Single(result.Model.Triggers);
        Assert.Equal(
            (Guid.Parse(TriggerId), "ReviewClosedNotes", "triggers/review.json", TriggerType.Event, TriggerMode.Async),
            (trigger.Id, trigger.Name, trigger.File, trigger.Type, trigger.Mode));

        // The event and the process resolve ignoring letter case to their declared names.
        Assert.Equal(new EventReference(Guid.Parse(NoteClosedId), "NoteClosed"), trigger.Event);
        Assert.Equal(new ProcessReference(Guid.Parse(NoteReviewId), "NoteReview"), trigger.Process);
        Assert.NotNull(trigger.When);
        Assert.Equal("priority >= 3", trigger.When.Expression);
        Assert.Equal(ExpressionType.Boolean, trigger.When.Check.Type);
        Assert.Contains(result.Resources, resource => resource.Path == "triggers/review.json");
    }

    [Fact]
    public void A_trigger_without_mode_or_condition_is_async_and_has_no_condition()
    {
        using var folder = Folder().With("triggers/review.json", Trigger("ReviewClosedNotes", "NoteClosed", "NoteReview"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var trigger = Assert.Single(result.Model!.Triggers);
        Assert.Equal((TriggerMode.Async, null), (trigger.Mode, trigger.When));
    }

    [Fact]
    public void A_trigger_in_sync_mode_compiles_to_sync()
    {
        using var folder = Folder().With("triggers/review.json", Trigger("ReviewClosedNotes", "NoteClosed", "NoteReview", """, "mode": "sync" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(TriggerMode.Sync, Assert.Single(result.Model!.Triggers).Mode);
    }

    [Fact]
    public void Process_and_event_mistakes_are_each_a_diagnostic_at_their_path_in_one_pass()
    {
        using var folder = Folder()
            .With("triggers/a.json", Trigger("A", "Nothing", "Nobody"))
            .With("triggers/b.json", Trigger("B", "PersonMoved", "NoteReview", id: OtherTriggerId))
            .With("triggers/c.json", Trigger("C", "NoteClosed", "PersonReview", id: ThirdTriggerId));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownTriggerEvent, "triggers/a.json", "/event"),
                (DiagnosticCodes.UnknownTriggerProcess, "triggers/a.json", "/process"),
                (DiagnosticCodes.TriggerEventOverOtherEntity, "triggers/b.json", "/event"),
                (DiagnosticCodes.TriggerEventOverOtherEntity, "triggers/c.json", "/event"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
        Assert.Equal(
            [
                "The event 'Nothing' was not found. No loaded event has that name.",
                "The process 'Nobody' was not found. No loaded process has that name.",
                "The event 'PersonMoved' is about the entity 'Person', but the process 'NoteReview' is over 'Note'. A trigger starts the process for the record the event names.",
                "The event 'NoteClosed' is about the entity 'Note', but the process 'PersonReview' is over 'Person'. A trigger starts the process for the record the event names.",
            ],
            result.Diagnostics.Select(diagnostic => diagnostic.Message));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_process_or_event_file_that_did_not_load_is_not_reported_again()
    {
        using var folder = Folder()
            .With("processes/broken.json", """
                { "id": "77777777-7777-4777-8777-777777777709", "kind": "process", "name": "BrokenReview", "formatVersion": 1 }
                """)
            .With("events/broken.json", """
                { "id": "f86ca48b-4afc-4060-9e40-c423522ea719", "kind": "event", "name": "BrokenEvent", "formatVersion": 1 }
                """)
            .With("triggers/a.json", Trigger("A", "BrokenEvent", "NoteReview", """, "when": "nothing" """))
            .With("triggers/b.json", Trigger("B", "NoteClosed", "BrokenReview", id: OtherTriggerId));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticCodes.SchemaViolation, diagnostic.Code));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.File.StartsWith("triggers/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("priority", ExpressionDiagnosticCodes.ResultTypeMismatch)]
    [InlineData("title == 'x'", ExpressionDiagnosticCodes.UnknownName)]
    [InlineData("IsUrgent(priority)", ExpressionDiagnosticCodes.UnknownFunction)]
    [InlineData("priority >=", ExpressionDiagnosticCodes.SyntaxError)]
    public void A_condition_that_is_not_a_boolean_over_the_payload_is_a_diagnostic_at_when(string when, string code)
    {
        using var folder = Folder()
            .With("rules/is-urgent.json", IsUrgentRule)
            .With("triggers/review.json", Trigger("ReviewClosedNotes", "NoteClosed", "NoteReview", $$""", "when": "{{when}}" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((code, "triggers/review.json", "/when"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_data_change_trigger_compiles_into_the_model_with_its_entity_on_flags_and_fields()
    {
        using var folder = Folder().With("triggers/review.json", DataChangeTrigger("note", "NOTEREVIEW", """, "on": ["update"], "fields": ["PRIORITY"] """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var trigger = Assert.Single(result.Model.Triggers);
        Assert.Equal(
            (Guid.Parse(TriggerId), "ReviewNotes", "triggers/review.json", TriggerType.DataChange, DataChangeKinds.Update),
            (trigger.Id, trigger.Name, trigger.File, trigger.Type, trigger.On));
        Assert.Equal(new EntityReference(Guid.Parse(NoteId), "Note"), trigger.Entity);
        Assert.Equal(new ProcessReference(Guid.Parse(NoteReviewId), "NoteReview"), trigger.Process);
        Assert.Equal(["priority"], trigger.Fields);
        Assert.Equal((TriggerMode.Async, null, null), (trigger.Mode, trigger.Event, trigger.When));
    }

    [Fact]
    public void A_data_change_trigger_without_fields_has_null_fields_and_both_flags()
    {
        using var folder = Folder().With("triggers/review.json", DataChangeTrigger("Note", "NoteReview", """, "on": ["create", "update"] """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var trigger = Assert.Single(result.Model!.Triggers);
        Assert.Equal((DataChangeKinds.Create | DataChangeKinds.Update, null), (trigger.On, trigger.Fields));
    }

    [Fact]
    public void A_data_change_condition_sees_the_record_and_can_call_a_rule()
    {
        using var folder = Folder()
            .With("rules/is-urgent.json", IsUrgentRule)
            .With("triggers/review.json", DataChangeTrigger("Note", "NoteReview", """, "on": ["create"], "when": "IsUrgent(priority) and title == 'x'" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        var when = Assert.Single(result.Model!.Triggers).When;
        Assert.NotNull(when);
        Assert.Equal(ExpressionType.Boolean, when.Check.Type);
    }

    [Fact]
    public void A_data_change_condition_that_is_not_a_boolean_is_a_diagnostic_at_when()
    {
        using var folder = Folder().With("triggers/review.json", DataChangeTrigger("Note", "NoteReview", """, "on": ["create"], "when": "priority" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((ExpressionDiagnosticCodes.ResultTypeMismatch, "triggers/review.json", "/when"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_data_change_trigger_over_an_unknown_entity_or_another_entity_is_a_diagnostic_at_entity()
    {
        using var folder = Folder()
            .With("triggers/a.json", DataChangeTrigger("Nothing", "NoteReview", """, "on": ["create"] """))
            .With("triggers/b.json", DataChangeTrigger("Person", "NoteReview", """, "on": ["create"] """, id: OtherTriggerId));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownTriggerEntity, "triggers/a.json", "/entity"),
                (DiagnosticCodes.TriggerEntityNotProcessSubject, "triggers/b.json", "/entity"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.File, diagnostic.Path)));
        Assert.Equal(
            [
                "The entity 'Nothing' was not found. No loaded entity has that name.",
                "The trigger watches the entity 'Person', but the process 'NoteReview' is over 'Note'. A trigger starts the process for the record that changed.",
            ],
            result.Diagnostics.Select(diagnostic => diagnostic.Message));
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_data_change_field_that_is_unknown_or_computed_is_a_diagnostic_at_its_index()
    {
        using var folder = Folder().With("triggers/review.json", DataChangeTrigger("Note", "NoteReview", """, "on": ["update"], "fields": ["title", "nothing", "Summary"] """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.InvalidTriggerField, "/fields/1"),
                (DiagnosticCodes.InvalidTriggerField, "/fields/2"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.Contains("names no field", result.Diagnostics[0].Message);
        Assert.Contains("computed field", result.Diagnostics[1].Message);
        Assert.Null(result.Model);
    }

    [Fact]
    public void A_data_change_trigger_over_an_entity_that_did_not_load_is_not_reported_again()
    {
        using var folder = Folder()
            .With("entities/broken.json", """
                { "id": "11111111-1111-4111-8111-111111111119", "kind": "entity", "name": "Broken", "formatVersion": 1 }
                """)
            .With("triggers/review.json", DataChangeTrigger("Broken", "NoteReview", """, "on": ["create"], "fields": ["x"], "when": "nothing" """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.File.StartsWith("triggers/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(""" "type": "dataChange", "entity": "Note", "on": ["create"], "event": "NoteClosed" """)]
    [InlineData(""" "type": "dataChange", "entity": "Note", "on": ["create"], "mode": "async" """)]
    [InlineData(""" "type": "dataChange", "entity": "Note", "on": [] """)]
    [InlineData(""" "type": "dataChange", "entity": "Note", "on": ["create", "create"] """)]
    [InlineData(""" "type": "dataChange", "entity": "Note", "on": ["delete"] """)]
    [InlineData(""" "type": "dataChange", "on": ["create"] """)]
    [InlineData(""" "type": "dataChange", "entity": "Note" """)]
    [InlineData(""" "type": "dataChange", "entity": "Note", "on": ["update"], "fields": [] """)]
    [InlineData(""" "type": "dataChange", "entity": "Note", "on": ["update"], "fields": ["title", "title"] """)]
    [InlineData(""" "type": "event", "event": "NoteClosed", "on": ["create"] """)]
    [InlineData(""" "type": "event", "event": "NoteClosed", "fields": ["title"] """)]
    [InlineData(""" "type": "schedule", "event": "NoteClosed" """)]
    [InlineData(""" "type": "dataChange", "event": "NoteClosed" """)]
    [InlineData(""" "type": "event", "event": "NoteClosed", "mode": "Sync" """)]
    [InlineData(""" "type": "event", "event": "NoteClosed", "mode": "later" """)]
    [InlineData(""" "type": "event", "event": "NoteClosed", "entity": "Note" """)]
    [InlineData(""" "type": "event", "event": "NoteClosed", "when": "" """)]
    [InlineData(""" "type": "event" """)]
    public void A_trigger_in_another_shape_is_a_schema_violation(string body)
    {
        using var folder = Folder().With("triggers/review.json", $$"""
            { "id": "{{TriggerId}}", "kind": "trigger", "name": "ReviewClosedNotes", "formatVersion": 1, "process": "NoteReview", {{body}} }
            """);

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.SchemaViolation, "triggers/review.json"), (diagnostic.Code, diagnostic.File)));
        Assert.Null(result.Model);
    }

    private const string IsUrgentRule = """
        { "id": "66666666-6666-4666-8666-666666666602", "kind": "rule", "name": "IsUrgent", "formatVersion": 1,
          "parameters": [{ "name": "value", "type": "integer" }], "resultType": "boolean", "expression": "value >= 3" }
        """;

    private static string DataChangeTrigger(string entity, string process, string rest = "", string id = TriggerId) => $$"""
        { "id": "{{id}}", "kind": "trigger", "name": "{{(id == TriggerId ? "ReviewNotes" : "ReviewOthers")}}", "formatVersion": 1, "type": "dataChange",
          "entity": "{{entity}}", "process": "{{process}}" {{rest}} }
        """;

    private static string Trigger(string name, string @event, string process, string rest = "", string id = TriggerId) => $$"""
        { "id": "{{id}}", "kind": "trigger", "name": "{{name}}", "formatVersion": 1, "type": "event",
          "event": "{{@event}}", "process": "{{process}}" {{rest}} }
        """;

    /// <summary>
    /// An application with the root entities <c>Note</c> and <c>Person</c>, the event
    /// <c>NoteClosed</c> about a note with an integer <c>priority</c> and a decimal <c>amount</c>,
    /// the event <c>PersonMoved</c> about a person, the process <c>NoteReview</c> over <c>Note</c>
    /// and the process <c>PersonReview</c> over <c>Person</c>.
    /// </summary>
    private static TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", PresentationCompilerTests.Manifest)
            .With("entities/note.json", Note)
            .With("entities/person.json", Person)
            .With("events/note-closed.json", $$"""
                { "id": "{{NoteClosedId}}", "kind": "event", "name": "NoteClosed", "formatVersion": 1, "entity": "Note",
                  "fields": [
                    { "name": "priority", "type": "integer" },
                    { "name": "amount", "type": "decimal" }
                  ] }
                """)
            .With("events/person-moved.json", """
                { "id": "f86ca48b-4afc-4060-9e40-c423522ea71e", "kind": "event", "name": "PersonMoved", "formatVersion": 1, "entity": "Person",
                  "fields": [] }
                """)
            .With("processes/note-review.json", $$"""
                { "id": "{{NoteReviewId}}", "kind": "process", "name": "NoteReview", "formatVersion": 1, "entity": "Note",
                  "start": "done", "steps": [{ "name": "done", "type": "end" }] }
                """)
            .With("processes/person-review.json", """
                { "id": "77777777-7777-4777-8777-777777777702", "kind": "process", "name": "PersonReview", "formatVersion": 1, "entity": "Person",
                  "start": "done", "steps": [{ "name": "done", "type": "end" }] }
                """);
}
