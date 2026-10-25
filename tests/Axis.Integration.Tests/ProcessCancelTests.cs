using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using Npgsql;
using static Axis.Integration.Tests.Polling;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// An operator with a <c>cancel</c> grant stops a running or waiting purchase request approval,
/// through the HTTP API. The sample gives the grant to the finance reviewers, here <c>dung</c>.
/// <c>anna</c> starts the approvals and <c>chau</c> is the manager. Each test leaves no due work
/// item behind, so the next one finds only its own.
/// </summary>
public sealed class ProcessCancelTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private const string Reason = """{ "reason": "Stuck for a week" }""";

    private static readonly string _requests = EntityNaming.QualifiedTable(EntityNaming.Table(Guid.Parse("d9ccbf89-5004-4d52-8885-2ee377b7caa5")));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cancelling_an_instance_waiting_for_the_manager_closes_its_task_and_lets_the_record_start_again()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var subject = await CreateRequestAsync(anna, 500);
        var instance = await StartAsync(anna, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var task = await OpenTaskAsync(instance);
        Assert.Contains(task, await InboxAsync(chau));

        using (var response = await CancelAsync(dung, instance, Reason))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal(instance, body.RootElement.GetProperty("id").GetGuid());
            Assert.Equal("PurchaseRequestApproval", body.RootElement.GetProperty("process").GetString());
            Assert.Equal(subject, body.RootElement.GetProperty("subjectId").GetGuid());
            Assert.Equal("cancelled", body.RootElement.GetProperty("state").GetString());
        }

        Assert.Equal("cancelled", await fixture.ScalarAsync("SELECT state FROM axis.process_instances WHERE id = @id", ("id", instance)));
        Assert.NotNull(await fixture.ScalarAsync("SELECT ended_at FROM axis.process_instances WHERE id = @id", ("id", instance)));
        Assert.Equal("cancelled", await fixture.ScalarAsync("SELECT state FROM axis.process_tasks WHERE id = @id", ("id", task)));
        Assert.Null(await fixture.ScalarAsync("SELECT outcome FROM axis.process_tasks WHERE id = @id", ("id", task)));
        Assert.DoesNotContain(task, await InboxAsync(chau));
        Assert.Equal(0L, await WorkItemsAsync(instance));

        using (var completed = await CompleteAsync(chau, task, """{ "outcome": "approve" }"""))
        {
            Assert.Equal(HttpStatusCode.Conflict, completed.StatusCode);
        }

        // The cancel does not undo the submit, so the author takes the request back before starting it again.
        await SetStatusAsync(subject, "returned");
        var second = await StartAsync(anna, subject);
        Assert.NotEqual(instance, second);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var rows = await fixture.InstancesAsync(subject);
        Assert.Equal(["cancelled", "waiting"], rows.Select(row => row.State));
    }

    [Fact]
    public async Task Cancelling_an_instance_deletes_the_deadline_item_of_its_open_task()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var subject = await CreateRequestAsync(anna, 500);
        var instance = await StartAsync(anna, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var task = await OpenTaskAsync(instance);

        // The sample has no deadline, so the item is the one a task step with a deadline would queue.
        await fixture.ScalarAsync(
            "INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, process_instance_id, task_id) VALUES (gen_random_uuid(), @tenant, 'task.deadline', now() + interval '1 day', @instance, @task)",
            ("tenant", Tenant),
            ("instance", instance),
            ("task", task));
        Assert.Equal(
            1L,
            await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE task_id = @id AND kind = 'task.deadline'", ("id", task)));

        using (var response = await CancelAsync(dung, instance, Reason))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal("cancelled", await fixture.ScalarAsync("SELECT state FROM axis.process_tasks WHERE id = @id", ("id", task)));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE task_id = @id", ("id", task)));
    }

    [Fact]
    public async Task A_cancel_writes_one_audit_record_with_the_user_and_the_reason_and_a_history_row()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var subject = await CreateRequestAsync(anna, 500);
        var instance = await StartAsync(anna, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var task = await OpenTaskAsync(instance);

        using (var response = await CancelAsync(dung, instance, Reason))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.cancelled'", ("id", instance)));
        Assert.Equal(
            ("dung", "Dung Finance Reviewer", subject),
            await AuditActorAsync(instance));
        using var details = JsonDocument.Parse((string)(await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.cancelled'", ("id", instance)))!);
        Assert.Equal(["step", "reason", "taskIds", "operationIds"], details.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("managerApproval", details.RootElement.GetProperty("step").GetString());
        Assert.Equal("Stuck for a week", details.RootElement.GetProperty("reason").GetString());
        Assert.Equal([task], details.RootElement.GetProperty("taskIds").EnumerateArray().Select(id => id.GetGuid()));
        Assert.Empty(details.RootElement.GetProperty("operationIds").EnumerateArray());

        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id AND output->>'command' = 'cancel' AND step = 'managerApproval'",
                ("id", instance)));
        Assert.Equal(
            "{\"command\": \"cancel\"}",
            await fixture.ScalarAsync(
                "SELECT output::text FROM axis.process_step_history WHERE process_instance_id = @id AND output->>'command' = 'cancel'", ("id", instance)));
        Assert.Equal(
            0L,
            await fixture.CountAsync(
                "SELECT count(*) - count(DISTINCT revision) FROM axis.process_step_history WHERE process_instance_id = @id", ("id", instance)));
    }

    [Fact]
    public async Task Cancelling_an_instance_with_a_pending_call_cancels_the_call_and_sends_nothing()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var (subject, instance) = await StageCallAsync(anna);
        var call = await CallAsync(instance);
        Assert.Equal("pending", await CallStateAsync(call));
        Assert.Equal(1L, await WorkItemsAsync(instance));

        using (var response = await CancelAsync(dung, instance, Reason))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal("cancelled", await CallStateAsync(call));
        Assert.Null(await fixture.ScalarAsync("SELECT input FROM axis.connector_calls WHERE id = @id", ("id", call)));
        Assert.Equal(0L, await WorkItemsAsync(instance));
        using var details = JsonDocument.Parse((string)(await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.cancelled'", ("id", instance)))!);
        Assert.Equal([call], details.RootElement.GetProperty("operationIds").EnumerateArray().Select(id => id.GetGuid()));

        await fixture.RunUntilIdleAsync(CancellationToken);
        var (number, orderNumber) = await NumbersAsync(anna, subject);
        Assert.Empty(await fixture.OrdersAsync(number));
        Assert.Null(orderNumber);
        Assert.Equal("cancelled", await StateAsync(instance));
    }

    [Fact]
    public async Task Cancelling_an_instance_whose_call_is_unknown_is_refused_and_changes_nothing()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var (_, instance) = await StageCallAsync(anna);
        var call = await CallAsync(instance);
        await fixture.ScalarAsync("UPDATE axis.connector_calls SET state = 'unknown' WHERE id = @id", ("id", call));
        var historyBefore = await fixture.CountAsync("SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id", ("id", instance));

        try
        {
            using var response = await CancelAsync(dung, instance, Reason);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
            Assert.Equal(call, problem.RootElement.GetProperty("callId").GetGuid());

            Assert.Equal("waiting", await StateAsync(instance));
            Assert.Equal("unknown", await CallStateAsync(call));
            Assert.NotNull(await fixture.ScalarAsync("SELECT input FROM axis.connector_calls WHERE id = @id", ("id", call)));
            Assert.Equal(1L, await WorkItemsAsync(instance));
            Assert.Equal(
                historyBefore,
                await fixture.CountAsync("SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id", ("id", instance)));
            Assert.Equal(
                0L,
                await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.cancelled'", ("id", instance)));
        }
        finally
        {
            // The call would wait for a reconcile that this test does not want to run.
            await fixture.ScalarAsync("DELETE FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instance));
        }
    }

    [Fact]
    public async Task A_step_a_worker_had_loaded_commits_nothing_after_the_cancel()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var subject = await CreateRequestAsync(anna, 500);
        var instance = await StartAsync(anna, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        using (var approved = await CompleteAsync(chau, await OpenTaskAsync(instance), """{ "outcome": "approve" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        // Only the decision runs, so the markApproved item is the next one due.
        Assert.Equal(1, await fixture.RunStepsUntilIdleAsync(CancellationToken, limit: 1));
        Assert.Equal("running", await StateAsync(instance));
        var historyBefore = await fixture.CountAsync("SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id", ("id", instance));

        await using var holder = await fixture.OpenConnectionAsync();
        await using var transaction = await holder.BeginTransactionAsync(CancellationToken);
        await using (var hold = new NpgsqlCommand(
            $"SELECT 1 FROM {_requests} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id FOR UPDATE", holder, transaction))
        {
            hold.Parameters.AddWithValue("id", subject);
            await hold.ExecuteScalarAsync(CancellationToken);
        }

        var run = Task.Run(() => fixture.RunUntilIdleAsync(CancellationToken), CancellationToken);
        await WaitUntilAsync(async () => run.IsCompleted || await fixture.AdminCountAsync(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'") == 1, "The run did not wait on the lock.");
        Assert.False(run.IsCompleted);

        using (var response = await CancelAsync(dung, instance, Reason))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await transaction.RollbackAsync(CancellationToken);
        await run;

        Assert.Equal("submitted", (await ReadStatusAsync(anna, subject)));
        Assert.Equal("cancelled", await StateAsync(instance));

        // The only new history row is the cancel's own, recorded at the step the instance was on.
        Assert.Equal(
            0L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id AND step = 'markApproved' AND output->>'command' IS DISTINCT FROM 'cancel'",
                ("id", instance)));
        Assert.Equal(
            historyBefore + 1,
            await fixture.CountAsync("SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id", ("id", instance)));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.connector_calls WHERE process_instance_id = @id", ("id", instance)));
        Assert.Equal(0L, await WorkItemsAsync(instance));
    }

    [Fact]
    public async Task A_user_without_a_cancel_grant_gets_403_and_a_completed_instance_gets_409()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var subject = await CreateRequestAsync(anna, 500);
        var instance = await StartAsync(anna, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);

        using (var denied = await CancelAsync(anna, instance, Reason))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using (var managerDenied = await CancelAsync(chau, instance, Reason))
        {
            Assert.Equal(HttpStatusCode.Forbidden, managerDenied.StatusCode);
        }

        Assert.Equal("waiting", await StateAsync(instance));

        using (var rejected = await CompleteAsync(chau, await OpenTaskAsync(instance), """{ "outcome": "reject" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        }

        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("completed", await StateAsync(instance));
        using var response = await CancelAsync(dung, instance, Reason);
        using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
        Assert.Equal("The instance is not running or waiting.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("completed", await StateAsync(instance));
    }

    [Fact]
    public async Task A_cancel_that_is_not_well_formed_or_names_no_instance_writes_nothing()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var subject = await CreateRequestAsync(anna, 500);
        var instance = await StartAsync(anna, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);

        foreach (var (body, key, message) in new[]
        {
            ("{}", "/reason", "Required."),
            ("""{ "reason": "  " }""", "/reason", "Required."),
            ($$"""{ "reason": "{{new string('x', 501)}}" }""", "/reason", "Must be at most 500 characters."),
            ("""{ "reason": "Because", "note": 1 }""", "/note", "Unknown property."),
            ("""{ "reason": "A", "reason": "B" }""", "/reason", "Duplicate property."),
            ("[]", "", "Must be a JSON object."),
            ("{", "", "Must be a JSON object."),
        })
        {
            using var response = await CancelAsync(dung, instance, body);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
            var errors = problem.RootElement.GetProperty("errors");
            Assert.Equal([key], errors.EnumerateObject().Select(error => error.Name));
            Assert.Equal([message], errors.GetProperty(key).EnumerateArray().Select(error => error.GetString()));
        }

        using (var unknown = await CancelAsync(dung, Guid.NewGuid(), Reason))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        // Only the hyphenated form names an instance.
        using (var request = RecordApiFixture.Request(HttpMethod.Post, $"{App}/processes/PurchaseRequestApproval/instances/{instance:N}/cancel", Host, Reason))
        {
            using var response = await dung.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using (var request = RecordApiFixture.Request(HttpMethod.Post, $"{App}/processes/PurchaseRequestApproval/instances/{instance:D}/cancel", Host, Reason))
        {
            request.Content = new StringContent(Reason, System.Text.Encoding.UTF8, "text/plain");
            using var response = await dung.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        }

        Assert.Equal("waiting", await StateAsync(instance));
        Assert.Equal(
            0L,
            await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.cancelled'", ("id", instance)));
    }

    // Starts an approval and runs its steps, up to the staged purchase order call. The call is not sent.
    private async Task<(Guid Subject, Guid Instance)> StageCallAsync(HttpClient anna)
    {
        using var chau = fixture.CreateClient().SignedInAs("chau");
        var subject = await CreateRequestAsync(anna, 500);
        var instance = await StartAsync(anna, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        using (var approved = await CompleteAsync(chau, await OpenTaskAsync(instance), """{ "outcome": "approve" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        await fixture.RunStepsUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", await StateAsync(instance));
        return (subject, instance);
    }

    private async Task<Guid> OpenTaskAsync(Guid instance) =>
        (Guid)(await fixture.ScalarAsync(
            "SELECT id FROM axis.process_tasks WHERE process_instance_id = @id AND state = 'open'", ("id", instance)))!;

    private async Task<Guid> CallAsync(Guid instance) =>
        (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", ("id", instance)))!;

    private async Task<string?> CallStateAsync(Guid call) =>
        (string?)await fixture.ScalarAsync("SELECT state FROM axis.connector_calls WHERE id = @id", ("id", call));

    private async Task<string?> StateAsync(Guid instance) =>
        (string?)await fixture.ScalarAsync("SELECT state FROM axis.process_instances WHERE id = @id", ("id", instance));

    private Task<long> WorkItemsAsync(Guid instance) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instance));

    private async Task<(string? Actor, string? Name, Guid? Record)> AuditActorAsync(Guid instance)
    {
        await using var connection = await fixture.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT actor, actor_name, record_id FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.cancelled'", connection);
        command.Parameters.AddWithValue("id", instance);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.GetString(1), reader.GetGuid(2));
    }

    private async Task SetStatusAsync(Guid subject, string status)
    {
        await using var connection = await fixture.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE {_requests}
            SET {EntityNaming.Quote(EntityNaming.Column("status"))} = @status, {EntityNaming.Quote(EntityNaming.VersionColumn)} = {EntityNaming.Quote(EntityNaming.VersionColumn)} + 1
            WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id
            """,
            connection);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("id", subject);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(CancellationToken));
    }

    private static async Task<IReadOnlyList<Guid>> InboxAsync(HttpClient client)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{App}/tasks?pageSize=100", Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return [.. body.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
    }

    private static async Task<string?> ReadStatusAsync(HttpClient client, Guid subject)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{App}/entities/PurchaseRequest/records/{subject:D}", Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return body.RootElement.GetProperty("values").GetProperty("status").GetString();
    }
}
