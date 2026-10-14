using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using Npgsql;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A purchase request keeps its place in the approval process when the worker stops: while it
/// waits for its manager, and when a worker dies in the middle of a step. An approved request then
/// gets its purchase order from the purchasing system stub. Each test leaves no due work item
/// behind, so the next one finds only its own.
/// </summary>
public sealed class PurchaseRequestRecoveryTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string Records = "/api/apps/PurchaseRequests/entities/PurchaseRequest/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(500, "completed", "markApproved,createPurchaseOrder,createPurchaseOrder,assignPaymentReference,assignPaymentReference,done", "approved")]
    [InlineData(10000, "waiting", "financeApproval", "submitted")]
    public async Task A_decision_made_while_no_worker_runs_is_taken_up_by_the_next_worker(int unitPrice, string state, string steps, string status)
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        using var chau = fixture.CreateClient().SignedInAs("chau");
        var subject = await CreateRequestAsync(anna, unitPrice);
        var instance = await StartAsync(anna, subject);

        var logs = new LogCollector();
        using (var workerA = await fixture.StartWorkerAsync(logs))
        {
            await WaitUntilAsync(async () => await StateAsync(instance) == "waiting:managerApproval");
            await workerA.StopAsync(CancellationToken);
        }

        var task = (Guid)(await fixture.ScalarAsync(
            "SELECT id FROM axis.process_tasks WHERE process_instance_id = @instance AND state = 'open'",
            ("instance", instance)))!;
        using (var completed = await CompleteAsync(chau, task, """{ "outcome": "approve" }"""))
        {
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        }

        Assert.Equal("running:financeCheck", await StateAsync(instance));
        Assert.Equal(1L, await WorkItemsAsync(instance));
        Assert.Equal("submitted", (await ReadAsync(anna, subject)).Status);

        using (var workerB = await fixture.StartWorkerAsync(logs))
        {
            await WaitUntilAsync(async () => await StateAsync(instance) == $"{state}:{steps.Split(',')[^1]}");
            await workerB.StopAsync(CancellationToken);
        }

        Assert.Equal(status, (await ReadAsync(anna, subject)).Status);
        Assert.Equal(0L, await WorkItemsAsync(instance));
        if (unitPrice >= 10000)
        {
            Assert.Equal(
                "role:finance-reviewer",
                await fixture.ScalarAsync(
                    "SELECT assignee_kind || ':' || assignee FROM axis.process_tasks WHERE process_instance_id = @instance AND state = 'open' AND step = 'financeApproval'",
                    ("instance", instance)));
        }

        // The task step writes a row when it opens the task and another when the task is decided.
        // The connector step writes a row when it stages the call and another when the call
        // settles. Every other step ran once, and no revision was written twice.
        Assert.Equal($"submit,managerApproval,managerApproval,financeCheck,{steps}", await StepsAsync(instance));
        Assert.Equal(
            0L,
            await fixture.CountAsync(
                "SELECT count(*) - count(DISTINCT revision) FROM axis.process_step_history WHERE process_instance_id = @instance",
                ("instance", instance)));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));

        // Only the approved request has a purchase order, and the stub holds exactly one for it.
        var (number, orderNumber) = await NumbersAsync(anna, subject);
        var orders = await fixture.OrdersAsync(number);
        if (status == "approved")
        {
            Assert.NotNull(orderNumber);
            Assert.Equal(orderNumber, Assert.Single(orders));
        }
        else
        {
            Assert.Null(orderNumber);
            Assert.Empty(orders);
        }
    }

    [Fact]
    public async Task A_worker_that_dies_mid_step_leaves_no_partial_writes_and_the_next_worker_commits_the_step_once()
    {
        using var anna = fixture.CreateClient().SignedInAs("anna");
        var subject = await CreateRequestAsync(anna, 500);
        var instance = await StartAsync(anna, subject);
        var (_, version) = await ReadAsync(anna, subject);

        // Holding this lock blocks the step's audit insert, after it has advanced the instance,
        // written the record and inserted its history row, none of them committed.
        await using (var blocker = await fixture.OpenConnectionAsync())
        await using (var transaction = await blocker.BeginTransactionAsync(CancellationToken))
        {
            await using (var lockAudit = new NpgsqlCommand("LOCK TABLE axis.audit_records IN EXCLUSIVE MODE", blocker, transaction))
            {
                await lockAudit.ExecuteNonQueryAsync(CancellationToken);
            }

            var crashedLogs = new LogCollector();
            var workerA = await fixture.StartWorkerAsync(crashedLogs);
            try
            {
                await WaitUntilAsync(async () => await fixture.CountAsync(
                    "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'") == 1);
                Assert.Equal(1L, await fixture.CountAsync(
                    "SELECT count(*) FROM pg_locks WHERE NOT granted AND relation = 'axis.audit_records'::regclass"));

                // Ending the worker's session is a crash from the database's side: its writes in
                // flight are thrown away. The host logs the lost session and stops before its two
                // second lease runs out, so it cannot claim the item again.
                Assert.Equal(1L, await fixture.CountAsync(
                    "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'"));
                await WaitUntilAsync(() => Task.FromResult(crashedLogs.Entries.Any(entry => entry.StartsWith("Error", StringComparison.Ordinal))));
                await workerA.StopAsync(CancellationToken);
            }
            finally
            {
                workerA.Dispose();
            }

            await transaction.RollbackAsync(CancellationToken);
        }

        Assert.Equal(("draft", version), await ReadAsync(anna, subject));
        Assert.Equal("running:submit:1", await StateAsync(instance, withRevision: true));
        Assert.Equal(0L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @instance", ("instance", instance)));
        Assert.Equal(0L, await SystemAuditAsync(instance));
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @instance AND claim_token IS NOT NULL",
            ("instance", instance)));

        var logs = new LogCollector();
        using (var workerB = await fixture.StartWorkerAsync(logs))
        {
            await WaitUntilAsync(async () => await StateAsync(instance) == "waiting:managerApproval");
            await workerB.StopAsync(CancellationToken);
        }

        Assert.Equal(("submitted", version + 1), await ReadAsync(anna, subject));
        Assert.Equal(1L, await fixture.CountAsync(
            "SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @instance AND step = 'submit'",
            ("instance", instance)));
        Assert.Equal(1L, await SystemAuditAsync(instance, "record.updated"));
        Assert.Equal(1L, await SystemAuditAsync(instance, "process.stepCompleted", "submit"));
        Assert.Equal(1L, await SystemAuditAsync(instance, "task.created"));
        Assert.Equal(3L, await SystemAuditAsync(instance));
        Assert.Equal(0L, await WorkItemsAsync(instance));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    /// <summary>The instance's state and step, such as <c>waiting:managerApproval</c>, and its revision when asked.</summary>
    private async Task<string?> StateAsync(Guid instance, bool withRevision = false) =>
        (string?)await fixture.ScalarAsync(
            withRevision
                ? "SELECT state || ':' || step || ':' || revision FROM axis.process_instances WHERE id = @instance"
                : "SELECT state || ':' || step FROM axis.process_instances WHERE id = @instance",
            ("instance", instance));

    /// <summary>The steps of the instance's history in revision order, joined by commas.</summary>
    private async Task<string?> StepsAsync(Guid instance) =>
        (string?)await fixture.ScalarAsync(
            "SELECT string_agg(step, ',' ORDER BY revision) FROM axis.process_step_history WHERE process_instance_id = @instance",
            ("instance", instance));

    private Task<long> WorkItemsAsync(Guid instance) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @instance", ("instance", instance));

    /// <summary>Counts the instance's <c>system</c> audit records, of <paramref name="action"/> and for <paramref name="step"/> when given.</summary>
    private Task<long> SystemAuditAsync(Guid instance, string? action = null, string? step = null) =>
        fixture.CountAsync(
            """
            SELECT count(*) FROM axis.audit_records
            WHERE process_instance_id = @instance AND actor = 'system'
              AND (@action = '' OR action = @action) AND (@step = '' OR details->>'step' = @step)
            """,
            ("instance", instance),
            ("action", action ?? ""),
            ("step", step ?? ""));

    /// <summary>The request's status and version, read through the record API.</summary>
    private static async Task<(string? Status, long Version)> ReadAsync(HttpClient client, Guid subject)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{Records}/{subject:D}", Host);
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return (body.RootElement.GetProperty("values").GetProperty("status").GetString(), body.RootElement.GetProperty("version").GetInt64());
    }
}
