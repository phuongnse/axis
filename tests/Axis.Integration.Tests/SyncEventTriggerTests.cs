using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

/// <summary>
/// Event triggers in <c>sync</c> mode, which start their process in the transaction of the
/// <c>raiseEvent</c> step that raises the event. StepApp's <c>Flag</c> raises <c>RequestFlagged</c>.
/// Its <c>sync</c> trigger <c>ReviewFlaggedRequests</c> starts <c>RoleApprove</c>, and its
/// <c>async</c> trigger <c>CheckFlaggedRequests</c> starts <c>Review</c>.
/// </summary>
public sealed class SyncEventTriggerTests(EventDeliveryFixture fixture) : IClassFixture<EventDeliveryFixture>
{
    private const string Sync = "ReviewFlaggedRequests";

    private const string Async = "CheckFlaggedRequests";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sync_trigger_has_started_its_process_once_the_raising_step_commits_and_delivery_starts_it_no_second_time()
    {
        var tenant = fixture.Tenant("a");
        var subjectId = await tenant.InsertRequestAsync(500m, 4);
        var flagId = await tenant.StartAsync("Flag", subjectId, tenant.FirstReleaseId);

        // Only the raising step runs, so no delivery has run yet.
        Assert.True(await tenant.RunNextStepAsync(CancellationToken));

        var eventId = await EventIdAsync(tenant, flagId);
        var instance = Assert.Single(await InstancesAsync(tenant, "RoleApprove", subjectId));
        Assert.Equal((ProcessStarts.Running, tenant.FirstReleaseId), (instance.State, instance.ReleaseId));
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
                ["trigger"] = Sync,
                ["eventId"] = eventId,
            },
            JsonNode.Parse(details!)));

        await fixture.RunUntilIdleAsync("a", CancellationToken);

        Assert.Equal(instance.Id, Assert.Single(await InstancesAsync(tenant, "RoleApprove", subjectId)).Id);
        Assert.Equal(0L, await ReceiptCountAsync(tenant, Sync, eventId));
        Assert.Equal(ProcessStarts.Completed, (await tenant.InstanceAsync(flagId)).State);
    }

    [Fact]
    public async Task Sync_start_that_fails_rolls_back_the_raising_step_with_its_event_and_fails_the_raising_instance()
    {
        var tenant = fixture.Tenant("a");
        var subjectId = await tenant.InsertRequestAsync(500m, 4);
        var flagId = await tenant.StartAsync("Flag", subjectId, tenant.FirstReleaseId);

        // The injected error fires only for this test's record, so no real start is affected.
        await tenant.ScalarAsync(
            $$"""
            CREATE FUNCTION axis.test_injected_start_failure() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF NEW.subject_id = '{{subjectId:D}}'::uuid THEN
                    RAISE EXCEPTION 'injected start failure';
                END IF;
                RETURN NEW;
            END
            $body$;
            CREATE TRIGGER test_injected_start_failure BEFORE INSERT ON axis.process_instances
                FOR EACH ROW EXECUTE FUNCTION axis.test_injected_start_failure();
            """);
        try
        {
            await fixture.RunUntilIdleAsync("a", CancellationToken);
        }
        finally
        {
            await tenant.ScalarAsync(
                """
                DROP TRIGGER test_injected_start_failure ON axis.process_instances;
                DROP FUNCTION axis.test_injected_start_failure();
                """);
        }

        Assert.Equal(ProcessStarts.Failed, (await tenant.InstanceAsync(flagId)).State);
        var failure = Assert.Single(await tenant.HistoryAsync(flagId));
        Assert.Equal("flag", failure.Step);
        Assert.Equal("Npgsql.PostgresException", failure.Error);
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.events WHERE process_instance_id = @id", ("id", flagId)));
        Assert.Empty(await InstancesAsync(tenant, "RoleApprove", subjectId));
        Assert.Empty(await InstancesAsync(tenant, "Review", subjectId));
    }

    [Fact]
    public async Task Sync_start_skipped_because_the_process_already_runs_lets_the_raising_step_commit_with_its_event()
    {
        var tenant = fixture.Tenant("a");
        var subjectId = await tenant.InsertRequestAsync(500m, 4);
        var runningId = await tenant.StartAsync("RoleApprove", subjectId, tenant.FirstReleaseId);
        await fixture.RunUntilIdleAsync("a", CancellationToken);
        Assert.Equal(ProcessStarts.Waiting, (await tenant.InstanceAsync(runningId)).State);

        var flagId = await tenant.StartAsync("Flag", subjectId, tenant.FirstReleaseId);
        await fixture.RunUntilIdleAsync("a", CancellationToken);

        Assert.Equal(ProcessStarts.Completed, (await tenant.InstanceAsync(flagId)).State);
        Assert.Equal(1L, await tenant.CountAsync("SELECT count(*) FROM axis.events WHERE process_instance_id = @id", ("id", flagId)));
        Assert.Equal(runningId, Assert.Single(await InstancesAsync(tenant, "RoleApprove", subjectId)).Id);

        var skip = Assert.Single(
            fixture.Logs.Entries,
            entry => entry.Contains(Sync, StringComparison.Ordinal) && entry.Contains(subjectId.ToString(), StringComparison.Ordinal));
        Assert.StartsWith("Information: ", skip, StringComparison.Ordinal);
        Assert.Contains("the process already runs for the record", skip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Event_with_a_sync_and_an_async_trigger_starts_each_process_once()
    {
        var tenant = fixture.Tenant("a");
        var subjectId = await tenant.InsertRequestAsync(500m, 4);
        var flagId = await tenant.StartAsync("Flag", subjectId, tenant.FirstReleaseId);
        await fixture.RunUntilIdleAsync("a", CancellationToken);

        Assert.Equal(ProcessStarts.Completed, (await tenant.InstanceAsync(flagId)).State);
        var eventId = await EventIdAsync(tenant, flagId);
        Assert.Single(await InstancesAsync(tenant, "RoleApprove", subjectId));
        var review = Assert.Single(await InstancesAsync(tenant, "Review", subjectId));
        Assert.Equal(("started", (Guid?)review.Id), await ReceiptAsync(tenant, Async, eventId));
        Assert.Equal(0L, await ReceiptCountAsync(tenant, Sync, eventId));
    }

    private static async Task<Guid> EventIdAsync(ProcessStepFixture tenant, Guid instanceId) =>
        (Guid)(await tenant.ScalarAsync("SELECT id FROM axis.events WHERE process_instance_id = @id", ("id", instanceId)))!;

    private static Guid TriggerId(ProcessStepFixture tenant, string trigger) =>
        Assert.Single(tenant.Model.Triggers, candidate => candidate.Name == trigger).Id;

    /// <summary>The number of receipts the trigger has for the occurrence.</summary>
    private static Task<long> ReceiptCountAsync(ProcessStepFixture tenant, string trigger, Guid eventId) =>
        tenant.CountAsync(
            "SELECT count(*) FROM axis.trigger_starts WHERE trigger_id = @trigger AND source = @source",
            ("trigger", TriggerId(tenant, trigger)),
            ("source", eventId.ToString("D")));

    /// <summary>The outcome and instance of the trigger's only receipt for the occurrence.</summary>
    private static async Task<(string Outcome, Guid? InstanceId)> ReceiptAsync(ProcessStepFixture tenant, string trigger, Guid eventId)
    {
        await using var command = tenant.DataSource.CreateCommand(
            "SELECT outcome, process_instance_id FROM axis.trigger_starts WHERE trigger_id = @trigger AND source = @source");
        command.Parameters.AddWithValue("trigger", TriggerId(tenant, trigger));
        command.Parameters.AddWithValue("source", eventId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        var receipt = (reader.GetString(0), reader.IsDBNull(1) ? (Guid?)null : reader.GetGuid(1));
        Assert.False(await reader.ReadAsync(CancellationToken));
        return receipt;
    }

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
}
