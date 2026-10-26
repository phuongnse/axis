using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

public sealed class ProcessTimerTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Wait_timer_leaves_the_instance_waiting_with_one_timer_item_and_a_later_run_moves_it_on()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("TimerWait", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Waiting, 2L, "pause", false), await fixture.InstanceAsync(instanceId));
        var row = Assert.Single(await fixture.HistoryAsync(instanceId));
        Assert.Equal(("pause", 1L, null), (row.Step, row.Revision, row.Error));
        var output = JsonNode.Parse(row.Output!)!;
        Assert.Equal(ProcessStarts.Waiting, output["state"]!.GetValue<string>());

        // The step's time is its transaction time, which the history stores as started_at.
        Assert.Equal(
            1L,
            await fixture.CountAsync(
                """
                SELECT count(*) FROM axis.process_work_items w, axis.process_step_history h
                WHERE w.process_instance_id = @id AND w.kind = @kind AND w.revision = 2
                  AND h.process_instance_id = @id AND h.step = 'pause' AND w.due_at = h.started_at + interval '1 hour'
                """,
                ("id", instanceId),
                ("kind", ProcessStarts.TimerWorkItemKind)));
        Assert.Equal(1L, await WorkItemCountAsync(instanceId));
        var dueAt = Assert.IsType<DateTime>(await fixture.ScalarAsync(
            "SELECT due_at FROM axis.process_work_items WHERE process_instance_id = @id",
            ("id", instanceId)));
        Assert.Equal(
            new DateTimeOffset(dueAt),
            DateTimeOffset.Parse(output["dueAt"]!.GetValue<string>(), CultureInfo.InvariantCulture));
        Assert.Empty(await fixture.SystemAuditActionsAsync(instanceId));
        Assert.Null((await fixture.RequestAsync(subjectId)).Values["status"]);
    }

    [Fact]
    public async Task Wait_timer_that_is_due_moves_the_instance_to_its_next_step_and_the_process_completes()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("TimerWait", subjectId, fixture.FirstReleaseId);
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal(ProcessStarts.Waiting, (await fixture.InstanceAsync(instanceId)).State);

        await MakeTimerDueAsync(instanceId);
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 5L, "done", true), await fixture.InstanceAsync(instanceId));
        Assert.Equal(0L, await WorkItemCountAsync(instanceId));
        var history = await fixture.HistoryAsync(instanceId);
        Assert.Equal(["pause", "pause", "mark", "done"], history.Select(row => row.Step));
        Assert.Equal([1L, 2L, 3L, 4L], history.Select(row => row.Revision));
        Assert.Equal("""{"next": "mark"}""", history[1].Output);
        Assert.Null(history[1].Error);
        Assert.Equal("""{"next": "mark", "step": "pause"}""", await StepCompletedDetailsAsync(instanceId, "pause"));
        Assert.Equal("submitted", (await fixture.RequestAsync(subjectId)).Values["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Until_in_the_past_moves_the_instance_on_in_the_same_run()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        await fixture.SetSubmittedAtAsync(subjectId, DateTimeOffset.UtcNow.AddDays(-1));
        var instanceId = await fixture.StartAsync("TimerUntil", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Completed, 4L, "done", true), await fixture.InstanceAsync(instanceId));
        Assert.Equal(0L, await WorkItemCountAsync(instanceId));
        var history = await fixture.HistoryAsync(instanceId);
        Assert.Equal(["pause", "pause", "done"], history.Select(row => row.Step));
        Assert.Equal(ProcessStarts.Waiting, JsonNode.Parse(history[0].Output!)!["state"]!.GetValue<string>());
        Assert.Equal("""{"next": "done"}""", history[1].Output);
    }

    [Fact]
    public async Task Until_that_gives_null_fails_the_step_and_the_instance()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("TimerUntil", subjectId, fixture.FirstReleaseId);

        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal((ProcessStarts.Failed, 2L, "pause", true), await fixture.InstanceAsync(instanceId));
        Assert.Equal(0L, await WorkItemCountAsync(instanceId));
        var row = Assert.Single(await fixture.HistoryAsync(instanceId));
        Assert.Equal("pause", row.Step);
        Assert.Equal("The until of the timer step 'pause' gave no time.", row.Error);
        Assert.Null(row.Output);
        Assert.Equal([AuditActions.ProcessFailed], await fixture.SystemAuditActionsAsync(instanceId));
    }

    [Fact]
    public async Task Stale_timer_item_of_a_cancelled_instance_is_deleted_and_changes_nothing_else()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("TimerWait", subjectId, fixture.FirstReleaseId);
        await fixture.RunUntilIdleAsync(CancellationToken);
        Assert.Equal(1L, await WorkItemCountAsync(instanceId));

        // The cancel command deletes the instance's items. A stale item exists when a worker claimed it
        // before the cancel committed, so the test marks the instance with SQL and keeps the item.
        await fixture.ScalarAsync(
            "UPDATE axis.process_instances SET state = 'cancelled', revision = revision + 1, ended_at = now() WHERE id = @id RETURNING 1",
            ("id", instanceId));
        var cancelled = await fixture.InstanceAsync(instanceId);
        Assert.Equal(ProcessStarts.Cancelled, cancelled.State);
        var history = await fixture.HistoryAsync(instanceId);
        var audit = await fixture.SystemAuditActionsAsync(instanceId);

        await MakeTimerDueAsync(instanceId);
        await fixture.RunUntilIdleAsync(CancellationToken);

        Assert.Equal(0L, await WorkItemCountAsync(instanceId));
        Assert.Equal(cancelled, await fixture.InstanceAsync(instanceId));
        Assert.Equal(history.Count, (await fixture.HistoryAsync(instanceId)).Count);
        Assert.Equal(audit, await fixture.SystemAuditActionsAsync(instanceId));
    }

    private async Task MakeTimerDueAsync(Guid instanceId) =>
        await fixture.ScalarAsync(
            "UPDATE axis.process_work_items SET due_at = now() - interval '1 minute' WHERE process_instance_id = @id AND kind = @kind RETURNING 1",
            ("id", instanceId),
            ("kind", ProcessStarts.TimerWorkItemKind));

    private Task<long> WorkItemCountAsync(Guid instanceId) =>
        fixture.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE process_instance_id = @id", ("id", instanceId));

    private async Task<string?> StepCompletedDetailsAsync(Guid instanceId, string step) =>
        (string?)await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action AND actor = 'system' AND details->>'step' = @step",
            ("id", instanceId),
            ("action", AuditActions.ProcessStepCompleted),
            ("step", step));
}
