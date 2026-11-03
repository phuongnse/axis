using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

/// <summary>
/// The <c>createRecord</c> step creates a record of another entity as <c>system</c>, through the
/// rules of a record create. The tests activate a copy of StepApp with a <c>RequestNote</c> entity
/// that has a sequence, a computed field, a default, a creator field and a validation, the
/// processes <c>LogNote</c> and <c>LogBadNote</c> that create a note for a request, and a create
/// trigger on <c>RequestNote</c> that starts <c>NoteCreated</c>. The copy stays in this class's database.
/// </summary>
public sealed class ProcessCreateRecordStepTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private static readonly ConditionalWeakTable<ProcessStepFixture, Task<(Guid ReleaseId, ApplicationModel Model)>> _releases = [];

    private const string NoteNumber = """
        { "id": "4d1c0b7e-6a52-4b8e-9d3f-1e2a7c5b8f01", "kind": "sequence", "name": "NoteNumber", "formatVersion": 1, "format": "NOTE-{n:4}" }
        """;

    private const string RequestNote = """
        {
          "id": "9a3e5c71-2d48-4f6b-8c10-7b5d4e2f9a02",
          "kind": "entity",
          "name": "RequestNote",
          "formatVersion": 1,
          "displayField": "text",
          "fields": [
            { "name": "number", "type": "text", "sequence": "NoteNumber" },
            { "name": "request", "type": "reference", "target": "Request" },
            { "name": "text", "type": "text", "required": true },
            { "name": "shout", "type": "text", "expression": "upper(text)" },
            { "name": "kind", "type": "text", "default": "'note'" },
            { "name": "author", "type": "text", "creator": true }
          ],
          "validations": [
            { "name": "check1", "expression": "text != 'bad'", "message": { "textKey": "request.amountNegative" }, "field": "text" }
          ]
        }
        """;

    private const string LogNote = """
        {
          "id": "b1d7f2a4-8c36-4e59-a0b1-3f6c9d2e7a03",
          "kind": "process",
          "name": "LogNote",
          "formatVersion": 1,
          "entity": "Request",
          "start": "log",
          "steps": [
            {
              "name": "log",
              "type": "operation",
              "operation": "createRecord",
              "entity": "RequestNote",
              "set": { "request": "id", "text": "title" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private const string LogBadNote = """
        {
          "id": "c2e8a3b5-9d47-4f60-b1c2-4a7d0e3f8b04",
          "kind": "process",
          "name": "LogBadNote",
          "formatVersion": 1,
          "entity": "Request",
          "start": "log",
          "steps": [
            {
              "name": "log",
              "type": "operation",
              "operation": "createRecord",
              "entity": "RequestNote",
              "set": { "request": "id", "text": "'bad'" },
              "next": "done"
            },
            { "name": "done", "type": "end" }
          ]
        }
        """;

    private const string NoteCreated = """
        {
          "id": "d3f9b4c6-ae58-4071-82d3-5b8e1f4a9c05",
          "kind": "process",
          "name": "NoteCreated",
          "formatVersion": 1,
          "entity": "RequestNote",
          "start": "done",
          "steps": [{ "name": "done", "type": "end" }]
        }
        """;

    private const string NoteTrigger = """
        {
          "id": "e4a0c5d7-bf69-4182-93e4-6c9f2a5b0d06",
          "kind": "trigger",
          "name": "StartNoteCreated",
          "formatVersion": 1,
          "type": "dataChange",
          "entity": "RequestNote",
          "on": ["create"],
          "process": "NoteCreated"
        }
        """;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_step_creates_the_record_as_system_and_starts_the_create_trigger()
    {
        var (releaseId, model) = await ReleaseAsync();
        var subjectId = await fixture.InsertRequestAsync(null, 1);
        var instanceId = await fixture.StartAsync("LogNote", subjectId, releaseId, model);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, "done", true), await Instance(instanceId));
        var history = await fixture.HistoryAsync(instanceId);
        var output = Assert.IsType<JsonObject>(JsonNode.Parse(Assert.IsType<string>(history[0].Output)));
        Assert.Equal(["next", "recordId"], output.Select(entry => entry.Key).Order(StringComparer.Ordinal));
        Assert.Equal("done", output["next"]!.GetValue<string>());
        var recordId = output["recordId"]!.GetValue<Guid>();

        Assert.True(model.TryGetEntity("RequestNote", out var note));
        var title = (string)(await fixture.ScalarAsync(
            $"SELECT {Column("title")} FROM {RequestTable(model)} WHERE {Column("id")} = @id", ("id", subjectId)))!;
        var row = await NoteAsync(note, recordId);
        Assert.Matches("^NOTE-[0-9]{4}$", row.Number);
        Assert.Equal((subjectId, title, title.ToUpperInvariant(), "note", "system"), (row.Request, row.Text, row.Shout, row.Kind, row.Author));

        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.audit_records WHERE action = 'record.created' AND actor = 'system' AND process_instance_id = @instance AND record_id = @record AND entity_id = @entity",
                ("instance", instanceId),
                ("record", recordId),
                ("entity", note.Id)));
        Assert.True(model.TryGetProcess("NoteCreated", out var created));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_instances WHERE process_id = @process AND subject_id = @record AND subject_entity_id = @entity",
                ("process", created.Id),
                ("record", recordId),
                ("entity", note.Id)));
    }

    [Fact]
    public async Task A_value_that_breaks_a_validation_fails_the_step_and_leaves_no_record()
    {
        var (releaseId, model) = await ReleaseAsync();
        var subjectId = await fixture.InsertRequestAsync(null, 1);
        var instanceId = await fixture.StartAsync("LogBadNote", subjectId, releaseId, model);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(ProcessStarts.Failed, (await fixture.InstanceAsync(instanceId)).State);
        var failed = Assert.Single(await fixture.HistoryAsync(instanceId));
        Assert.Equal("log", failed.Step);
        Assert.StartsWith("The record create was rejected:", failed.Error, StringComparison.Ordinal);
        Assert.Contains("/values/text", failed.Error, StringComparison.Ordinal);

        Assert.True(model.TryGetEntity("RequestNote", out var note));
        Assert.Equal(
            0L,
            await fixture.CountAsync(
                $"SELECT count(*) FROM {EntityNaming.QualifiedTable(EntityNaming.Table(note.Id))} WHERE {Column("request")} = @request",
                ("request", subjectId)));
        Assert.Equal(
            0L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.audit_records WHERE action = 'record.created' AND process_instance_id = @instance",
                ("instance", instanceId)));
    }

    private Task<(Guid ReleaseId, ApplicationModel Model)> ReleaseAsync() =>
        _releases.GetValue(
            fixture,
            _ => fixture.ActivateCopyWithFilesAsync(
                ("sequences/note-number.json", NoteNumber),
                ("entities/request-note.json", RequestNote),
                ("processes/log-note.json", LogNote),
                ("processes/log-bad-note.json", LogBadNote),
                ("processes/note-created.json", NoteCreated),
                ("triggers/start-note-created.json", NoteTrigger)));

    private async Task<(string State, string Step, bool Ended)> Instance(Guid instanceId)
    {
        var (state, _, step, ended) = await fixture.InstanceAsync(instanceId);
        return (state, step, ended);
    }

    private async Task<NoteRow> NoteAsync(EntityModel note, Guid id)
    {
        await using var command = fixture.DataSource.CreateCommand(
            $"""
            SELECT {Column("number")}, {Column("request")}, {Column("text")}, {Column("shout")}, {Column("kind")}, {Column("author")}
            FROM {EntityNaming.QualifiedTable(EntityNaming.Table(note.Id))} WHERE {Column("id")} = @id
            """);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return new NoteRow(reader.GetString(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5));
    }

    private static string RequestTable(ApplicationModel model)
    {
        Assert.True(model.TryGetEntity("Request", out var request));
        return EntityNaming.QualifiedTable(EntityNaming.Table(request.Id));
    }

    private static string Column(string field) =>
        EntityNaming.Quote(field == "id" ? EntityNaming.IdColumn : EntityNaming.Column(field));

    private sealed record NoteRow(string Number, Guid Request, string Text, string Shout, string Kind, string Author);
}
