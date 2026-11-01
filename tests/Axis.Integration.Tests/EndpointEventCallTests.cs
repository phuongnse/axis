using System.Net;
using System.Text;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using static Axis.Integration.Tests.EndpointOutputFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// An inbound endpoint call with a <c>process</c> and an <c>event</c> raises the event for the new record in
/// its own transaction. EndpointApp's <c>ImportLoggedNote</c> raises <c>NoteLogged</c>, which a <c>sync</c>
/// trigger answers by starting <c>AckNote</c> and an <c>async</c> trigger by starting <c>FollowNote</c>.
/// A <c>quantity</c> of 1 makes its <c>share</c> field divide by zero.
/// </summary>
public sealed class EndpointEventCallTests(EndpointOutputFixture fixture) : IClassFixture<EndpointOutputFixture>
{
    private const string Endpoint = "/api/apps/EndpointApp/endpoints/ImportLoggedNote";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_call_stores_one_occurrence_of_the_active_release_that_names_its_instance_and_runs_the_triggers()
    {
        const string title = "Logged with triggers";

        using var response = await CallAsync($$"""{ "input": { "title": "{{title}}", "quantity": 3 } }""");

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var json = JsonDocument.Parse(body);
        var recordId = json.RootElement.GetProperty("recordId").GetGuid();
        var instanceId = json.RootElement.GetProperty("instanceId").GetGuid();
        Assert.Equal(1L, await NotesAsync(title));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                """
                SELECT count(*) FROM axis.events e
                JOIN axis.active_releases a ON a.release_id = e.release_id
                WHERE e.subject_id = @subject AND e.event = 'NoteLogged' AND e.process_instance_id = @instance
                """,
                ("subject", recordId),
                ("instance", instanceId)));
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM axis.events WHERE subject_id = @subject", ("subject", recordId)));

        // The sync trigger started its process inside the call. The async trigger waits for the worker.
        Assert.Equal(1L, await StartedAsync("AckNote", recordId));
        Assert.Equal(0L, await StartedAsync("FollowNote", recordId));

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(1L, await StartedAsync("FollowNote", recordId));
        Assert.Equal(1L, await StartedAsync("AckNote", recordId));
    }

    [Fact]
    public async Task The_occurrence_and_the_instances_its_triggers_start_carry_the_calling_service_account()
    {
        using var response = await CallAsync("""{ "input": { "title": "Logged by a service account", "quantity": 3 } }""");

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var json = JsonDocument.Parse(body);
        var recordId = json.RootElement.GetProperty("recordId").GetGuid();
        var instanceId = json.RootElement.GetProperty("instanceId").GetGuid();
        await fixture.RunUntilIdleAsync(CancellationToken);

        var caller = await fixture.ScalarAsync<string?>("SELECT accountable_user FROM axis.process_instances WHERE id = @id", ("id", instanceId));
        Assert.NotNull(caller);
        Assert.Equal(
            caller,
            await fixture.ScalarAsync<string?>("SELECT accountable_user FROM axis.events WHERE subject_id = @subject AND event = 'NoteLogged'", ("subject", recordId)));
        foreach (var process in new[] { "AckNote", "FollowNote" })
        {
            Assert.True(fixture.Model.TryGetProcess(process, out var model));
            Assert.Equal(
                caller,
                await fixture.ScalarAsync<string?>(
                    "SELECT accountable_user FROM axis.process_instances WHERE process_id = @process AND subject_id = @subject",
                    ("process", model.Id),
                    ("subject", recordId)));
        }
    }

    [Fact]
    public async Task A_repeated_call_with_the_same_idempotency_key_raises_no_second_occurrence()
    {
        const string title = "Logged once";
        const string input = $$"""{ "input": { "title": "{{title}}", "quantity": 3 } }""";

        using var first = await CallAsync(input, "log-once");
        var firstBody = await first.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(first.StatusCode == HttpStatusCode.Created, firstBody);
        using var repeat = await CallAsync(input, "log-once");
        Assert.Equal(HttpStatusCode.Created, repeat.StatusCode);

        using var json = JsonDocument.Parse(firstBody);
        var recordId = json.RootElement.GetProperty("recordId").GetGuid();
        Assert.Equal(1L, await NotesAsync(title));
        Assert.Equal(1L, await fixture.CountAsync("SELECT count(*) FROM axis.events WHERE subject_id = @subject", ("subject", recordId)));
    }

    [Fact]
    public async Task A_sync_trigger_start_that_fails_leaves_no_record_no_instance_and_no_occurrence()
    {
        const string title = "Logged with a failing trigger";
        Assert.True(fixture.Model.TryGetProcess("AckNote", out var ack));
        var instances = await InstancesAsync();
        var occurrences = await fixture.CountAsync("SELECT count(*) FROM axis.events");

        // The injected error fires only for the sync trigger's process, so no other start is affected.
        await fixture.ExecuteAsync(
            $$"""
            CREATE FUNCTION axis.test_injected_event_start_failure() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW.process_id = '{{ack.Id:D}}'::uuid THEN
                    RAISE EXCEPTION 'injected start failure';
                END IF;
                RETURN NEW;
            END
            $body$;
            CREATE TRIGGER test_injected_event_start_failure BEFORE INSERT ON axis.process_instances
                FOR EACH ROW EXECUTE FUNCTION axis.test_injected_event_start_failure();
            """);
        try
        {
            using var response = await CallAsync($$"""{ "input": { "title": "{{title}}", "quantity": 3 } }""");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            await fixture.ExecuteAsync(
                """
                DROP TRIGGER test_injected_event_start_failure ON axis.process_instances;
                DROP FUNCTION axis.test_injected_event_start_failure();
                """);
        }

        Assert.Equal(0L, await NotesAsync(title));
        Assert.Equal(instances, await InstancesAsync());
        Assert.Equal(occurrences, await fixture.CountAsync("SELECT count(*) FROM axis.events"));
    }

    [Fact]
    public async Task A_fields_expression_that_fails_at_run_time_is_a_400_and_stores_nothing()
    {
        const string title = "Logged with a division by zero";
        var instances = await InstancesAsync();
        var occurrences = await fixture.CountAsync("SELECT count(*) FROM axis.events");

        using var response = await CallAsync($$"""{ "input": { "title": "{{title}}", "quantity": 1 } }""");

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("/event/fields/share", out _), body);
        Assert.Equal(0L, await NotesAsync(title));
        Assert.Equal(instances, await InstancesAsync());
        Assert.Equal(occurrences, await fixture.CountAsync("SELECT count(*) FROM axis.events"));
    }

    private Task<long> NotesAsync(string title)
    {
        Assert.True(fixture.Model.TryGetEntity("Note", out var note));
        return fixture.CountAsync(
            $"SELECT count(*) FROM {EntityNaming.QualifiedTable(EntityNaming.Table(note.Id))} WHERE {EntityNaming.Quote(EntityNaming.Column("title"))} = @title",
            ("title", title));
    }

    private async Task<long> StartedAsync(string process, Guid recordId)
    {
        Assert.True(fixture.Model.TryGetProcess(process, out var model));
        return await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_instances WHERE process_id = @process AND subject_id = @subject",
            ("process", model.Id),
            ("subject", recordId));
    }

    private Task<long> InstancesAsync() => fixture.CountAsync("SELECT count(*) FROM axis.process_instances");

    private async Task<HttpResponseMessage> CallAsync(string body, string? key = null)
    {
        using var client = fixture.CreateClient().CallingAs("importer-system");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Endpoint, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Host = Host;
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }

        return await client.SendAsync(request, CancellationToken);
    }
}
