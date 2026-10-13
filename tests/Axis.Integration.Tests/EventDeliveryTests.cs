using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

/// <summary>
/// The delivery of raised events to their <c>async</c> event triggers. StepApp's <c>Announce</c>
/// raises <c>RequestNoted</c> with <c>ratio</c> as the amount over the divisor. Its trigger
/// <c>ApproveNotedRequests</c> starts <c>RoleApprove</c> when the ratio is 100 or more, and
/// <c>DecideHugeNotedRequests</c> starts <c>Decide</c> when it is a million or more.
/// </summary>
public sealed class EventDeliveryTests(EventDeliveryFixture fixture) : IClassFixture<EventDeliveryFixture>
{
    private const string Approve = "ApproveNotedRequests";

    private const string DecideHuge = "DecideHugeNotedRequests";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delivering_a_raised_event_starts_the_triggered_process_once_for_its_record_as_system()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, eventId) = await RaiseAsync(tenant);

        // The ratio is 125, so only ApproveNotedRequests starts its process.
        var instance = Assert.Single(await InstancesAsync(tenant, "RoleApprove", subjectId));
        Assert.Equal((ProcessStarts.Waiting, tenant.FirstReleaseId), (instance.State, instance.ReleaseId));
        Assert.Empty(await InstancesAsync(tenant, "Decide", subjectId));

        Assert.Equal(("started", (Guid?)instance.Id), await ReceiptAsync(tenant, Approve, eventId, subjectId));
        Assert.Equal(("skipped", (Guid?)null), await ReceiptAsync(tenant, DecideHuge, eventId, subjectId));

        Assert.True(tenant.Model.TryGetProcess("RoleApprove", out var process));
        var details = (string?)await tenant.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action AND actor = 'system'",
            ("id", instance.Id),
            ("action", AuditActions.ProcessStarted));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["processId"] = process.Id,
                ["releaseId"] = tenant.FirstReleaseId,
                ["trigger"] = Approve,
                ["eventId"] = eventId,
            },
            JsonNode.Parse(details!)));

        // The delivery item is gone once every trigger has its receipt.
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE event_id = @id", ("id", eventId)));
    }

    [Fact]
    public async Task Trigger_whose_when_is_false_starts_no_instance()
    {
        var tenant = fixture.Tenant("a");

        // The ratio is 25, below both triggers' thresholds.
        var (subjectId, eventId) = await RaiseAsync(tenant, amount: 100m);

        Assert.Empty(await InstancesAsync(tenant, "RoleApprove", subjectId));
        Assert.Empty(await InstancesAsync(tenant, "Decide", subjectId));
        Assert.Equal(("skipped", (Guid?)null), await ReceiptAsync(tenant, Approve, eventId, subjectId));
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.StartsWith("Information: ", StringComparison.Ordinal)
                && entry.Contains(Approve, StringComparison.Ordinal)
                && entry.Contains(subjectId.ToString(), StringComparison.Ordinal)
                && entry.Contains("the when is not true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delivery_run_again_on_a_restarted_worker_after_its_start_committed_starts_no_second_instance()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, eventId) = await RaiseAsync(tenant);
        var first = Assert.Single(await InstancesAsync(tenant, "RoleApprove", subjectId));

        // The first instance completes, so only the receipt can stop a second start.
        await tenant.ScalarAsync(
            "UPDATE axis.process_instances SET state = 'completed', ended_at = now() WHERE id = @id",
            ("id", first.Id));

        // A second item for the same occurrence stands for a delivery that a crashed worker ran.
        await fixture.EnqueueDeliveryAsync("a", eventId);
        await fixture.RestartWorkerAsync();
        await fixture.RunUntilIdleAsync("a", CancellationToken);

        var instance = Assert.Single(await InstancesAsync(tenant, "RoleApprove", subjectId));
        Assert.Equal((first.Id, ProcessStarts.Completed), (instance.Id, instance.State));
        Assert.Equal(("started", (Guid?)first.Id), await ReceiptAsync(tenant, Approve, eventId, subjectId));
        Assert.Equal(
            1L,
            await tenant.CountAsync(
                "SELECT count(*) FROM axis.audit_records WHERE record_id = @subject AND action = @action",
                ("subject", subjectId),
                ("action", AuditActions.ProcessStarted)));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE event_id = @id", ("id", eventId)));
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.Contains(Approve, StringComparison.Ordinal)
                && entry.Contains(subjectId.ToString(), StringComparison.Ordinal)
                && entry.Contains("the trigger already handled the occurrence", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delivery_skips_the_start_when_the_process_already_runs_for_the_record_and_the_raising_process_completes()
    {
        var tenant = fixture.Tenant("a");
        var subjectId = await tenant.InsertRequestAsync(500m, 4);
        var runningId = await tenant.StartAsync("RoleApprove", subjectId, tenant.FirstReleaseId);
        await fixture.RunUntilIdleAsync("a", CancellationToken);
        Assert.Equal(ProcessStarts.Waiting, (await tenant.InstanceAsync(runningId)).State);

        var announceId = await tenant.StartAsync("Announce", subjectId, tenant.FirstReleaseId);
        await fixture.RunUntilIdleAsync("a", CancellationToken);

        Assert.Equal(ProcessStarts.Completed, (await tenant.InstanceAsync(announceId)).State);
        var eventId = await EventIdAsync(tenant, announceId);
        Assert.Equal(runningId, Assert.Single(await InstancesAsync(tenant, "RoleApprove", subjectId)).Id);
        Assert.Equal(("skipped", (Guid?)null), await ReceiptAsync(tenant, Approve, eventId, subjectId));

        var skip = Assert.Single(
            fixture.Logs.Entries,
            entry => entry.Contains(Approve, StringComparison.Ordinal) && entry.Contains(subjectId.ToString(), StringComparison.Ordinal));
        Assert.StartsWith("Information: ", skip, StringComparison.Ordinal);
        Assert.Contains("the process already runs for the record", skip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Event_raised_in_one_tenant_starts_no_process_and_writes_no_receipt_in_another()
    {
        var a = fixture.Tenant("a");
        var b = fixture.Tenant("b");
        var (subjectId, _) = await RaiseAsync(a);
        await fixture.RunUntilIdleAsync("b", CancellationToken);

        Assert.Single(await InstancesAsync(a, "RoleApprove", subjectId));
        Assert.Equal(0L, await b.CountAsync("SELECT count(*) FROM axis.process_instances"));
        Assert.Equal(0L, await b.CountAsync("SELECT count(*) FROM axis.trigger_starts"));
        Assert.Equal(0L, await b.CountAsync("SELECT count(*) FROM axis.process_work_items"));
    }

    /// <summary>
    /// Raises <c>RequestNoted</c> for a new request of <paramref name="amount"/> over 4 through
    /// <c>Announce</c>, and runs the tenant's work, its delivery included, until none is left.
    /// Returns the request and the event.
    /// </summary>
    private async Task<(Guid SubjectId, Guid EventId)> RaiseAsync(ProcessStepFixture tenant, decimal amount = 500m)
    {
        var subjectId = await tenant.InsertRequestAsync(amount, 4);
        var announceId = await tenant.StartAsync("Announce", subjectId, tenant.FirstReleaseId);
        await fixture.RunUntilIdleAsync(tenant.Tenant, CancellationToken);
        Assert.Equal(ProcessStarts.Completed, (await tenant.InstanceAsync(announceId)).State);
        return (subjectId, await EventIdAsync(tenant, announceId));
    }

    private static async Task<Guid> EventIdAsync(ProcessStepFixture tenant, Guid instanceId) =>
        (Guid)(await tenant.ScalarAsync("SELECT id FROM axis.events WHERE process_instance_id = @id", ("id", instanceId)))!;

    /// <summary>The instances of <paramref name="process"/> for the record, in the order they were created.</summary>
    private static async Task<IReadOnlyList<(Guid Id, string State, Guid ReleaseId)>> InstancesAsync(ProcessStepFixture tenant, string process, Guid subjectId)
    {
        Assert.True(tenant.Model.TryGetProcess(process, out var model));
        await using var command = tenant.DataSource.CreateCommand(
            "SELECT id, state, release_id FROM axis.process_instances WHERE process_id = @process AND subject_id = @subject ORDER BY id");
        command.Parameters.AddWithValue("process", model.Id);
        command.Parameters.AddWithValue("subject", subjectId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<(Guid, string, Guid)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2)));
        }

        return rows;
    }

    /// <summary>The outcome and instance of the trigger's receipt for the occurrence and record. The receipt must exist.</summary>
    private static async Task<(string Outcome, Guid? InstanceId)> ReceiptAsync(ProcessStepFixture tenant, string trigger, Guid eventId, Guid subjectId)
    {
        var triggerId = Assert.Single(tenant.Model.Triggers, candidate => candidate.Name == trigger).Id;
        await using var command = tenant.DataSource.CreateCommand(
            "SELECT outcome, process_instance_id FROM axis.trigger_starts WHERE trigger_id = @trigger AND source = @source AND subject_id = @subject");
        command.Parameters.AddWithValue("trigger", triggerId);
        command.Parameters.AddWithValue("source", eventId.ToString("D"));
        command.Parameters.AddWithValue("subject", subjectId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }
}
