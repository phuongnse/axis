using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Processes.Instances;
using Axis.Server.Tests;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// An approved purchase request ends with exactly one purchase order when the purchasing system
/// stores the order but the worker never hears back: the worker stops in the middle of the call, a
/// second worker takes the call over while the first still waits, or the response is lost. Each
/// test leaves no due work item behind, so the next one finds only its own.
/// </summary>
public sealed class PurchaseRequestLostResponseTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string Records = "/api/apps/PurchaseRequests/entities/PurchaseRequest/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_worker_stopped_after_the_order_is_stored_is_settled_by_the_next_worker_with_one_order()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        var (subject, number) = await CreateAsync(anna);
        await StubAsync($"/test/hold-request/{number}");
        var instance = await StartAsync(anna, subject);

        var call = Guid.Empty;
        using (var workerA = await fixture.StartWorkerAsync(new LogCollector()))
        {
            await ApproveAsync(chau, instance);
            call = await StoredWhileSendingAsync(instance, number);
            await workerA.StopAsync(CancellationToken);
        }

        // The stopped worker recorded nothing about its attempt.
        Assert.Equal("sending:1", await CallAsync(call));
        Assert.Null((await NumbersAsync(anna, subject)).PurchaseOrderNumber);
        Assert.Equal("waiting:createPurchaseOrder", await StateAsync(instance));

        await EndLeaseAsync(call);
        var logs = new LogCollector();
        using (var workerB = await fixture.StartWorkerAsync(logs))
        {
            // Worker B counts the stopped attempt as an unknown outcome, and sends the call again.
            await WaitUntilAsync(async () => await CallAsync(call) == "pending:1");
            Assert.Equal(ConnectorCallHandler.UnknownOutcome, await LastErrorAsync(call));
            await MakeDueAsync(call);
            await WaitUntilAsync(async () => await StateAsync(instance) == "completed:done");
            await workerB.StopAsync(CancellationToken);
        }

        Assert.Equal("succeeded:2", await CallAsync(call));
        var (_, orderNumber) = await NumbersAsync(anna, subject);
        Assert.NotNull(orderNumber);
        Assert.Equal(orderNumber, Assert.Single(await fixture.OrdersAsync(number)));
        Assert.Equal(0L, await WorkItemsAsync(instance));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Two_workers_racing_on_the_same_call_create_one_order()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        var (subject, number) = await CreateAsync(anna);
        await StubAsync($"/test/hold-request/{number}");
        var instance = await StartAsync(anna, subject);

        var logsA = new LogCollector();
        var logsB = new LogCollector();
        using var workerA = await fixture.StartWorkerAsync(logsA);
        await ApproveAsync(chau, instance);
        var call = await StoredWhileSendingAsync(instance, number);

        // Worker A still waits for its response when worker B takes the call over and settles it.
        await EndLeaseAsync(call);
        using var workerB = await fixture.StartWorkerAsync(logsB);
        await WaitUntilAsync(async () => await CallAsync(call) == "pending:1");
        await MakeDueAsync(call);
        await WaitUntilAsync(async () => await CallAsync(call) == "succeeded:2");

        // Worker A then gets its answer, and records nothing.
        await StubAsync($"/test/release-request/{number}");
        await WaitUntilAsync(() => Task.FromResult(logsA.Entries.Any(entry => entry.Contains("was claimed by another worker", StringComparison.Ordinal))));
        await WaitUntilAsync(async () => await StateAsync(instance) == "completed:done");
        await workerA.StopAsync(CancellationToken);
        await workerB.StopAsync(CancellationToken);

        var (_, orderNumber) = await NumbersAsync(anna, subject);
        Assert.NotNull(orderNumber);
        Assert.Equal(orderNumber, Assert.Single(await fixture.OrdersAsync(number)));
        Assert.Equal(1L, await SystemAuditAsync(instance, "connector.called"));
        Assert.Equal(1L, await fixture.CountAsync(
            """
            SELECT count(*) FROM axis.audit_records
            WHERE process_instance_id = @instance AND actor = 'system' AND action = 'record.updated'
              AND details->'fields' @> '["purchaseOrderNumber"]'::jsonb
            """,
            ("instance", instance)));
        Assert.Equal(1L, await SettledRowsAsync(instance));
        Assert.Equal(0L, await WorkItemsAsync(instance));
        Assert.DoesNotContain(logsA.Entries.Concat(logsB.Entries), entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_dropped_response_is_retried_with_the_same_key_and_history_and_audit_show_the_attempts_and_the_settlement()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        var (subject, number) = await CreateAsync(anna);
        await StubAsync($"/test/drop-request/{number}");
        var instance = await StartAsync(anna, subject);

        var logs = new LogCollector();
        var call = Guid.Empty;
        using (var worker = await fixture.StartWorkerAsync(logs))
        {
            await ApproveAsync(chau, instance);
            await WaitUntilAsync(async () => await InstanceCallAsync(instance) == "pending:1");
            call = await CallIdAsync(instance);
            Assert.NotNull(await LastErrorAsync(call));
            Assert.Single(await fixture.OrdersAsync(number));
            await MakeDueAsync(call);
            await WaitUntilAsync(async () => await StateAsync(instance) == "completed:done");
            await worker.StopAsync(CancellationToken);
        }

        var (orderNumber, version) = await ReadAsync(anna, subject);
        Assert.NotNull(orderNumber);
        Assert.Equal(orderNumber, Assert.Single(await fixture.OrdersAsync(number)));

        // The connector step writes a row when it stages the call and another when the call settles.
        // The retry writes neither a history row nor an audit record.
        Assert.Equal(
            "submit,managerApproval,managerApproval,financeCheck,markApproved,createPurchaseOrder,createPurchaseOrder,assignPaymentReference,assignPaymentReference,done",
            (string?)await fixture.ScalarAsync(
                "SELECT string_agg(step, ',' ORDER BY revision) FROM axis.process_step_history WHERE process_instance_id = @instance",
                ("instance", instance)));
        Assert.Equal(1L, await SettledRowsAsync(instance));
        // The payment reference step then writes the record once more, so the purchase order step settled one version earlier.
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["operationId"] = call, ["outcome"] = "succeeded", ["next"] = "assignPaymentReference", ["subjectVersion"] = version - 1 },
            JsonNode.Parse((string)(await fixture.ScalarAsync(
                """
                SELECT output::text FROM axis.process_step_history
                WHERE process_instance_id = @instance AND step = 'createPurchaseOrder' AND output->>'outcome' = 'succeeded'
                """,
                ("instance", instance)))!)));

        Assert.Equal(1L, await SystemAuditAsync(instance, "connector.callStaged"));
        Assert.Equal(1L, await SystemAuditAsync(instance, "connector.called"));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["connector"] = "PurchasingSystem",
                ["operation"] = "createPurchaseOrder",
                ["operationId"] = call,
                ["attempts"] = 2,
                ["outcome"] = "succeeded",
                ["lastStatus"] = 200,
            },
            JsonNode.Parse((string)(await fixture.ScalarAsync(
                "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @instance AND action = 'connector.called'",
                ("instance", instance)))!)));
        Assert.Equal(0L, await WorkItemsAsync(instance));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    /// <summary>Creates a draft Operations request below the finance threshold, and returns its id and number.</summary>
    private static async Task<(Guid Subject, string Number)> CreateAsync(HttpClient anna)
    {
        var subject = await CreateRequestAsync(anna, 500);
        var (number, _) = await NumbersAsync(anna, subject);
        return (subject, number);
    }

    /// <summary>Waits until the running worker has opened chau's task for the instance, and approves it.</summary>
    private async Task ApproveAsync(HttpClient chau, Guid instance)
    {
        await WaitUntilAsync(async () => await StateAsync(instance) == "waiting:managerApproval");
        var task = (Guid)(await fixture.ScalarAsync(
            "SELECT id FROM axis.process_tasks WHERE process_instance_id = @instance AND state = 'open'",
            ("instance", instance)))!;
        using var completed = await CompleteAsync(chau, task, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }

    /// <summary>
    /// Waits until the stub has stored the order for the held request <paramref name="number"/>
    /// while the instance's call is still <c>sending</c>, and returns the call's id.
    /// </summary>
    private async Task<Guid> StoredWhileSendingAsync(Guid instance, string number)
    {
        await WaitUntilAsync(async () => await InstanceCallAsync(instance) == "sending:1" && (await fixture.OrdersAsync(number)).Count == 1);
        return await CallIdAsync(instance);
    }

    /// <summary>Sends a <c>POST</c> to a test endpoint of the purchasing system stub.</summary>
    private async Task StubAsync(string path)
    {
        using var client = new HttpClient { BaseAddress = new Uri(fixture.PurchasingUrl) };
        using var response = await client.PostAsync(path, null, CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private async Task<Guid> CallIdAsync(Guid instance) =>
        (Guid)(await fixture.ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @instance", ("instance", instance)))!;

    /// <summary>The state and attempts of the call, such as <c>sending:1</c>.</summary>
    private async Task<string?> CallAsync(Guid call) =>
        (string?)await fixture.ScalarAsync("SELECT state || ':' || attempts FROM axis.connector_calls WHERE id = @id", ("id", call));

    /// <summary>The state and attempts of the instance's call, or <see langword="null"/> before the call is staged.</summary>
    private async Task<string?> InstanceCallAsync(Guid instance) =>
        (string?)await fixture.ScalarAsync(
            "SELECT state || ':' || attempts FROM axis.connector_calls WHERE process_instance_id = @instance",
            ("instance", instance));

    private async Task<string?> LastErrorAsync(Guid call) =>
        (string?)await fixture.ScalarAsync("SELECT last_error FROM axis.connector_calls WHERE id = @id", ("id", call));

    /// <summary>Makes the call's work item due now. It stands in for the sample's 30 second backoff running out.</summary>
    private async Task MakeDueAsync(Guid call) =>
        await fixture.ScalarAsync("UPDATE axis.process_work_items SET due_at = now() WHERE connector_call_id = @id", ("id", call));

    /// <summary>
    /// Ends the lease on the call's work item now. It stands in for the lease of about 32 seconds, the
    /// connector's timeout and the worker's lease, running out while the request is held.
    /// </summary>
    private async Task EndLeaseAsync(Guid call) =>
        await fixture.ScalarAsync("UPDATE axis.process_work_items SET lease_expires_at = now() WHERE connector_call_id = @id", ("id", call));

    /// <summary>The instance's state and step, such as <c>waiting:managerApproval</c>.</summary>
    private async Task<string?> StateAsync(Guid instance) =>
        (string?)await fixture.ScalarAsync("SELECT state || ':' || step FROM axis.process_instances WHERE id = @instance", ("instance", instance));

    /// <summary>Counts the instance's settled <c>createPurchaseOrder</c> history rows with the outcome <c>succeeded</c>.</summary>
    private Task<long> SettledRowsAsync(Guid instance) =>
        fixture.CountAsync(
            """
            SELECT count(*) FROM axis.process_step_history
            WHERE process_instance_id = @instance AND step = 'createPurchaseOrder' AND output->>'outcome' = 'succeeded'
            """,
            ("instance", instance));

    private Task<long> WorkItemsAsync(Guid instance) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @instance", ("instance", instance));

    /// <summary>Counts the instance's <c>system</c> audit records of <paramref name="action"/>.</summary>
    private Task<long> SystemAuditAsync(Guid instance, string action) =>
        fixture.CountAsync(
            "SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @instance AND actor = 'system' AND action = @action",
            ("instance", instance),
            ("action", action));

    /// <summary>The request's purchase order number and version, read through the record API.</summary>
    private static async Task<(string? PurchaseOrderNumber, long Version)> ReadAsync(HttpClient client, Guid subject)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{Records}/{subject:D}", Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var values = body.RootElement.GetProperty("values");
        return (
            values.TryGetProperty("purchaseOrderNumber", out var order) ? order.GetString() : null,
            body.RootElement.GetProperty("version").GetInt64());
    }
}
