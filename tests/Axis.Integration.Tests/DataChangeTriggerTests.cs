using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Data.Naming;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Data change triggers fire for the writes that commit: a record API create and update, an
/// inbound endpoint's create, an <c>updateRecord</c> step and a task form save.
/// <c>EscalateOnStage</c> starts <c>Escalate</c> on an update that changes <c>stage</c>, and
/// <c>NotifyEscalated</c> starts <c>Notify</c> when the new stage is <c>escalated</c>.
/// <c>ReviewNewTickets</c> starts <c>Review</c> on every create.
/// <c>RecheckAmounts</c> starts <c>Recheck</c> on an update that changes <c>amount</c>, when the new
/// amount is at least 100. The signed-in clerk holds no process grant, so each start shows that the
/// trigger runs as <c>system</c>.
/// </summary>
public sealed class DataChangeTriggerTests(TriggerApiFixture fixture) : IClassFixture<TriggerApiFixture>
{
    private const string Tickets = "/api/apps/TriggerApp/entities/Ticket/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_create_starts_the_trigger_process_with_the_record_and_a_failed_create_starts_none()
    {
        var instancesBefore = await fixture.CountAsync("SELECT count(*) FROM axis.process_instances");

        var id = await CreateAsync("""{ "title": "Printer" }""");

        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.process_instances
            WHERE subject_id = @record AND process_id = @process AND release_id = @release AND state = 'running' AND revision = 1
            """,
            ("record", id), ("process", fixture.ProcessId("Review")), ("release", fixture.ReleaseId)));
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.process_work_items w
            JOIN axis.process_instances i ON i.id = w.process_instance_id
            WHERE i.subject_id = @record AND w.tenant_id = 'a' AND w.kind = 'process.step'
            """,
            ("record", id)));
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE record_id = @record AND action = 'process.started'", ("record", id)));
        Assert.Equal(instancesBefore + 1, await fixture.CountAsync("SELECT count(*) FROM axis.process_instances"));

        var ticketsBefore = await fixture.CountAsync($"SELECT count(*) FROM {fixture.TicketTable}");
        var auditsBefore = await fixture.CountAsync("SELECT count(*) FROM axis.audit_records");
        using var invalid = await SendAsync(HttpMethod.Post, Tickets, """{ "values": { "title": "Broken", "amount": -1 } }""");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(instancesBefore + 1, await fixture.CountAsync("SELECT count(*) FROM axis.process_instances"));
        Assert.Equal(ticketsBefore, await fixture.CountAsync($"SELECT count(*) FROM {fixture.TicketTable}"));
        Assert.Equal(auditsBefore, await fixture.CountAsync("SELECT count(*) FROM axis.audit_records"));
    }

    [Fact]
    public async Task An_update_starts_the_process_only_when_a_listed_field_changes()
    {
        var id = await CreateAsync("""{ "title": "Scanner", "amount": 10 }""");

        // A field that is not in the trigger's fields.
        await UpdateAsync(id, """{ "version": 1, "values": { "title": "Scanner 2" } }""");
        Assert.Equal(0L, await RecheckCountAsync(id));

        // The listed field, sent with the value it already has.
        await UpdateAsync(id, """{ "version": 2, "values": { "amount": 10 } }""");
        Assert.Equal(0L, await RecheckCountAsync(id));

        await UpdateAsync(id, """{ "version": 3, "values": { "amount": 150 } }""");
        Assert.Equal(1L, await RecheckCountAsync(id));
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.process_instances
            WHERE subject_id = @record AND process_id = @process AND release_id = @release AND state = 'running'
            """,
            ("record", id), ("process", fixture.ProcessId("Recheck")), ("release", fixture.ReleaseId)));
    }

    [Fact]
    public async Task A_user_with_no_start_grant_triggers_the_start_and_the_audit_record_names_system_and_the_trigger()
    {
        var id = await CreateAsync("""{ "title": "Monitor" }""");

        var details = await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE record_id = @record AND action = 'process.started'",
            ("record", id));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["processId"] = fixture.ProcessId("Review"),
                ["releaseId"] = fixture.ReleaseId,
                ["trigger"] = "ReviewNewTickets",
            },
            JsonNode.Parse((string)details!)));
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.audit_records WHERE record_id = @record AND action = 'process.started' AND actor = 'system' AND actor_name IS NULL",
            ("record", id)));

        // The clerk's own audit record is still the clerk's.
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.audit_records WHERE record_id = @record AND action = 'record.created' AND actor = 'cara'",
            ("record", id)));
    }

    [Fact]
    public async Task The_instance_a_trigger_starts_keeps_the_writing_user_as_its_accountable_user()
    {
        var id = await CreateAsync("""{ "title": "Dock" }""");

        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.process_instances
            WHERE subject_id = @record AND process_id = @process AND accountable_user = 'cara' AND decision_task IS NULL
            """,
            ("record", id), ("process", fixture.ProcessId("Review"))));
    }

    [Fact]
    public async Task A_create_whose_start_condition_is_false_is_stored_and_the_skip_is_logged()
    {
        var id = await CreateAsync("""{ "title": "Retired", "status": "closed" }""");

        Assert.Equal(1L, await fixture.CountAsync($"SELECT count(*) FROM {fixture.TicketTable} WHERE {Column("id")} = @id", ("id", id)));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE subject_id = @record", ("record", id)));
        var skipped = Assert.Single(fixture.Logs.Entries, entry => entry.Contains(id.ToString("D"), StringComparison.Ordinal));
        Assert.StartsWith("Information: ", skipped, StringComparison.Ordinal);
        Assert.Contains("ReviewNewTickets", skipped, StringComparison.Ordinal);
        Assert.Contains("the start condition is not true", skipped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_update_whose_when_is_false_is_stored_and_starts_nothing()
    {
        var id = await CreateAsync("""{ "title": "Cable", "amount": 5 }""");

        await UpdateAsync(id, """{ "version": 1, "values": { "amount": 50 } }""");

        Assert.Equal(0L, await RecheckCountAsync(id));
        Assert.Equal(1L, await fixture.CountAsync($"SELECT count(*) FROM {fixture.TicketTable} WHERE {Column("id")} = @id AND {Column("amount")} = 50", ("id", id)));
        var skipped = Assert.Single(fixture.Logs.Entries, entry => entry.Contains(id.ToString("D"), StringComparison.Ordinal) && entry.Contains("RecheckAmounts", StringComparison.Ordinal));
        Assert.StartsWith("Information: ", skipped, StringComparison.Ordinal);
        Assert.Contains("the when is not true", skipped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_endpoint_call_starts_its_own_process_as_the_caller_and_the_trigger_process_as_system()
    {
        using var client = fixture.CreateClient().CallingAs("ticket-system");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/api/apps/TriggerApp/endpoints/ImportTicket", UriKind.Relative))
        {
            Content = new StringContent("""{ "input": { "title": "Imported", "amount": 7 } }""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Host = TriggerApiFixture.Host;

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var recordId = json.RootElement.GetProperty("recordId").GetGuid();
        var instanceId = json.RootElement.GetProperty("instanceId").GetGuid();
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.audit_records
            WHERE action = 'process.started' AND record_id = @record AND process_instance_id = @instance
              AND actor = 'ticket-system' AND details ->> 'endpoint' = 'ImportTicket'
            """,
            ("record", recordId), ("instance", instanceId)));
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.audit_records a
            JOIN axis.process_instances i ON i.id = a.process_instance_id
            WHERE a.action = 'process.started' AND a.record_id = @record AND a.actor = 'system'
              AND i.process_id = @process AND a.details ->> 'trigger' = 'ReviewNewTickets'
            """,
            ("record", recordId), ("process", fixture.ProcessId("Review"))));
        Assert.Equal(2L, await fixture.CountAsync("SELECT count(*) FROM axis.process_instances WHERE subject_id = @record", ("record", recordId)));
    }

    [Fact]
    public async Task A_step_that_changes_a_field_its_own_trigger_watches_does_not_start_its_process_again_and_the_skip_is_logged()
    {
        var id = await CreateAsync("""{ "title": "Router" }""");
        await UpdateAsync(id, """{ "version": 1, "values": { "stage": "open" } }""");
        Assert.Equal(1L, await CountInstancesAsync(id, "Escalate"));

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(1L, await CountInstancesAsync(id, "Escalate"));
        var skipped = Assert.Single(fixture.Logs.Entries, entry =>
            entry.Contains(id.ToString("D"), StringComparison.Ordinal) && entry.Contains("EscalateOnStage", StringComparison.Ordinal));
        Assert.StartsWith("Information: ", skipped, StringComparison.Ordinal);
        Assert.Contains("the process already runs for the record", skipped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_that_changes_a_field_another_trigger_watches_starts_that_process_in_the_steps_transaction()
    {
        var id = await CreateAsync("""{ "title": "Switch" }""");
        await UpdateAsync(id, """{ "version": 1, "values": { "stage": "open" } }""");

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(1L, await CountInstancesAsync(id, "Notify"));
        var started = (string)(await fixture.ScalarAsync(
            """
            SELECT a.xmin::text FROM axis.audit_records a
            JOIN axis.process_instances i ON i.id = a.process_instance_id
            WHERE a.action = 'process.started' AND a.record_id = @record AND i.process_id = @process
            """,
            ("record", id), ("process", fixture.ProcessId("Notify"))))!;
        var updated = (string)(await fixture.ScalarAsync(
            """
            SELECT a.xmin::text FROM axis.audit_records a
            JOIN axis.process_instances i ON i.id = a.process_instance_id
            WHERE a.action = 'record.updated' AND a.actor = 'system' AND a.record_id = @record AND i.process_id = @process
            """,
            ("record", id), ("process", fixture.ProcessId("Escalate"))))!;
        Assert.Equal(updated, started);
    }

    [Fact]
    public async Task A_step_that_changes_a_field_another_trigger_watches_starts_that_process_with_the_accountable_user_of_the_step()
    {
        var id = await CreateAsync("""{ "title": "Relay" }""");
        await UpdateAsync(id, """{ "version": 1, "values": { "stage": "open" } }""");

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(1L, await CountInstancesAsync(id, "Notify"));
        const string accountable = "SELECT accountable_user FROM axis.process_instances WHERE subject_id = @record AND process_id = @process";
        Assert.Equal("cara", await fixture.ScalarAsync(accountable, ("record", id), ("process", fixture.ProcessId("Escalate"))));
        Assert.Equal("cara", await fixture.ScalarAsync(accountable, ("record", id), ("process", fixture.ProcessId("Notify"))));
    }

    [Fact]
    public async Task A_task_form_save_that_changes_a_watched_field_starts_the_triggered_process_as_system()
    {
        var id = await CreateAsync("""{ "title": "Dock" }""");
        var instance = await fixture.StartAsync("Triage", id);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var task = await fixture.TaskIdAsync(instance);

        using var client = fixture.CreateClient().SignedInAs("cara");
        using var request = Request(
            HttpMethod.Post,
            $"/api/apps/TriggerApp/tasks/{task:D}/complete",
            TriggerApiFixture.Host,
            """{ "outcome": "done", "version": 1, "values": { "stage": "escalated" } }""");
        using var response = await client.SendAsync(request, CancellationToken);

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(1L, await CountInstancesAsync(id, "Notify"));
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.audit_records a
            JOIN axis.process_instances i ON i.id = a.process_instance_id
            WHERE a.action = 'process.started' AND a.actor = 'system' AND a.record_id = @record
              AND i.process_id = @process AND a.details ->> 'trigger' = 'NotifyEscalated'
            """,
            ("record", id), ("process", fixture.ProcessId("Notify"))));
    }

    private static string Column(string field) => EntityNaming.Quote(field == "id" ? EntityNaming.IdColumn : EntityNaming.Column(field));

    private Task<long> CountInstancesAsync(Guid recordId, string process) =>
        fixture.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE subject_id = @record AND process_id = @process",
            ("record", recordId), ("process", fixture.ProcessId(process)));

    private Task<long> RecheckCountAsync(Guid recordId) =>
        fixture.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE subject_id = @record AND process_id = @process",
            ("record", recordId), ("process", fixture.ProcessId("Recheck")));

    private async Task<Guid> CreateAsync(string values)
    {
        using var response = await SendAsync(HttpMethod.Post, Tickets, $$"""{ "values": {{values}} }""");
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task UpdateAsync(Guid id, string body)
    {
        using var response = await SendAsync(HttpMethod.Patch, $"{Tickets}/{id:D}", body);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string body)
    {
        using var client = fixture.CreateClient().SignedInAs("cara");
        using var request = Request(method, path, TriggerApiFixture.Host, body);
        return await client.SendAsync(request, CancellationToken);
    }
}
