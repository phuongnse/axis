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
        { "id": "{{PersonId}}", "kind": "entity", "name": "Person", "formatVersion": 1, "displayField": "name",
          "fields": [ { "name": "name", "type": "text", "required": true } ] }
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
    public void An_event_trigger_that_names_a_process_without_a_subject_is_a_diagnostic_at_the_process()
    {
        using var folder = WithSubjectlessProcess(Folder())
            .With("triggers/review.json", Trigger("ReviewClosedNotes", "NoteClosed", "NightlyJob"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.TriggerProcessWithoutSubject, "triggers/review.json", "/process"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
    }

    [Fact]
    public void A_data_change_trigger_that_names_a_process_without_a_subject_is_a_diagnostic_at_the_process()
    {
        using var folder = WithSubjectlessProcess(Folder())
            .With("triggers/review.json", DataChangeTrigger("Note", "NightlyJob", """, "on": ["create"] """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.TriggerProcessWithoutSubject, "triggers/review.json", "/process"), (diagnostic.Code, diagnostic.File, diagnostic.Path));
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
    [InlineData(""" "type": "schedule", "schedule": "0 6 * * *", "timeZone": "Europe/Berlin", "dataSource": "NoteRows", "when": "priority >= 3" """)]
    [InlineData(""" "type": "schedule", "schedule": "0 6 * * *", "timeZone": "Europe/Berlin", "dataSource": "NoteRows", "mode": "async" """)]
    [InlineData(""" "type": "schedule", "schedule": "0 6 * * *", "dataSource": "NoteRows" """)]
    [InlineData(""" "type": "schedule", "schedule": "0 6 * * *", "timeZone": "Europe/Berlin", "dataSource": "NoteRows", "entity": "Note" """)]
    [InlineData(""" "type": "event", "event": "NoteClosed", "schedule": "0 6 * * *" """)]
    [InlineData(""" "type": "dataChange", "entity": "Note", "on": ["create"], "timeZone": "Europe/Berlin" """)]
    [InlineData(""" "type": "schedule", "schedule": "0 6 * * *", "timeZone": "Europe/Berlin", "dataSource": "NoteRows", "parameters": { "bad key": 1 } """)]
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

    [Fact]
    public void A_schedule_trigger_compiles_into_the_model_with_its_schedule_time_zone_data_source_and_parameters()
    {
        using var folder = ScheduleFolder().With("triggers/nightly.json", ScheduleTrigger(rest: """, "parameters": { "MINPRIORITY": 3, "Status": null } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        var trigger = Assert.Single(result.Model.Triggers);
        Assert.Equal(
            (Guid.Parse(TriggerId), "Nightly", "triggers/nightly.json", TriggerType.Schedule, TriggerMode.Async),
            (trigger.Id, trigger.Name, trigger.File, trigger.Type, trigger.Mode));
        Assert.Equal("0 6 * * 1-5", trigger.Schedule!.Expression);
        Assert.Equal("Europe/Berlin", trigger.TimeZone!.Id);
        Assert.Equal(new DataSourceReference(Guid.Parse(NoteRowsId), "NoteRows"), trigger.DataSource);
        Assert.Equal(new ProcessReference(Guid.Parse(NoteReviewId), "NoteReview"), trigger.Process);

        // The values are keyed by the declared names, in the data source's order.
        Assert.Equal(
            [new KeyValuePair<string, object?>("minPriority", 3L), new KeyValuePair<string, object?>("status", null)],
            trigger.Parameters!.ToList());
        Assert.Null(trigger.Event);
        Assert.Null(trigger.Entity);
        Assert.Null(trigger.When);
    }

    [Fact]
    public void A_schedule_trigger_reads_every_parameter_type_as_its_clr_type()
    {
        using var folder = ScheduleFolder()
            .With("data-sources/typed.json", $$"""
                { "id": "{{TypedRowsId}}", "kind": "dataSource", "name": "TypedRows", "formatVersion": 1, "entity": "Note",
                  "fields": [{ "name": "title", "path": "title" }],
                  "parameters": [
                    { "name": "t", "type": "text" },
                    { "name": "i", "type": "integer" },
                    { "name": "d", "type": "decimal" },
                    { "name": "b", "type": "boolean" },
                    { "name": "day", "type": "date" },
                    { "name": "at", "type": "date-time" },
                    { "name": "e", "type": "enum", "values": ["open", "closed"] },
                    { "name": "r", "type": "reference", "target": "Person" }
                  ] }
                """)
            .With("triggers/nightly.json", ScheduleTrigger(
                dataSource: "TypedRows",
                rest: $$""", "parameters": { "t": "x", "i": -4, "d": 1.5, "b": true, "day": "2026-03-29", "at": "2026-03-29T03:00:00+02:00", "e": "closed", "r": "{{PersonId}}" } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            ["x", -4L, 1.5m, true, new DateOnly(2026, 3, 29), new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.Zero), "closed", Guid.Parse(PersonId)],
            Assert.Single(result.Model!.Triggers).Parameters!.Values.ToArray());
    }

    [Fact]
    public void A_bad_schedule_is_reported_in_one_pass_at_each_path()
    {
        using var folder = ScheduleFolder().With("triggers/nightly.json", ScheduleTrigger(
            schedule: "* * * *", timeZone: "Mars/Olympus", dataSource: "PersonRows", process: "NoteReview"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.TriggerDataSourceOverOtherEntity, "/dataSource"),
                (DiagnosticCodes.MissingTriggerParameter, "/parameters"),
                (DiagnosticCodes.InvalidTriggerSchedule, "/schedule"),
                (DiagnosticCodes.UnknownTriggerTimeZone, "/timeZone"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.Contains("NoteReview", result.Diagnostics.Single(diagnostic => diagnostic.Code == DiagnosticCodes.TriggerDataSourceOverOtherEntity).Message);
        Assert.Null(result.Model);
    }

    [Fact]
    public void Every_parameter_the_trigger_does_not_give_is_reported_once_at_parameters()
    {
        using var folder = ScheduleFolder().With("triggers/nightly.json", ScheduleTrigger());

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [(DiagnosticCodes.MissingTriggerParameter, "/parameters"), (DiagnosticCodes.MissingTriggerParameter, "/parameters")],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.Contains("minPriority", result.Diagnostics[0].Message);
        Assert.Contains("status", result.Diagnostics[1].Message);
    }

    [Fact]
    public void A_schedule_trigger_over_an_unknown_data_source_is_a_diagnostic_at_data_source()
    {
        using var folder = ScheduleFolder().With("triggers/nightly.json", ScheduleTrigger(dataSource: "Missing"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownTriggerDataSource, "/dataSource"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_schedule_trigger_over_a_data_source_that_did_not_load_is_not_reported_again()
    {
        using var folder = ScheduleFolder()
            .With("data-sources/broken.json", """
                { "id": "99999999-9999-4999-8999-999999999909", "kind": "dataSource", "name": "Broken", "formatVersion": 1 }
                """)
            .With("triggers/nightly.json", ScheduleTrigger(dataSource: "Broken"));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.NotEmpty(result.Diagnostics);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.File.StartsWith("triggers/", StringComparison.Ordinal));
    }

    [Fact]
    public void A_schedule_trigger_over_a_grouped_data_source_is_a_diagnostic_at_data_source()
    {
        using var folder = ScheduleFolder().With("triggers/nightly.json", ScheduleTrigger(dataSource: "NotesByTitle"));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.GroupedTriggerDataSource, "/dataSource"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_schedule_trigger_with_an_unknown_or_repeated_parameter_key_is_a_diagnostic_at_the_key()
    {
        using var folder = ScheduleFolder().With("triggers/nightly.json", ScheduleTrigger(
            rest: """, "parameters": { "minPriority": 3, "nothing": 1, "MINPRIORITY": 4, "status": null } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Equal(
            [
                (DiagnosticCodes.UnknownTriggerParameter, "/parameters/MINPRIORITY"),
                (DiagnosticCodes.UnknownTriggerParameter, "/parameters/nothing"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)));
        Assert.Contains("already given by 'minPriority'", result.Diagnostics[0].Message);
    }

    [Theory]
    [InlineData(""" "minPriority": "3", "status": null """)]
    [InlineData(""" "minPriority": 3.5, "status": null """)]
    [InlineData(""" "minPriority": 99999999999999999999, "status": null """)]
    [InlineData(""" "minPriority": null, "status": null """)]
    [InlineData(""" "minPriority": true, "status": null """)]
    public void A_wrongly_typed_value_is_a_diagnostic_at_the_key(string parameters)
    {
        using var folder = ScheduleFolder().With("triggers/nightly.json", ScheduleTrigger(rest: $$""", "parameters": { {{parameters}} } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidTriggerParameterValue, "/parameters/minPriority"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void An_enum_value_that_is_not_in_the_values_is_a_diagnostic_at_the_key()
    {
        using var folder = ScheduleFolder().With("triggers/nightly.json", ScheduleTrigger(rest: """, "parameters": { "minPriority": 3, "status": "later" } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidTriggerParameterValue, "/parameters/status"), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData("* * * *")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("* * 0 * *")]
    [InlineData("* * * 13 *")]
    [InlineData("* * * * 8")]
    [InlineData("5-3 * * * *")]
    [InlineData("*/0 * * * *")]
    [InlineData("5/10 * * * *")]
    [InlineData("0 0 * JAN *")]
    [InlineData("0 0 * * MON")]
    [InlineData("0 0 30 2 *")]
    [InlineData("0 0 31 4,6,9,11 *")]
    [InlineData("1,,2 * * * *")]
    [InlineData("-1 * * * *")]
    [InlineData("1- * * * *")]
    [InlineData("1-2-3 * * * *")]
    [InlineData("*/-1 * * * *")]
    [InlineData("١ * * * *")]
    public void An_invalid_cron_is_a_diagnostic_at_schedule(string schedule)
    {
        using var folder = ScheduleFolder().With("triggers/nightly.json", ScheduleTrigger(
            schedule: schedule, rest: """, "parameters": { "minPriority": 3, "status": null } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.InvalidTriggerSchedule, "/schedule"), (diagnostic.Code, diagnostic.Path));
    }

    [Theory]
    [InlineData("0 0 29 2 *")]
    [InlineData("0 0 31 * *")]
    [InlineData("0 0 30 2 1")]
    [InlineData("*/15 9-17 * * 1-5")]
    [InlineData("0 0 * * 7")]
    [InlineData("0,30 */6 1,15 1-6/2 0")]
    public void A_valid_cron_compiles(string schedule)
    {
        using var folder = ScheduleFolder().With("triggers/nightly.json", ScheduleTrigger(
            schedule: schedule, rest: """, "parameters": { "minPriority": 3, "status": null } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("Europe/berlin")]
    [InlineData("W. Europe Standard Time")]
    [InlineData("Berlin")]
    public void A_time_zone_that_is_not_an_exact_iana_id_is_a_diagnostic_at_time_zone(string timeZone)
    {
        using var folder = ScheduleFolder().With("triggers/nightly.json", ScheduleTrigger(
            timeZone: timeZone, rest: """, "parameters": { "minPriority": 3, "status": null } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownTriggerTimeZone, "/timeZone"), (diagnostic.Code, diagnostic.Path));
    }

    [Fact]
    public void A_schedule_trigger_with_an_unknown_process_is_axc0129()
    {
        using var folder = ScheduleFolder().With("triggers/nightly.json", ScheduleTrigger(
            process: "Nothing", rest: """, "parameters": { "minPriority": 3, "status": null } """));

        var result = ApplicationCompiler.Compile(folder.Path);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.UnknownTriggerProcess, "/process"), (diagnostic.Code, diagnostic.Path));
    }

    private const string NoteRowsId = "99999999-9999-4999-8999-999999999901";
    private const string TypedRowsId = "99999999-9999-4999-8999-999999999904";

    private static string ScheduleTrigger(
        string schedule = "0 6 * * 1-5",
        string timeZone = "Europe/Berlin",
        string dataSource = "NoteRows",
        string process = "NoteReview",
        string rest = "") => $$"""
        { "id": "{{TriggerId}}", "kind": "trigger", "name": "Nightly", "formatVersion": 1, "type": "schedule",
          "schedule": "{{schedule}}", "timeZone": "{{timeZone}}", "dataSource": "{{dataSource}}", "process": "{{process}}" {{rest}} }
        """;

    /// <summary>
    /// <see cref="Folder"/> with the data source <c>NoteRows</c> over <c>Note</c> with the required integer
    /// <c>minPriority</c> and the optional enum <c>status</c>, the grouped data source <c>NotesByTitle</c>
    /// and the data source <c>PersonRows</c> over <c>Person</c>.
    /// </summary>
    private static TemporaryFolder ScheduleFolder() =>
        Folder()
            .With("data-sources/note-rows.json", $$"""
                { "id": "{{NoteRowsId}}", "kind": "dataSource", "name": "NoteRows", "formatVersion": 1, "entity": "Note",
                  "fields": [{ "name": "title", "path": "title" }],
                  "parameters": [
                    { "name": "minPriority", "type": "integer", "required": true },
                    { "name": "status", "type": "enum", "values": ["open", "closed"] }
                  ] }
                """)
            .With("data-sources/notes-by-title.json", """
                { "id": "99999999-9999-4999-8999-999999999902", "kind": "dataSource", "name": "NotesByTitle", "formatVersion": 1, "entity": "Note",
                  "fields": [{ "name": "title", "path": "title" }],
                  "aggregate": { "groupBy": ["title"], "measures": [{ "name": "n", "function": "count" }] } }
                """)
            .With("data-sources/person-rows.json", """
                { "id": "99999999-9999-4999-8999-999999999903", "kind": "dataSource", "name": "PersonRows", "formatVersion": 1, "entity": "Person",
                  "fields": [{ "name": "name", "path": "name" }],
                  "parameters": [{ "name": "limit", "type": "integer" }] }
                """);

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

    /// <summary>Adds the process <c>NightlyJob</c>, which has the integer parameter <c>days</c> and no subject.</summary>
    private static TemporaryFolder WithSubjectlessProcess(TemporaryFolder folder) =>
        folder.With("processes/nightly-job.json", """
            { "id": "77777777-7777-4777-8777-777777777703", "kind": "process", "name": "NightlyJob", "formatVersion": 1,
              "parameters": [{ "name": "days", "type": "integer" }],
              "start": "done", "steps": [{ "name": "done", "type": "end" }] }
            """);

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
