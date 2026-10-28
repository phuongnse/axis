using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using Npgsql;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// An operator with a <c>resolve</c> grant settles a purchase order call whose outcome is unknown, through the
/// HTTP API. The sample gives the grant to the finance reviewers, here <c>finance-reviewer</c>. <c>requester</c> starts the
/// approvals and <c>manager-2</c> is the manager. The sample connector declares a lookup, so each test forces the call
/// to <c>unknown</c> and deletes its work item, as a connector with no lookup leaves it. Each test leaves no due
/// work item behind, so the next one finds only its own.
/// </summary>
public sealed class ProcessResolveTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string Succeeded = """{ "reason": "Checked in the purchasing system", "outcome": "succeeded", "output": { "orderNumber": "PO-RESOLVED-1" } }""";

    private const string Failed = """{ "reason": "No order exists", "outcome": "failed" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Resolving_an_unknown_call_as_succeeded_writes_the_output_and_the_instance_runs_on()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var financeReviewer = fixture.CreateClient().SignedInAs("finance-reviewer");
        var (subject, instance, call) = await UnknownCallAsync(requester);
        var (number, _) = await NumbersAsync(requester, subject);

        using (var response = await ResolveAsync(financeReviewer, instance, call, Succeeded))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal(instance, body.RootElement.GetProperty("id").GetGuid());
            Assert.Equal(subject, body.RootElement.GetProperty("subjectId").GetGuid());
            Assert.Equal("running", body.RootElement.GetProperty("state").GetString());
        }

        Assert.Equal("succeeded", await CallStateAsync(call));
        Assert.NotNull(await fixture.ScalarAsync("SELECT settled_at FROM axis.connector_calls WHERE id = @id", ("id", call)));
        Assert.Equal("PO-RESOLVED-1", (await NumbersAsync(requester, subject)).PurchaseOrderNumber);

        await RunToEndAsync(instance);

        Assert.Equal("completed", await StateAsync(instance));
        Assert.Empty(await fixture.OrdersAsync(number));
        Assert.Equal("PO-RESOLVED-1", (await NumbersAsync(requester, subject)).PurchaseOrderNumber);
        Assert.Equal(0L, await DueWorkItemsAsync(instance));
    }

    [Fact]
    public async Task A_succeeded_resolve_writes_one_audit_record_with_the_user_and_the_reason_and_no_connector_called()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var financeReviewer = fixture.CreateClient().SignedInAs("finance-reviewer");
        var (subject, instance, call) = await UnknownCallAsync(requester);

        using (var response = await ResolveAsync(financeReviewer, instance, call, Succeeded))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(
            1L,
            await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'connector.callResolved'", ("id", instance)));
        Assert.Equal(
            0L,
            await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'connector.called'", ("id", instance)));
        Assert.Equal(
            ("finance-reviewer", "Finance Reviewer User", subject),
            await AuditActorAsync(instance, "connector.callResolved"));
        using var details = JsonDocument.Parse((string)(await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = 'connector.callResolved'", ("id", instance)))!);
        // The details are jsonb, which does not keep the order of the properties.
        Assert.Equal(
            ["connector", "operation", "operationId", "outcome", "reason", "step"],
            details.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("createPurchaseOrder", details.RootElement.GetProperty("step").GetString());
        Assert.Equal("PurchasingSystem", details.RootElement.GetProperty("connector").GetString());
        Assert.Equal(call, details.RootElement.GetProperty("operationId").GetGuid());
        Assert.Equal("succeeded", details.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("Checked in the purchasing system", details.RootElement.GetProperty("reason").GetString());

        // The record update names the user too, and the history row says the call was resolved by hand.
        Assert.Equal(("finance-reviewer", "Finance Reviewer User", subject), await AuditActorAsync(instance, "record.updated", "details->'fields' = '[\"purchaseOrderNumber\"]'::jsonb"));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id AND step = 'createPurchaseOrder' AND output->>'resolved' = 'true' AND output->>'outcome' = 'succeeded'",
                ("id", instance)));
        await fixture.RunUntilIdleAsync(CancellationToken);
    }

    [Fact]
    public async Task A_resolve_whose_output_lacks_a_required_value_is_400_and_changes_nothing()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var financeReviewer = fixture.CreateClient().SignedInAs("finance-reviewer");
        var (subject, instance, call) = await UnknownCallAsync(requester);
        var revision = await ScalarAsync("SELECT revision FROM axis.process_instances WHERE id = @id", instance);
        var history = await HistoryAsync(instance);
        var audit = await AuditAsync(instance);

        foreach (var (body, key, message) in new[]
        {
            ("""{ "reason": "Checked", "outcome": "succeeded", "output": { } }""", "/output/orderNumber", "Required."),
            ("""{ "reason": "Checked", "outcome": "succeeded", "output": { "orderNumber": null } }""", "/output/orderNumber", "Required."),
            ("""{ "reason": "Checked", "outcome": "succeeded", "output": { "orderNumber": 5 } }""", "/output/orderNumber", "Must be a text value."),
            ("""{ "reason": "Checked", "outcome": "succeeded", "output": { "orderNumber": "PO-1", "extra": 1 } }""", "/output/extra", "Unknown property."),
            ("""{ "reason": "Checked", "outcome": "succeeded" }""", "/output/orderNumber", "Required."),
        })
        {
            using var response = await ResolveAsync(financeReviewer, instance, call, body);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
            var errors = problem.RootElement.GetProperty("errors");
            Assert.Equal([key], errors.EnumerateObject().Select(error => error.Name));
            Assert.Equal([message], errors.GetProperty(key).EnumerateArray().Select(error => error.GetString()));
        }

        Assert.Equal("unknown", await CallStateAsync(call));
        Assert.Equal("waiting", await StateAsync(instance));
        Assert.Equal(revision, await ScalarAsync("SELECT revision FROM axis.process_instances WHERE id = @id", instance));
        Assert.Equal(history, await HistoryAsync(instance));
        Assert.Equal(audit, await AuditAsync(instance));
        Assert.Null((await NumbersAsync(requester, subject)).PurchaseOrderNumber);
        Assert.Equal(0L, await DueWorkItemsAsync(instance));
    }

    [Fact]
    public async Task A_body_of_the_wrong_shape_is_400()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var financeReviewer = fixture.CreateClient().SignedInAs("finance-reviewer");
        var (_, instance, call) = await UnknownCallAsync(requester);

        foreach (var (body, key, message) in new[]
        {
            ("""{ "outcome": "succeeded", "output": { "orderNumber": "x" } }""", "/reason", "Required."),
            ("""{ "reason": "Because", "output": { "orderNumber": "x" } }""", "/outcome", "Required."),
            ("""{ "reason": "Because", "outcome": "done" }""", "/outcome", "Must be succeeded or failed."),
            ("""{ "reason": "Because", "outcome": "failed", "output": { } }""", "/output", "Not allowed when the outcome is failed."),
            ("""{ "reason": "Because", "outcome": "succeeded", "output": [] }""", "/output", "Must be an object."),
            ("""{ "reason": "Because", "outcome": "failed", "note": 1 }""", "/note", "Unknown property."),
            ("[]", "", "Must be a JSON object."),
        })
        {
            using var response = await ResolveAsync(financeReviewer, instance, call, body);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.BadRequest);
            var errors = problem.RootElement.GetProperty("errors");
            Assert.Equal([key], errors.EnumerateObject().Select(error => error.Name));
            Assert.Equal([message], errors.GetProperty(key).EnumerateArray().Select(error => error.GetString()));
        }

        Assert.Equal("unknown", await CallStateAsync(call));
        Assert.Equal("waiting", await StateAsync(instance));
        await fixture.RunUntilIdleAsync(CancellationToken);
    }

    [Fact]
    public async Task Resolving_as_failed_fails_the_instance_and_a_retry_sends_again_under_the_same_identity()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var financeReviewer = fixture.CreateClient().SignedInAs("finance-reviewer");
        var (subject, instance, call) = await UnknownCallAsync(requester);
        var (number, _) = await NumbersAsync(requester, subject);

        using (var response = await ResolveAsync(financeReviewer, instance, call, Failed))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
            Assert.Equal("failed", body.RootElement.GetProperty("state").GetString());
        }

        Assert.Equal("failed", await CallStateAsync(call));
        Assert.Equal("failed", await StateAsync(instance));
        Assert.NotNull(await fixture.ScalarAsync("SELECT ended_at FROM axis.process_instances WHERE id = @id", ("id", instance)));
        Assert.Equal(
            1L,
            await fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.failed'", ("id", instance)));
        Assert.Equal(("finance-reviewer", "Finance Reviewer User", subject), await AuditActorAsync(instance, "process.failed"));
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id AND output->>'resolved' = 'true' AND output->>'outcome' = 'failed'",
                ("id", instance)));
        Assert.Equal(0L, await DueWorkItemsAsync(instance));

        using (var retried = await RetryAsync(financeReviewer, instance, """{ "reason": "Send it again" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        }

        await RunToEndAsync(instance);

        Assert.Equal("completed", await StateAsync(instance));
        Assert.Equal(call, await ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", instance));
        Assert.Equal("succeeded", await CallStateAsync(call));
        var order = Assert.Single(await OrdersAsync(number));
        Assert.Equal(call.ToString("D"), order);
        Assert.NotNull((await NumbersAsync(requester, subject)).PurchaseOrderNumber);
    }

    [Fact]
    public async Task A_user_without_the_grant_a_call_that_is_not_unknown_and_an_unknown_call_id_change_nothing()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var manager2 = fixture.CreateClient().SignedInAs("manager-2");
        using var financeReviewer = fixture.CreateClient().SignedInAs("finance-reviewer");
        var (_, instance, call) = await UnknownCallAsync(requester);
        var revision = await ScalarAsync("SELECT revision FROM axis.process_instances WHERE id = @id", instance);
        var history = await HistoryAsync(instance);
        var audit = await AuditAsync(instance);

        foreach (var client in new[] { requester, manager2 })
        {
            using var denied = await ResolveAsync(client, instance, call, Succeeded);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using (var unknown = await ResolveAsync(financeReviewer, instance, Guid.NewGuid(), Succeeded))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }

        // A call of another instance is no call of this one.
        var (_, other, otherCall) = await UnknownCallAsync(requester);
        using (var elsewhere = await ResolveAsync(financeReviewer, instance, otherCall, Succeeded))
        {
            Assert.Equal(HttpStatusCode.NotFound, elsewhere.StatusCode);
        }

        Assert.Equal("unknown", await CallStateAsync(call));
        Assert.Equal("unknown", await CallStateAsync(otherCall));
        Assert.Equal(revision, await ScalarAsync("SELECT revision FROM axis.process_instances WHERE id = @id", instance));
        Assert.Equal(history, await HistoryAsync(instance));
        Assert.Equal(audit, await AuditAsync(instance));

        // A pending call is refused with 409.
        await fixture.ScalarAsync("UPDATE axis.connector_calls SET state = 'pending' WHERE id = @id", ("id", call));
        using (var pending = await ResolveAsync(financeReviewer, instance, call, Succeeded))
        {
            using var problem = await RecordApiFixture.ReadProblemAsync(pending, HttpStatusCode.Conflict);
            Assert.Equal("The call's outcome is not unknown.", problem.RootElement.GetProperty("title").GetString());
        }

        // An unknown call of an instance that is no longer waiting on its step is refused with 409.
        await fixture.ScalarAsync("UPDATE axis.connector_calls SET state = 'unknown' WHERE id = @id", ("id", call));
        await fixture.ScalarAsync("UPDATE axis.process_instances SET state = 'running' WHERE id = @id", ("id", instance));
        using (var running = await ResolveAsync(financeReviewer, instance, call, Succeeded))
        {
            using var problem = await RecordApiFixture.ReadProblemAsync(running, HttpStatusCode.Conflict);
            Assert.Equal("The instance is not waiting on the call's step.", problem.RootElement.GetProperty("title").GetString());
        }

        await fixture.ScalarAsync("UPDATE axis.process_instances SET state = 'waiting' WHERE id = @id", ("id", instance));
        Assert.Equal("unknown", await CallStateAsync(call));
        Assert.Equal(revision, await ScalarAsync("SELECT revision FROM axis.process_instances WHERE id = @id", instance));
        Assert.Equal(history, await HistoryAsync(instance));
        Assert.Equal(audit, await AuditAsync(instance));
        Assert.NotEqual(other, instance);
        Assert.Equal(0L, await DueWorkItemsAsync(instance));
    }

    // Starts an approval, approves it as the manager and runs it until the purchase order call is staged. The call is
    // then set to unknown with no work item, as a connector with no lookup leaves it.
    private async Task<(Guid Subject, Guid Instance, Guid Call)> UnknownCallAsync(HttpClient requester)
    {
        using var manager2 = fixture.CreateClient().SignedInAs("manager-2");
        var subject = await CreateRequestAsync(requester, 500);
        var instance = await StartAsync(requester, subject);
        await fixture.RunUntilIdleAsync(CancellationToken);
        var task = (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.process_tasks WHERE process_instance_id = @id AND state = 'open'", ("id", instance)))!;
        using (var approved = await CompleteAsync(manager2, task, """{ "outcome": "approve" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        }

        await fixture.RunStepsUntilIdleAsync(CancellationToken);
        Assert.Equal("waiting", await StateAsync(instance));
        var call = (Guid)(await ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", instance))!;
        await fixture.ScalarAsync("UPDATE axis.connector_calls SET state = 'unknown', attempts = 1 WHERE id = @id", ("id", call));
        await fixture.ScalarAsync("DELETE FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instance));
        return (subject, instance, call);
    }

    // Runs the due work items until the instance is completed or failed.
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

    // The idempotency keys of the stub's orders for the request number.
    private async Task<IReadOnlyList<string>> OrdersAsync(string requestNumber)
    {
        using var client = new HttpClient { BaseAddress = new Uri(fixture.PurchasingUrl) };
        using var body = JsonDocument.Parse(await client.GetStringAsync("/purchase-orders", CancellationToken));
        return [.. body.RootElement.EnumerateArray()
            .Where(order => order.GetProperty("requestNumber").GetString() == requestNumber)
            .Select(order => order.GetProperty("idempotencyKey").GetString()!)];
    }

    private async Task<object?> ScalarAsync(string sql, Guid instance) =>
        await fixture.ScalarAsync(sql, ("id", instance));

    private async Task<string?> StateAsync(Guid instance) =>
        (string?)await fixture.ScalarAsync("SELECT state FROM axis.process_instances WHERE id = @id", ("id", instance));

    private async Task<string?> CallStateAsync(Guid call) =>
        (string?)await fixture.ScalarAsync("SELECT state FROM axis.connector_calls WHERE id = @id", ("id", call));

    private Task<long> HistoryAsync(Guid instance) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id", ("id", instance));

    private Task<long> AuditAsync(Guid instance) =>
        fixture.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id", ("id", instance));

    private Task<long> DueWorkItemsAsync(Guid instance) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instance));

    private async Task<(string? Actor, string? Name, Guid? Record)> AuditActorAsync(Guid instance, string action, string? extra = null)
    {
        await using var connection = await fixture.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT actor, actor_name, record_id FROM axis.audit_records WHERE process_instance_id = @id AND action = @action{(extra is null ? string.Empty : " AND " + extra)}",
            connection);
        command.Parameters.AddWithValue("id", instance);
        command.Parameters.AddWithValue("action", action);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.GetString(1), reader.GetGuid(2));
    }
}
