using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using Npgsql;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The routes that list, redeliver and discard the dead letters of a process. StepApp's <c>Announce</c>
/// raises <c>RequestNoted</c>, and both <c>ApproveNotedRequests</c> and <c>DecideHugeNotedRequests</c> pass
/// their <c>when</c> for a ratio of a million or more. A PostgreSQL trigger makes the start of
/// <c>Decide</c> fail for one record until the delivery is a dead letter, and is dropped before the
/// operator acts. A second PostgreSQL trigger makes the wake of one <c>WaitEvent</c> instance fail the same way.
/// <c>maria</c> holds <c>inspect</c> and <c>retry</c> on <c>Decide</c> and <c>WaitEvent</c>, <c>ines</c> holds
/// <c>inspect</c> only, and <c>otto</c> holds neither. The class shares a collection with
/// <see cref="DeadLetterTests"/>, so the dead letters it makes cannot reach that class's metric listener.
/// </summary>
[Collection(DeadLetterTests.CollectionName)]
public sealed class DeadLetterEndpointTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Tenant = TenantA;

    private const string Reason = "The receiving system is back.";

    private const int Limit = 5;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_with_inspect_lists_the_open_dead_letter_with_the_commands_the_grants_allow_and_a_user_without_inspect_gets_403()
    {
        var letter = await DeadLetterAsync();

        using var maria = await ListOkAsync("maria", "?state=open");
        var item = FindItem(maria, letter.Id);
        Assert.Equal(
            ["id", "kind", "eventId", "triggerId", "processInstanceId", "emailId", "webhookId", "occurrenceAt", "attempts", "lastError", "state", "reason", "settledBy", "createdAt", "settledAt", "commands"],
            item.EnumerateObject().Select(property => property.Name));
        Assert.Equal("event.deliver", item.GetProperty("kind").GetString());
        Assert.Equal(letter.EventId, item.GetProperty("eventId").GetGuid());
        Assert.Equal(letter.TriggerId, item.GetProperty("triggerId").GetGuid());
        Assert.Equal(Limit, item.GetProperty("attempts").GetInt32());
        Assert.Equal("Npgsql.PostgresException with SQLSTATE P0001", item.GetProperty("lastError").GetString());
        Assert.Equal("open", item.GetProperty("state").GetString());
        Assert.Equal(DateTimeKind.Utc, item.GetProperty("createdAt").GetDateTime().Kind);
        Assert.All(["processInstanceId", "emailId", "webhookId", "occurrenceAt", "reason", "settledBy", "settledAt"], name => Assert.Equal(JsonValueKind.Null, item.GetProperty(name).ValueKind));
        Assert.Equal(["redeliver", "discard"], Commands(item));
        Assert.Equal(1, maria.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(20, maria.RootElement.GetProperty("pageSize").GetInt32());
        Assert.True(maria.RootElement.GetProperty("totalCount").GetInt64() >= 1);

        using var ines = await ListOkAsync("ines", string.Empty);
        Assert.Empty(Commands(FindItem(ines, letter.Id)));

        using var denied = await SendAsync("otto", HttpMethod.Get, "dead-letters", body: null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var invalid = await SendAsync("maria", HttpMethod.Get, "dead-letters?state=closed&pageSize=0", body: null);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var problem = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(["pageSize", "state"], problem.RootElement.GetProperty("errors").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        using var discarded = await ListOkAsync("maria", "?state=discarded");
        Assert.DoesNotContain(discarded.RootElement.GetProperty("items").EnumerateArray(), candidate => candidate.GetProperty("id").GetGuid() == letter.Id);
    }

    [Fact]
    public async Task Redelivering_an_event_dead_letter_starts_only_the_trigger_that_failed_and_settles_the_row()
    {
        var letter = await DeadLetterAsync();
        var roleApprove = Assert.Single(await InstancesAsync("RoleApprove", letter.SubjectId));
        Assert.Empty(await InstancesAsync("Decide", letter.SubjectId));

        using var response = await SendAsync("maria", HttpMethod.Post, $"dead-letters/{letter.Id:D}/redeliver", ReasonBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(letter.Id, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("redelivered", body.RootElement.GetProperty("state").GetString());
        Assert.Equal(Reason, body.RootElement.GetProperty("reason").GetString());
        Assert.Equal("maria", body.RootElement.GetProperty("settledBy").GetString());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("settledAt").ValueKind);
        Assert.Empty(Commands(body.RootElement));
        Assert.Equal(
            1L,
            await fixture.AdminCountAsync(
                Tenant, $"SELECT count(*) FROM axis.process_work_items WHERE kind = 'event.deliver' AND trigger_id = '{letter.TriggerId:D}'"));

        await fixture.RunUntilIdleAsync(Tenant, CancellationToken);

        var decide = Assert.Single(await InstancesAsync("Decide", letter.SubjectId));
        Assert.Equal(roleApprove, Assert.Single(await InstancesAsync("RoleApprove", letter.SubjectId)));
        Assert.Equal("redelivered", await StateAsync(letter.Id));
        Assert.Equal(
            0L,
            await fixture.AdminCountAsync(Tenant, $"SELECT count(*) FROM axis.delivery_failures WHERE target = '{letter.TriggerId:D}/{letter.EventId:D}'"));
        Assert.Equal(
            ("started", (Guid?)decide),
            await ReceiptAsync(letter.TriggerId, letter));
    }

    [Fact]
    public async Task Discarding_settles_the_row_a_second_command_gets_409_and_a_user_without_retry_gets_403()
    {
        var letter = await DeadLetterAsync();
        var route = $"dead-letters/{letter.Id:D}";

        using var forbidden = await SendAsync("ines", HttpMethod.Post, $"{route}/discard", ReasonBody);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("open", await StateAsync(letter.Id));

        using var missingReason = await SendAsync("maria", HttpMethod.Post, $"{route}/discard", "{}");
        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);

        using var unknown = await SendAsync("maria", HttpMethod.Post, $"dead-letters/{Guid.NewGuid():D}/discard", ReasonBody);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var malformed = await SendAsync("maria", HttpMethod.Post, "dead-letters/not-an-id/discard", ReasonBody);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal("open", await StateAsync(letter.Id));

        using var discard = await SendAsync("maria", HttpMethod.Post, $"{route}/discard", ReasonBody);
        Assert.Equal(HttpStatusCode.OK, discard.StatusCode);
        using var body = JsonDocument.Parse(await discard.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("discarded", body.RootElement.GetProperty("state").GetString());
        Assert.Empty(Commands(body.RootElement));
        Assert.Equal("discarded", await StateAsync(letter.Id));

        foreach (var command in new[] { "discard", "redeliver" })
        {
            using var again = await SendAsync("maria", HttpMethod.Post, $"{route}/{command}", ReasonBody);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
            using var problem = JsonDocument.Parse(await again.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal("The dead letter is not open.", problem.RootElement.GetProperty("title").GetString());
        }

        // A discard queues no delivery.
        Assert.Empty(await InstancesAsync("Decide", letter.SubjectId));
        Assert.Equal(
            0L,
            await fixture.AdminCountAsync(
                Tenant, $"SELECT count(*) FROM axis.process_work_items WHERE kind = 'event.deliver' AND trigger_id = '{letter.TriggerId:D}'"));
    }

    [Fact]
    public async Task A_dead_letter_of_a_waiting_instance_shows_in_the_list_of_its_process_with_the_instance_and_no_trigger()
    {
        var letter = await WakeDeadLetterAsync();

        using var list = await ListOkAsync("maria", "?state=open", "WaitEvent");

        var item = FindItem(list, letter.Id);
        Assert.Equal("event.deliver", item.GetProperty("kind").GetString());
        Assert.Equal(letter.EventId, item.GetProperty("eventId").GetGuid());
        Assert.Equal(letter.InstanceId, item.GetProperty("processInstanceId").GetGuid());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("triggerId").ValueKind);
        Assert.Equal(Limit, item.GetProperty("attempts").GetInt32());
        Assert.Equal(["redeliver", "discard"], Commands(item));

        // The dead letter belongs to the instance's process, not to Decide.
        using var decide = await ListOkAsync("maria", "?state=open");
        Assert.DoesNotContain(decide.RootElement.GetProperty("items").EnumerateArray(), candidate => candidate.GetProperty("id").GetGuid() == letter.Id);
    }

    [Fact]
    public async Task Redelivering_a_dead_letter_of_a_waiting_instance_resumes_it_once()
    {
        var letter = await WakeDeadLetterAsync();
        Assert.Equal("waiting", await InstanceStateAsync(letter.InstanceId));

        using var response = await SendAsync("maria", HttpMethod.Post, $"dead-letters/{letter.Id:D}/redeliver", ReasonBody, "WaitEvent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("redelivered", body.RootElement.GetProperty("state").GetString());
        Assert.Equal(
            1L,
            await fixture.AdminCountAsync(
                Tenant,
                $"SELECT count(*) FROM axis.process_work_items WHERE kind = 'event.deliver' AND process_instance_id = '{letter.InstanceId:D}' AND event_id = '{letter.EventId:D}'"));

        await fixture.RunUntilIdleAsync(Tenant, CancellationToken);

        Assert.Equal("completed", await InstanceStateAsync(letter.InstanceId));
        Assert.Equal(1L, await MatchCountAsync(letter.InstanceId));
        Assert.Equal(
            0L,
            await fixture.AdminCountAsync(Tenant, $"SELECT count(*) FROM axis.delivery_failures WHERE target = '{letter.InstanceId:D}/{letter.EventId:D}'"));
        Assert.Equal(0L, await fixture.AdminCountAsync(Tenant, $"SELECT count(*) FROM axis.process_work_items WHERE event_id = '{letter.EventId:D}'"));

        // A second run changes nothing.
        await fixture.RunUntilIdleAsync(Tenant, CancellationToken);
        Assert.Equal("completed", await InstanceStateAsync(letter.InstanceId));
        Assert.Equal(1L, await MatchCountAsync(letter.InstanceId));
        Assert.Equal("redelivered", await StateAsync(letter.Id));
    }

    [Fact]
    public async Task Discarding_a_dead_letter_of_a_waiting_instance_queues_no_delivery_and_leaves_the_instance_waiting()
    {
        var letter = await WakeDeadLetterAsync();

        using var response = await SendAsync("maria", HttpMethod.Post, $"dead-letters/{letter.Id:D}/discard", ReasonBody, "WaitEvent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("discarded", await StateAsync(letter.Id));
        Assert.Equal(
            0L,
            await fixture.AdminCountAsync(Tenant, $"SELECT count(*) FROM axis.process_work_items WHERE kind = 'event.deliver' AND event_id = '{letter.EventId:D}'"));

        await fixture.RunUntilIdleAsync(Tenant, CancellationToken);

        Assert.Equal("waiting", await InstanceStateAsync(letter.InstanceId));
        Assert.Equal(0L, await MatchCountAsync(letter.InstanceId));
    }

    [Fact]
    public async Task Each_command_writes_an_audit_record_with_the_reason_and_the_user_as_actor()
    {
        var redelivered = await DeadLetterAsync();
        var discarded = await DeadLetterAsync();
        using var redeliver = await SendAsync("maria", HttpMethod.Post, $"dead-letters/{redelivered.Id:D}/redeliver", ReasonBody);
        using var discard = await SendAsync("maria", HttpMethod.Post, $"dead-letters/{discarded.Id:D}/discard", ReasonBody);
        Assert.Equal(HttpStatusCode.OK, redeliver.StatusCode);
        Assert.Equal(HttpStatusCode.OK, discard.StatusCode);

        foreach (var (action, letter) in new[] { ("deadLetter.redelivered", redelivered), ("deadLetter.discarded", discarded) })
        {
            var audit = Assert.Single(await AuditAsync(action, letter.Id));
            Assert.Equal(("maria", null, fixture.Model.Manifest.Id), (audit.Actor, audit.ActorName, audit.ApplicationId));
            Assert.True(audit.NoSubject);
            using var details = JsonDocument.Parse(audit.Details);
            Assert.Equal(
                ["deadLetterId", "eventId", "kind", "occurrenceAt", "reason", "triggerId"],
                details.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal(Reason, details.RootElement.GetProperty("reason").GetString());
            Assert.Equal("event.deliver", details.RootElement.GetProperty("kind").GetString());
            Assert.Equal(letter.TriggerId, details.RootElement.GetProperty("triggerId").GetGuid());
            Assert.Equal(letter.EventId, details.RootElement.GetProperty("eventId").GetGuid());
            Assert.Equal(JsonValueKind.Null, details.RootElement.GetProperty("occurrenceAt").ValueKind);
        }
    }

    [Fact]
    public async Task Redelivering_a_fan_out_dead_letter_whose_trigger_is_not_in_the_active_release_gets_409_and_leaves_the_row_open()
    {
        Assert.True(fixture.Model.TryGetProcess("Decide", out var decide));
        var id = Guid.CreateVersion7();
        var triggerId = Guid.NewGuid();
        await fixture.ExecuteAsync(
            Tenant,
            """
            INSERT INTO axis.dead_letters (id, kind, application_id, process_id, trigger_id, occurrence_at, attempts, last_error, state, created_at)
            VALUES (@id, 'trigger.fanOut', @application, @process, @trigger, '2026-01-01T05:00:00Z', 5, 'System.InvalidOperationException', 'open', now())
            """,
            ("id", id),
            ("application", fixture.Model.Manifest.Id),
            ("process", decide.Id),
            ("trigger", triggerId));

        using var response = await SendAsync("maria", HttpMethod.Post, $"dead-letters/{id:D}/redeliver", ReasonBody);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("The trigger of the dead letter is not in the active release.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("open", await StateAsync(id));
        Assert.Equal(
            0L,
            await fixture.AdminCountAsync(Tenant, $"SELECT count(*) FROM axis.process_work_items WHERE trigger_id = '{triggerId:D}'"));
        Assert.Equal(
            0L,
            await fixture.AdminCountAsync(Tenant, $"SELECT count(*) FROM axis.audit_records WHERE details->>'deadLetterId' = '{id:D}'"));

        // The row can still be discarded.
        using var discard = await SendAsync("maria", HttpMethod.Post, $"dead-letters/{id:D}/discard", ReasonBody);
        Assert.Equal(HttpStatusCode.OK, discard.StatusCode);
        Assert.Equal("discarded", await StateAsync(id));
    }

    [Fact]
    public async Task A_webhook_dead_letter_is_listed_with_its_webhook_for_a_user_with_inspect_and_a_user_without_inspect_gets_403_on_every_route()
    {
        Assert.True(fixture.Model.TryGetProcess("Decide", out var decide));
        var id = Guid.CreateVersion7();
        var eventId = Guid.NewGuid();
        var webhookId = Guid.NewGuid();
        await fixture.ExecuteAsync(
            Tenant,
            """
            INSERT INTO axis.dead_letters (id, kind, application_id, process_id, process_instance_id, event_id, webhook_id, attempts, last_error, state, created_at)
            VALUES (@id, 'webhook.deliver', @application, @process, @instance, @event, @webhook, 5, 'HTTP 500', 'open', now())
            """,
            ("id", id),
            ("application", fixture.Model.Manifest.Id),
            ("process", decide.Id),
            ("instance", Guid.NewGuid()),
            ("event", eventId),
            ("webhook", webhookId));

        using var maria = await ListOkAsync("maria", "?state=open");
        var item = FindItem(maria, id);
        Assert.Equal("webhook.deliver", item.GetProperty("kind").GetString());
        Assert.Equal(eventId, item.GetProperty("eventId").GetGuid());
        Assert.Equal(webhookId, item.GetProperty("webhookId").GetGuid());
        Assert.Equal("HTTP 500", item.GetProperty("lastError").GetString());
        Assert.Equal(["redeliver", "discard"], Commands(item));

        using var denied = await SendAsync("otto", HttpMethod.Get, "dead-letters", body: null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        foreach (var command in new[] { "redeliver", "discard" })
        {
            using var forbidden = await SendAsync("otto", HttpMethod.Post, $"dead-letters/{id:D}/{command}", ReasonBody);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        Assert.Equal("open", await StateAsync(id));
        Assert.Equal(
            0L,
            await fixture.AdminCountAsync(Tenant, $"SELECT count(*) FROM axis.process_work_items WHERE kind = 'webhook.deliver' AND event_id = '{eventId:D}'"));
    }

    private static string ReasonBody => JsonSerializer.Serialize(new { reason = Reason });

    /// <summary>
    /// Raises <c>RequestNoted</c> for a new request with a ratio over a million, after a PostgreSQL trigger has
    /// made the start of <c>Decide</c> for that record fail, and runs the delivery until it is a dead letter. The
    /// PostgreSQL trigger is dropped before this returns, so a redelivery can start <c>Decide</c>.
    /// </summary>
    private async Task<Letter> DeadLetterAsync()
    {
        Assert.True(fixture.Model.TryGetProcess("Decide", out var decide));
        var decideTrigger = Assert.Single(fixture.Model.Triggers, candidate => candidate.Name == "DecideHugeNotedRequests");
        var approveTrigger = Assert.Single(fixture.Model.Triggers, candidate => candidate.Name == "ApproveNotedRequests");
        var subjectId = await fixture.InsertRequestAsync(Tenant, null, divisor: 4, amount: 4_123_456.75m);
        var name = $"test_injected_failure_{subjectId:N}";
        await fixture.ScalarAsync(
            Tenant,
            $$"""
            CREATE FUNCTION axis.{{name}}() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW.process_id = '{{decide.Id:D}}'::uuid AND NEW.subject_id = '{{subjectId:D}}'::uuid THEN
                    RAISE EXCEPTION 'refused';
                END IF;
                RETURN NEW;
            END
            $body$;
            CREATE TRIGGER {{name}} BEFORE INSERT ON axis.process_instances
                FOR EACH ROW EXECUTE FUNCTION axis.{{name}}();
            """);
        try
        {
            var announceId = await fixture.StartAsync(Tenant, "Announce", subjectId);
            await fixture.RunUntilIdleAsync(Tenant, CancellationToken);
            var eventId = (Guid)(await fixture.ScalarAsync(Tenant, "SELECT id FROM axis.events WHERE process_instance_id = @id", ("id", announceId)))!;

            // The first run failed once. Each later run follows a backoff that the test skips.
            for (var failures = 1; failures < Limit; failures++)
            {
                await fixture.ScalarAsync(Tenant, "UPDATE axis.process_work_items SET due_at = now() WHERE event_id = @id", ("id", eventId));
                await fixture.RunUntilIdleAsync(Tenant, CancellationToken);
            }

            var id = (Guid)(await fixture.ScalarAsync(Tenant, "SELECT id FROM axis.dead_letters WHERE event_id = @id", ("id", eventId)))!;
            return new Letter(id, subjectId, eventId, decideTrigger.Id, approveTrigger.Id);
        }
        finally
        {
            await fixture.ScalarAsync(
                Tenant,
                $"""
                DROP TRIGGER {name} ON axis.process_instances;
                DROP FUNCTION axis.{name}();
                """);
        }
    }

    /// <summary>
    /// Starts a <c>WaitEvent</c> instance for a new request with a ratio over a million, makes a PostgreSQL trigger
    /// refuse its match row, raises <c>RequestNoted</c> and runs the delivery until the wake of that instance is a
    /// dead letter. The PostgreSQL trigger is dropped before this returns, so a redelivery can wake the instance.
    /// </summary>
    private async Task<WakeLetter> WakeDeadLetterAsync()
    {
        var subjectId = await fixture.InsertRequestAsync(Tenant, null, divisor: 4, amount: 4_123_456.75m);
        var instanceId = await fixture.StartAsync(Tenant, "WaitEvent", subjectId);
        await fixture.RunUntilIdleAsync(Tenant, CancellationToken);
        Assert.Equal("waiting", await InstanceStateAsync(instanceId));
        var name = $"test_injected_wake_failure_{instanceId:N}";
        await fixture.ScalarAsync(
            Tenant,
            $$"""
            CREATE FUNCTION axis.{{name}}() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW.process_instance_id = '{{instanceId:D}}'::uuid THEN
                    RAISE EXCEPTION 'refused';
                END IF;
                RETURN NEW;
            END
            $body$;
            CREATE TRIGGER {{name}} BEFORE INSERT ON axis.process_event_matches
                FOR EACH ROW EXECUTE FUNCTION axis.{{name}}();
            """);
        try
        {
            var announceId = await fixture.StartAsync(Tenant, "Announce", subjectId);
            await fixture.RunUntilIdleAsync(Tenant, CancellationToken);
            var eventId = (Guid)(await fixture.ScalarAsync(Tenant, "SELECT id FROM axis.events WHERE process_instance_id = @id", ("id", announceId)))!;

            // The first run failed once. Each later run follows a backoff that the test skips.
            for (var failures = 1; failures < Limit; failures++)
            {
                await fixture.ScalarAsync(Tenant, "UPDATE axis.process_work_items SET due_at = now() WHERE event_id = @id", ("id", eventId));
                await fixture.RunUntilIdleAsync(Tenant, CancellationToken);
            }

            var id = (Guid)(await fixture.ScalarAsync(Tenant, "SELECT id FROM axis.dead_letters WHERE event_id = @id", ("id", eventId)))!;
            return new WakeLetter(id, eventId, instanceId);
        }
        finally
        {
            await fixture.ScalarAsync(
                Tenant,
                $"""
                DROP TRIGGER {name} ON axis.process_event_matches;
                DROP FUNCTION axis.{name}();
                """);
        }
    }

    private async Task<string> InstanceStateAsync(Guid instanceId) =>
        (string)(await fixture.ScalarAsync(Tenant, "SELECT state FROM axis.process_instances WHERE id = @id", ("id", instanceId)))!;

    private async Task<long> MatchCountAsync(Guid instanceId) =>
        await fixture.AdminCountAsync(Tenant, $"SELECT count(*) FROM axis.process_event_matches WHERE process_instance_id = '{instanceId:D}'");

    private async Task<JsonDocument> ListOkAsync(string user, string query, string process = "Decide")
    {
        using var response = await SendAsync(user, HttpMethod.Get, $"dead-letters{query}", body: null, process);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static JsonElement FindItem(JsonDocument list, Guid id) =>
        Assert.Single(list.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);

    private static string[] Commands(JsonElement item) =>
        [.. item.GetProperty("commands").EnumerateArray().Select(command => command.GetString()!)];

    private async Task<HttpResponseMessage> SendAsync(string user, HttpMethod method, string path, string? body, string process = "Decide")
    {
        using var client = fixture.CreateClient().SignedInAs(user);
        using var request = RecordApiFixture.Request(method, $"/api/apps/StepApp/processes/{process}/{path}", Host(Tenant), body);
        return await client.SendAsync(request, CancellationToken);
    }

    private async Task<string> StateAsync(Guid id) =>
        (string)(await fixture.ScalarAsync(Tenant, "SELECT state FROM axis.dead_letters WHERE id = @id", ("id", id)))!;

    private async Task<IReadOnlyList<(string Actor, string? ActorName, Guid ApplicationId, bool NoSubject, string Details)>> AuditAsync(string action, Guid deadLetterId)
    {
        await using var connection = await fixture.OpenConnectionAsync(Tenant);
        await using var command = new NpgsqlCommand(
            """
            SELECT actor, actor_name, application_id, entity_id IS NULL AND record_id IS NULL AND process_instance_id IS NULL, details::text
            FROM axis.audit_records WHERE action = @action AND details->>'deadLetterId' = @id
            """,
            connection);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("id", deadLetterId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<(string, string?, Guid, bool, string)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetGuid(2), reader.GetBoolean(3), reader.GetString(4)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<Guid>> InstancesAsync(string process, Guid subjectId)
    {
        Assert.True(fixture.Model.TryGetProcess(process, out var model));
        await using var connection = await fixture.OpenConnectionAsync(Tenant);
        await using var command = new NpgsqlCommand(
            "SELECT id FROM axis.process_instances WHERE process_id = @process AND subject_id = @subject ORDER BY id",
            connection);
        command.Parameters.AddWithValue("process", model.Id);
        command.Parameters.AddWithValue("subject", subjectId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<Guid>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(reader.GetGuid(0));
        }

        return rows;
    }

    private async Task<(string Outcome, Guid? InstanceId)> ReceiptAsync(Guid triggerId, Letter letter)
    {
        await using var connection = await fixture.OpenConnectionAsync(Tenant);
        await using var command = new NpgsqlCommand(
            "SELECT outcome, process_instance_id FROM axis.trigger_starts WHERE trigger_id = @trigger AND source = @source AND subject_id = @subject",
            connection);
        command.Parameters.AddWithValue("trigger", triggerId);
        command.Parameters.AddWithValue("source", letter.EventId.ToString("D"));
        command.Parameters.AddWithValue("subject", letter.SubjectId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }

    private sealed record WakeLetter(Guid Id, Guid EventId, Guid InstanceId);

    private sealed record Letter(Guid Id, Guid SubjectId, Guid EventId, Guid TriggerId, Guid ApproveTriggerId);
}
