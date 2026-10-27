using System.Text.Json.Nodes;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

/// <summary>
/// The wake of process instances that wait in a <c>waitEvent</c> step, by the <c>event.deliver</c> item of
/// the event they wait for. StepApp's <c>WaitEvent</c> waits for <c>RequestNoted</c>, which has async
/// triggers, with a <c>when</c> and a timeout. <c>WaitWatched</c> waits for <c>RequestWatched</c>, which
/// no trigger listens to. <c>WaitTwice</c> passes a wait for <c>RequestWatched</c> two times. A test writes
/// the occurrence straight into the table, as the step tests do, and queues its delivery itself.
/// </summary>
public sealed class EventWakeTests(EventDeliveryFixture fixture) : IClassFixture<EventDeliveryFixture>
{
    private const string Noted = """{"title":"t","ratio":2}""";

    private const string Watched = """{"title":"t","ratio":1}""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Delivered_occurrence_resumes_the_waiting_instance_at_next()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await StartWaitingAsync(tenant, "WaitEvent");

        var eventId = await RaiseAndDeliverAsync(tenant, "RequestNoted", subjectId, instanceId, Noted);

        Assert.Equal((ProcessStarts.Completed, 4L, "done", true), await tenant.InstanceAsync(instanceId));
        var history = await tenant.HistoryAsync(instanceId);
        Assert.Equal(["await", "await", "done"], history.Select(row => row.Step));
        var output = JsonNode.Parse(history[1].Output!)!;
        Assert.Equal(("done", eventId), (output["next"]!.GetValue<string>(), output["eventId"]!.GetValue<Guid>()));
        Assert.Equal(1L, await MatchCountAsync(tenant, instanceId));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE event_id = @id", ("id", eventId)));
        Assert.Equal("""{"next": "done", "step": "await"}""", await StepCompletedDetailsAsync(tenant, instanceId));
    }

    [Fact]
    public async Task Delivered_occurrence_of_an_event_with_no_trigger_resumes_the_waiting_instance()
    {
        var tenant = fixture.Tenant("a");
        Assert.DoesNotContain(tenant.Model.Triggers, trigger => trigger.Event?.Name == "RequestWatched");
        var (subjectId, instanceId) = await StartWaitingAsync(tenant, "WaitWatched");

        var eventId = await RaiseAndDeliverAsync(tenant, "RequestWatched", subjectId, instanceId, Watched);

        Assert.Equal((ProcessStarts.Completed, 4L, "done", true), await tenant.InstanceAsync(instanceId));
        Assert.Equal(1L, await MatchCountAsync(tenant, instanceId));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE event_id = @id", ("id", eventId)));
    }

    [Fact]
    public async Task Occurrence_with_a_false_when_leaves_the_instance_waiting()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await StartWaitingAsync(tenant, "WaitEvent");

        await RaiseAndDeliverAsync(tenant, "RequestNoted", subjectId, instanceId, """{"title":"t","ratio":0}""");

        Assert.Equal((ProcessStarts.Waiting, 2L, "await", false), await tenant.InstanceAsync(instanceId));
        Assert.Equal(0L, await MatchCountAsync(tenant, instanceId));
    }

    [Fact]
    public async Task Occurrence_about_another_record_leaves_the_instance_waiting()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await StartWaitingAsync(tenant, "WaitWatched");
        var otherId = await tenant.InsertRequestAsync(500m, 1);

        await RaiseAndDeliverAsync(tenant, "RequestWatched", otherId, instanceId, Watched);

        Assert.Equal((ProcessStarts.Waiting, 2L, "await", false), await tenant.InstanceAsync(instanceId));
        Assert.Equal(0L, await MatchCountAsync(tenant, instanceId));
    }

    [Fact]
    public async Task Loop_that_waits_twice_uses_each_occurrence_once()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await StartWaitingAsync(tenant, "WaitTwice");

        var first = await RaiseAndDeliverAsync(tenant, "RequestWatched", subjectId, instanceId, Watched);
        Assert.Equal((ProcessStarts.Waiting, "await"), Position(await tenant.InstanceAsync(instanceId)));
        Assert.Equal(1L, await MatchCountAsync(tenant, instanceId));

        // The first occurrence arrives again while the instance waits in a later pass. It is used up.
        await fixture.EnqueueDeliveryAsync("a", first);
        await fixture.RunUntilIdleAsync("a", CancellationToken);
        Assert.Equal((ProcessStarts.Waiting, "await"), Position(await tenant.InstanceAsync(instanceId)));
        Assert.Equal(1L, await MatchCountAsync(tenant, instanceId));

        var second = await RaiseAndDeliverAsync(tenant, "RequestWatched", subjectId, instanceId, Watched);

        Assert.Equal((ProcessStarts.Completed, "done"), Position(await tenant.InstanceAsync(instanceId)));
        Assert.Equal(2L, await MatchCountAsync(tenant, instanceId));
        Assert.Equal(
            [first, second],
            await MatchedEventsAsync(tenant, instanceId));
        var wakes = (await tenant.HistoryAsync(instanceId))
            .Where(row => row.Step == "await" && row.Output is not null && row.Output.Contains("eventId", StringComparison.Ordinal))
            .Select(row => JsonNode.Parse(row.Output!)!["eventId"]!.GetValue<Guid>());
        Assert.Equal([first, second], wakes.Order());
        Assert.Equal(2L, await tenant.CountAsync(
            "SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.stepCompleted' AND details->>'step' = 'await'",
            ("id", instanceId)));
    }

    [Fact]
    public async Task Delivery_run_again_changes_no_instance_history_or_match_row()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await StartWaitingAsync(tenant, "WaitWatched");
        var eventId = await RaiseAndDeliverAsync(tenant, "RequestWatched", subjectId, instanceId, Watched);
        var before = await SnapshotAsync(tenant, instanceId);
        Assert.Equal((ProcessStarts.Completed, 4L, "done", true), before.Instance);

        // A second item for the same occurrence stands for a delivery that a crashed worker ran.
        await fixture.EnqueueDeliveryAsync("a", eventId);
        await fixture.RestartWorkerAsync();
        await fixture.RunUntilIdleAsync("a", CancellationToken);

        Assert.Equal(before, await SnapshotAsync(tenant, instanceId));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE event_id = @id", ("id", eventId)));
    }

    [Fact]
    public async Task Occurrence_delivered_after_the_timeout_leaves_the_instance_at_the_timeout_step()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await StartWaitingAsync(tenant, "WaitEvent");
        await tenant.ScalarAsync(
            "UPDATE axis.process_work_items SET due_at = now() - interval '1 minute' WHERE process_instance_id = @id AND kind = @kind RETURNING 1",
            ("id", instanceId),
            ("kind", ProcessStarts.TimerWorkItemKind));
        await fixture.RunUntilIdleAsync("a", CancellationToken);
        Assert.Equal((ProcessStarts.Completed, 4L, "timedOut", true), await tenant.InstanceAsync(instanceId));
        var before = await SnapshotAsync(tenant, instanceId);

        await RaiseAndDeliverAsync(tenant, "RequestNoted", subjectId, instanceId, Noted);

        Assert.Equal(before, await SnapshotAsync(tenant, instanceId));
        Assert.Equal(0L, await MatchCountAsync(tenant, instanceId));
    }

    /// <summary>Starts <paramref name="process"/> for a new request and runs it until it waits.</summary>
    private async Task<(Guid SubjectId, Guid InstanceId)> StartWaitingAsync(ProcessStepFixture tenant, string process)
    {
        var subjectId = await tenant.InsertRequestAsync(500m, 1);
        var instanceId = await tenant.StartAsync(process, subjectId, tenant.FirstReleaseId);
        await fixture.RunUntilIdleAsync("a", CancellationToken);
        Assert.Equal((ProcessStarts.Waiting, 2L, "await", false), await tenant.InstanceAsync(instanceId));
        return (subjectId, instanceId);
    }

    /// <summary>
    /// Writes an occurrence of <paramref name="eventName"/> about <paramref name="subjectId"/> straight into
    /// the table, queues its delivery and runs the tenant's work until none is left. The instance only stands for the raiser.
    /// </summary>
    private async Task<Guid> RaiseAndDeliverAsync(ProcessStepFixture tenant, string eventName, Guid subjectId, Guid raiserId, string payload)
    {
        Assert.True(tenant.Model.TryGetEntity("Request", out var request));
        var eventId = Guid.CreateVersion7();
        await tenant.ScalarAsync(
            """
            INSERT INTO axis.events (id, application_id, event, release_id, entity_id, subject_id, payload, process_instance_id, raised_at)
            VALUES (@id, @application, @event, @release, @entity, @subject, @payload::jsonb, @instance, now()) RETURNING 1
            """,
            ("id", eventId),
            ("application", tenant.Model.Manifest.Id),
            ("event", eventName),
            ("release", tenant.FirstReleaseId),
            ("entity", request.Id),
            ("subject", subjectId),
            ("payload", payload),
            ("instance", raiserId));
        await fixture.EnqueueDeliveryAsync("a", eventId);
        await fixture.RunUntilIdleAsync("a", CancellationToken);
        return eventId;
    }

    private static (string State, string Step) Position((string State, long Revision, string Step, bool Ended) instance) => (instance.State, instance.Step);

    private static Task<long> MatchCountAsync(ProcessStepFixture tenant, Guid instanceId) =>
        tenant.CountAsync("SELECT count(*) FROM axis.process_event_matches WHERE process_instance_id = @id", ("id", instanceId));

    private static async Task<IReadOnlyList<Guid>> MatchedEventsAsync(ProcessStepFixture tenant, Guid instanceId)
    {
        await using var command = tenant.DataSource.CreateCommand(
            "SELECT event_id FROM axis.process_event_matches WHERE process_instance_id = @id ORDER BY event_id");
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(CancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private static async Task<string?> StepCompletedDetailsAsync(ProcessStepFixture tenant, Guid instanceId) =>
        (string?)await tenant.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = 'process.stepCompleted' AND actor = 'system' AND details->>'step' = 'await'",
            ("id", instanceId));

    /// <summary>The instance, the numbers of its history, audit and match rows, and its step work items.</summary>
    private static async Task<((string, long, string, bool) Instance, long History, long Audit, long Matches, long Steps)> SnapshotAsync(ProcessStepFixture tenant, Guid instanceId) =>
        (
            await tenant.InstanceAsync(instanceId),
            await tenant.CountAsync("SELECT count(*) FROM axis.process_step_history WHERE process_instance_id = @id", ("id", instanceId)),
            await tenant.CountAsync("SELECT count(*) FROM axis.audit_records WHERE process_instance_id = @id", ("id", instanceId)),
            await MatchCountAsync(tenant, instanceId),
            await tenant.CountAsync(
                "SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id AND kind = @kind",
                ("id", instanceId),
                ("kind", ProcessStarts.StepWorkItemKind)));
}
