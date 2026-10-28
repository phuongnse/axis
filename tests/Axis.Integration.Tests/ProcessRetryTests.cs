using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Tests;
using Npgsql;
using static Axis.Integration.Tests.Polling;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// An operator with a <c>retry</c> grant sends a failed purchase request approval back to its
/// failed step, through the HTTP API. The sample gives the grant to the finance reviewers, here
/// <c>dung</c>. <c>requester</c> starts the approvals and <c>chau</c> is the manager of Operations.
/// A step fails when the request's department has no manager, and a retry works once the
/// department is Operations. Each test leaves no due work item behind, so the next one finds
/// only its own.
/// </summary>
public sealed class ProcessRetryTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private const string Reason = """{ "reason": "The department has a manager now" }""";

    private const string AlreadyRunningTitle = "A running or waiting instance of this process already exists for this record.";

    private static readonly Guid _globex = Guid.Parse("d97fc520-bc90-442b-8e4d-8d2b94b77c65");

    private static readonly string _requests = EntityNaming.QualifiedTable(EntityNaming.Table(Guid.Parse("d9ccbf89-5004-4d52-8885-2ee377b7caa5")));

    private static readonly string _departments = EntityNaming.QualifiedTable(EntityNaming.Table(Guid.Parse("ebe6bc20-4772-4d6c-839b-65d426ebeeca")));

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_step_that_failed_on_bad_data_runs_again_after_the_record_is_fixed()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var (subject, instance) = await FailAsync(requester);
        var releaseBefore = await ScalarAsync("SELECT release_id FROM axis.process_instances WHERE id = @id", instance);
        Assert.NotNull(await ScalarAsync("SELECT ended_at FROM axis.process_instances WHERE id = @id", instance));
        await SetDepartmentAsync(subject, Operations);

        using (var response = await RetryAsync(dung, instance, Reason))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal(instance, body.RootElement.GetProperty("id").GetGuid());
            Assert.Equal("PurchaseRequestApproval", body.RootElement.GetProperty("process").GetString());
            Assert.Equal(subject, body.RootElement.GetProperty("subjectId").GetGuid());
            Assert.Equal(releaseBefore, body.RootElement.GetProperty("releaseId").GetGuid());
            Assert.Equal("running", body.RootElement.GetProperty("state").GetString());
        }

        Assert.Equal("running", await StateAsync(instance));
        Assert.Equal("managerApproval", await ScalarAsync("SELECT step FROM axis.process_instances WHERE id = @id", instance));
        Assert.Null(await ScalarAsync("SELECT ended_at FROM axis.process_instances WHERE id = @id", instance));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id AND kind = 'process.step'", ("id", instance)));

        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", await StateAsync(instance));
        var task = await OpenTaskAsync(instance);
        Assert.Contains(task, await InboxAsync(chau));

        // The step ran with the department as it is now, so its manager decides and the instance moves on.
        using var approved = await CompleteAsync(chau, task, """{ "outcome": "reject" }""");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("completed", await StateAsync(instance));
    }

    [Fact]
    public async Task Retrying_while_another_instance_waits_for_the_record_is_409_and_changes_nothing()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var (subject, instance) = await FailAsync(requester);
        await SetDepartmentAsync(subject, Operations);
        await SetStatusAsync(subject, "returned");
        var second = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", await StateAsync(second));
        var revision = await ScalarAsync("SELECT revision FROM axis.process_instances WHERE id = @id", instance);
        var history = await HistoryAsync(instance);

        using (var response = await RetryAsync(dung, instance, Reason))
        {
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
            Assert.Equal(AlreadyRunningTitle, problem.RootElement.GetProperty("title").GetString());
        }

        await AssertUnchangedAsync(instance, revision, history);
        Assert.Equal("waiting", await StateAsync(second));
    }

    [Fact]
    public async Task A_retry_that_loses_the_race_on_the_unique_index_is_409_and_changes_nothing()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var (subject, instance) = await FailAsync(requester);
        await SetDepartmentAsync(subject, Operations);
        var revision = await ScalarAsync("SELECT revision FROM axis.process_instances WHERE id = @id", instance);
        var history = await HistoryAsync(instance);
        var rival = Guid.NewGuid();

        await using var holder = await fixture.OpenConnectionAsync();
        await using var transaction = await holder.BeginTransactionAsync(CancellationToken);
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO axis.process_instances (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step)
            SELECT @rival, application_id, process_id, subject_entity_id, subject_id, release_id, 'running', 1, step
            FROM axis.process_instances WHERE id = @id
            """,
            holder,
            transaction))
        {
            insert.Parameters.AddWithValue("rival", rival);
            insert.Parameters.AddWithValue("id", instance);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync(CancellationToken));
        }

        try
        {
            // The retry does not see the uncommitted row, so it waits on the unique index.
            var retry = Task.Run(async () =>
            {
                using var response = await RetryAsync(dung, instance, Reason);
                using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Conflict);
                return problem.RootElement.GetProperty("title").GetString();
            }, CancellationToken);
            await WaitUntilAsync(async () => retry.IsCompleted || await fixture.AdminCountAsync(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'") == 1, "The retry did not wait on the lock.");
            Assert.False(retry.IsCompleted);

            await transaction.CommitAsync(CancellationToken);
            Assert.Equal(AlreadyRunningTitle, await retry);
            await AssertUnchangedAsync(instance, revision, history);
        }
        finally
        {
            await fixture.AdminCountAsync($"WITH removed AS (DELETE FROM axis.process_instances WHERE id = '{rival:D}' RETURNING 1) SELECT count(*) FROM removed");
        }
    }

    [Fact]
    public async Task A_user_without_a_retry_grant_gets_403_and_an_instance_that_is_not_failed_gets_409()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var (_, failed) = await FailAsync(requester);
        using (var denied = await RetryAsync(requester, failed, Reason))
        {
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using (var managerDenied = await RetryAsync(chau, failed, Reason))
        {
            Assert.Equal(HttpStatusCode.Forbidden, managerDenied.StatusCode);
        }

        Assert.Equal("failed", await StateAsync(failed));
        Assert.Equal(0L, await RetriedAsync(failed));

        var subject = await CreateRequestAsync(requester, 500);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", await StateAsync(instance));
        using (var waiting = await RetryAsync(dung, instance, Reason))
        {
            using var problem = await RecordApiFixture.ReadProblemAsync(waiting, HttpStatusCode.Conflict);
            Assert.Equal("The instance is not failed.", problem.RootElement.GetProperty("title").GetString());
        }

        using (var rejected = await CompleteAsync(chau, await OpenTaskAsync(instance), """{ "outcome": "reject" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        }

        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("completed", await StateAsync(instance));
        using (var completed = await RetryAsync(dung, instance, Reason))
        {
            using var problem = await RecordApiFixture.ReadProblemAsync(completed, HttpStatusCode.Conflict);
            Assert.Equal("The instance is not failed.", problem.RootElement.GetProperty("title").GetString());
        }

        Assert.Equal("completed", await StateAsync(instance));
        Assert.Equal(0L, await RetriedAsync(instance));
    }

    [Fact]
    public async Task A_retry_writes_one_audit_record_with_the_user_and_the_reason_and_a_history_row()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var (subject, instance) = await FailAsync(requester);
        await SetDepartmentAsync(subject, Operations);

        using (var response = await RetryAsync(dung, instance, Reason))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal(1L, await RetriedAsync(instance));
        await using (var connection = await fixture.OpenConnectionAsync())
        {
            await using var command = new NpgsqlCommand(
                "SELECT actor, actor_name, record_id FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.retried'", connection);
            command.Parameters.AddWithValue("id", instance);
            await using var reader = await command.ExecuteReaderAsync(CancellationToken);
            Assert.True(await reader.ReadAsync(CancellationToken));
            Assert.Equal(("dung", "Dung Finance Reviewer", subject), (reader.GetString(0), reader.GetString(1), reader.GetGuid(2)));
        }

        using var details = JsonDocument.Parse((string)(await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.retried'", ("id", instance)))!);
        Assert.Equal(["step", "reason"], details.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal("managerApproval", details.RootElement.GetProperty("step").GetString());
        Assert.Equal("The department has a manager now", details.RootElement.GetProperty("reason").GetString());

        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id AND output->>'command' = 'retry' AND step = 'managerApproval'",
                ("id", instance)));
        Assert.Equal(
            "{\"command\": \"retry\"}",
            await fixture.ScalarAsync(
                "SELECT output::text FROM axis.process_step_history WHERE process_instance_id = @id AND output->>'command' = 'retry'", ("id", instance)));
        Assert.Equal(
            0L,
            await fixture.CountAsync(
                "SELECT count(*) - count(DISTINCT revision) FROM axis.process_step_history WHERE process_instance_id = @id", ("id", instance)));
    }

    [Fact]
    public async Task A_retry_that_is_not_well_formed_or_names_no_instance_writes_nothing()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var (subject, instance) = await FailAsync(requester);
        await SetDepartmentAsync(subject, Operations);

        foreach (var (body, key, message) in new[]
        {
            ("{}", "/reason", "Required."),
            ($$"""{ "reason": "{{new string('x', 501)}}" }""", "/reason", "Must be at most 500 characters."),
            ("""{ "reason": "Because", "note": 1 }""", "/note", "Unknown property."),
            ("[]", "", "Must be a JSON object."),
        })
        {
            using var response = await RetryAsync(dung, instance, body);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
            var errors = problem.RootElement.GetProperty("errors");
            Assert.Equal([key], errors.EnumerateObject().Select(error => error.Name));
            Assert.Equal([message], errors.GetProperty(key).EnumerateArray().Select(error => error.GetString()));
        }

        using (var unknown = await RetryAsync(dung, Guid.NewGuid(), Reason))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        Assert.Equal("failed", await StateAsync(instance));
        Assert.Equal(0L, await RetriedAsync(instance));
    }

    [Fact]
    public async Task A_retried_purchase_order_call_is_sent_again_with_the_same_key_and_ends_with_one_order()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var (_, number, instance) = await FailAtPurchaseOrderAsync(requester);
        var call = (Guid)(await ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", instance))!;
        await StubAsync($"/test/available-request/{number}");

        using (var response = await RetryAsync(dung, instance, Reason))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await RunToEndAsync(instance);

        Assert.Equal("completed", await StateAsync(instance));
        Assert.Equal(call, await ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", instance));
        Assert.Equal("succeeded:1", await CallAsync(call));
        Assert.Null(await fixture.ScalarAsync("SELECT last_error FROM axis.connector_calls WHERE id = @id", ("id", call)));
        var order = Assert.Single(await OrdersAsync(number));
        Assert.Equal(call.ToString("D"), order.Key);
        Assert.NotNull((await NumbersAsync(requester, await SubjectAsync(instance))).PurchaseOrderNumber);

        // The first run made five attempts that answered 503. The retry's attempt carries on with number 6.
        Assert.Equal(
            "1:retryable:503,2:retryable:503,3:retryable:503,4:retryable:503,5:retryable:503,6:succeeded:201",
            await fixture.ScalarAsync(
                """
                SELECT string_agg(number || ':' || outcome || ':' || http_status, ',' ORDER BY number)
                FROM axis.process_call_attempts WHERE call_id = @id AND kind = 'attempt'
                """,
                ("id", call)));

        // The step staged the call twice under the same operation id.
        Assert.Equal(
            2L,
            await fixture.CountAsync(
                """
                SELECT count(*) FROM axis.process_step_history
                WHERE process_instance_id = @id AND step = 'createPurchaseOrder' AND output->>'state' = 'waiting' AND output->>'operationId' = @call
                """,
                ("id", instance),
                ("call", call.ToString("D"))));
        Assert.Equal(2L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'connector.callStaged'", ("id", instance)));
    }

    [Fact]
    public async Task A_retried_purchase_order_call_sends_the_input_evaluated_from_the_record_as_it_is_now()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var dung = fixture.CreateClient().SignedInAs("dung");
        var (subject, number, instance) = await FailAtPurchaseOrderAsync(requester);
        var call = (Guid)(await ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", instance))!;
        Assert.NotEqual("GLOBEX", await fixture.ScalarAsync("SELECT input->>'supplierCode' FROM axis.connector_calls WHERE id = @id", ("id", call)));
        await UpdateRequestAsync(subject, "supplier", _globex);
        await StubAsync($"/test/available-request/{number}");

        using (var response = await RetryAsync(dung, instance, Reason))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await RunToEndAsync(instance);

        Assert.Equal("completed", await StateAsync(instance));
        Assert.Equal("GLOBEX", await fixture.ScalarAsync("SELECT input->>'supplierCode' FROM axis.connector_calls WHERE id = @id", ("id", call)));
        Assert.Equal("GLOBEX", Assert.Single(await OrdersAsync(number)).SupplierCode);
    }

    // Starts an approval for a request of 500 whose purchasing system is unavailable, approves it as chau,
    // and runs it until the createPurchaseOrder call has used up its five attempts and the instance failed.
    private async Task<(Guid Subject, string Number, Guid Instance)> FailAtPurchaseOrderAsync(HttpClient requester)
    {
        using var chau = fixture.CreateClient().SignedInAs("chau");
        var subject = await CreateRequestAsync(requester, 500);
        var (number, _) = await NumbersAsync(requester, subject);
        await StubAsync($"/test/unavailable-request/{number}");
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", await StateAsync(instance));
        using (var approved = await CompleteAsync(chau, await OpenTaskAsync(instance), """{ "outcome": "approve" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        await RunToEndAsync(instance);
        Assert.Equal("failed", await StateAsync(instance));
        Assert.Equal("createPurchaseOrder", await ScalarAsync("SELECT step FROM axis.process_instances WHERE id = @id", instance));
        var call = (Guid)(await ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", instance))!;
        Assert.Equal("failed:5", await CallAsync(call));
        Assert.Empty(await OrdersAsync(number));
        return (subject, number, instance);
    }

    // Runs the due work items, making a call that waits out its backoff due, until the instance is completed or failed.
    private async Task RunToEndAsync(Guid instance)
    {
        for (var round = 0; round < 20; round++)
        {
            await fixture.RunUntilIdleAsync(CancellationToken);
            if (await StateAsync(instance) is "completed" or "failed")
            {
                return;
            }

            await fixture.ScalarAsync(
                "UPDATE axis.process_work_items SET due_at = now() WHERE process_instance_id = @id AND connector_call_id IS NOT NULL", ("id", instance));
        }

        Assert.Fail("The instance did not complete or fail within 20 rounds.");
    }

    private async Task StubAsync(string path)
    {
        using var client = new HttpClient { BaseAddress = new Uri(fixture.PurchasingUrl) };
        using var response = await client.PostAsync(path, null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>The state and attempts of the connector call, such as <c>failed:5</c>.</summary>
    private async Task<string?> CallAsync(Guid call) =>
        (string?)await fixture.ScalarAsync("SELECT state || ':' || attempts FROM axis.connector_calls WHERE id = @id", ("id", call));

    private async Task<Guid> SubjectAsync(Guid instance) =>
        (Guid)(await ScalarAsync("SELECT subject_id FROM axis.process_instances WHERE id = @id", instance))!;

    private sealed record StubOrder(string Key, string? SupplierCode);

    // The stub's orders for the request number, with the idempotency key and supplier code each was created with.
    private async Task<IReadOnlyList<StubOrder>> OrdersAsync(string requestNumber)
    {
        using var client = new HttpClient { BaseAddress = new Uri(fixture.PurchasingUrl) };
        using var body = JsonDocument.Parse(await client.GetStringAsync("/purchase-orders", CancellationToken));
        return [.. body.RootElement.EnumerateArray()
            .Where(order => order.GetProperty("requestNumber").GetString() == requestNumber)
            .Select(order => new StubOrder(
                order.GetProperty("idempotencyKey").GetString()!,
                order.TryGetProperty("supplierCode", out var code) ? code.GetString() : null))];
    }

    // Starts an approval for a request whose department has no manager, and runs it until managerApproval fails.
    private async Task<(Guid Subject, Guid Instance)> FailAsync(HttpClient requester)
    {
        var department = Guid.NewGuid();
        await fixture.ScalarAsync(
            $"INSERT INTO {_departments} ({EntityNaming.Quote(EntityNaming.IdColumn)}, {EntityNaming.Quote(EntityNaming.Column("code"))}, {EntityNaming.Quote(EntityNaming.Column("name"))}) VALUES (@id, @code, 'No manager')",
            ("id", department),
            ("code", "T" + department.ToString("N")[..10]));
        var subject = await CreateRequestAsync(requester, 500, department);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal("failed", await StateAsync(instance));
        Assert.Equal("managerApproval", await ScalarAsync("SELECT step FROM axis.process_instances WHERE id = @id", instance));
        return (subject, instance);
    }

    private async Task AssertUnchangedAsync(Guid instance, object? revision, long history)
    {
        Assert.Equal("failed", await StateAsync(instance));
        Assert.Equal(revision, await ScalarAsync("SELECT revision FROM axis.process_instances WHERE id = @id", instance));
        Assert.NotNull(await ScalarAsync("SELECT ended_at FROM axis.process_instances WHERE id = @id", instance));
        Assert.Equal(0L, await fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instance)));
        Assert.Equal(history, await HistoryAsync(instance));
        Assert.Equal(0L, await RetriedAsync(instance));
    }

    private Task<long> HistoryAsync(Guid instance) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id", ("id", instance));

    private Task<long> RetriedAsync(Guid instance) =>
        fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.retried'", ("id", instance));

    private Task<object?> ScalarAsync(string sql, Guid instance) => fixture.ScalarAsync(sql, ("id", instance));

    private async Task<Guid> OpenTaskAsync(Guid instance) =>
        (Guid)(await ScalarAsync("SELECT id FROM axis.process_tasks WHERE process_instance_id = @id AND state = 'open'", instance))!;

    private async Task<string?> StateAsync(Guid instance) =>
        (string?)await ScalarAsync("SELECT state FROM axis.process_instances WHERE id = @id", instance);

    private async Task SetDepartmentAsync(Guid subject, Guid department) =>
        await UpdateRequestAsync(subject, "department", department);

    private async Task SetStatusAsync(Guid subject, string status) =>
        await UpdateRequestAsync(subject, "status", status);

    private async Task UpdateRequestAsync(Guid subject, string field, object value)
    {
        await using var connection = await fixture.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE {_requests}
            SET {EntityNaming.Quote(EntityNaming.Column(field))} = @value, {EntityNaming.Quote(EntityNaming.VersionColumn)} = {EntityNaming.Quote(EntityNaming.VersionColumn)} + 1
            WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id
            """,
            connection);
        command.Parameters.AddWithValue("value", value);
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
}
