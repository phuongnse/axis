using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

/// <summary>
/// The worker's run of schedule trigger occurrences. The fixture's trigger <c>ReviewLargeRequests</c>
/// starts <c>RoleApprove</c> at 06:00 Berlin time for each request with an amount of at least 1000.
/// A test sets the stored occurrence in the past instead of waiting for it.
/// </summary>
public sealed class ScheduleTriggerTests(ScheduleTriggerFixture fixture) : IClassFixture<ScheduleTriggerFixture>
{
    private const string Trigger = "ReviewLargeRequests";

    private static readonly DateTimeOffset _occurrence = DateTimeOffset.Parse("2026-01-01T05:00:00Z");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Activation_stores_the_next_occurrence_of_each_schedule_trigger_and_a_release_without_it_removes_both()
    {
        var tenant = fixture.Tenant("b");

        var state = Assert.Single(await tenant.TriggerSchedulesAsync());
        var item = Assert.Single(await tenant.ScheduleItemsAsync());
        Assert.Equal((state.TriggerId, state.NextOccurrenceAt), (item.TriggerId, item.DueAt));
        Assert.Null(state.LastOccurrenceAt);
        Assert.Equal(tenant.FirstReleaseId, state.ReleaseId);
        Assert.Equal(tenant.Model.Manifest.Id, state.ApplicationId);
        AssertNextBerlinSixOClock(state.NextOccurrenceAt);

        await tenant.ActivateAsync(ProcessStepFixture.StepAppFolder, []);

        Assert.Empty(await tenant.TriggerSchedulesAsync());
        Assert.Empty(await tenant.ScheduleItemsAsync());
    }

    [Fact]
    public async Task Occurrence_starts_the_process_once_for_each_record_of_the_data_source_and_moves_to_the_next_occurrence()
    {
        var tenant = fixture.Tenant("a");
        var large = new[] { await tenant.InsertRequestAsync(2000m, 1), await tenant.InsertRequestAsync(1500m, 1) };
        var small = await tenant.InsertRequestAsync(10m, 1);
        await DueAtAsync(tenant, _occurrence);

        await ScheduleTriggerFixture.RunUntilIdleAsync("a", fixture.NewWorker(), CancellationToken);

        foreach (var id in large)
        {
            var instance = Assert.Single(await InstancesAsync(tenant, id));
            Assert.Equal((ProcessStarts.Waiting, tenant.FirstReleaseId), (instance.State, instance.ReleaseId));
            Assert.Equal(("started", (Guid?)instance.Id), await ReceiptAsync(tenant, id));
        }

        Assert.Empty(await InstancesAsync(tenant, small));
        Assert.Equal(2L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts"));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.TriggerFanOutWorkItemKind)));

        // The item and the state wait for the next 06:00 in Berlin.
        var state = Assert.Single(await tenant.TriggerSchedulesAsync());
        var item = Assert.Single(await tenant.ScheduleItemsAsync());
        Assert.Equal(_occurrence, state.LastOccurrenceAt);
        Assert.Equal(state.NextOccurrenceAt, item.DueAt);
        AssertNextBerlinSixOClock(state.NextOccurrenceAt);

        Assert.True(tenant.Model.TryGetProcess("RoleApprove", out var process));
        var details = (string?)await tenant.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE record_id = @id AND action = @action AND actor = 'system'",
            ("id", large[0]),
            ("action", AuditActions.ProcessStarted));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["processId"] = process.Id,
                ["releaseId"] = tenant.FirstReleaseId,
                ["trigger"] = Trigger,
                ["occurrenceAt"] = "2026-01-01T05:00:00Z",
            },
            JsonNode.Parse(details!)));
    }

    [Fact]
    public async Task Two_workers_polling_the_same_tenant_run_one_occurrence_once()
    {
        var tenant = fixture.Tenant("c");
        var large = new[] { await tenant.InsertRequestAsync(3000m, 1), await tenant.InsertRequestAsync(1000m, 1), await tenant.InsertRequestAsync(1200m, 1) };
        await DueAtAsync(tenant, _occurrence);
        var first = fixture.NewWorker();
        var second = fixture.NewWorker();

        // Both workers poll while the occurrence runs.
        await Task.WhenAll(
            ScheduleTriggerFixture.RunUntilIdleAsync("c", first, CancellationToken),
            ScheduleTriggerFixture.RunUntilIdleAsync("c", second, CancellationToken));

        // Two more fan-outs of the occurrence stand for deliveries that a crashed worker ran. Both
        // workers run them at once and may try to start the same records.
        var state = Assert.Single(await tenant.TriggerSchedulesAsync());
        await tenant.EnqueueFanOutAsync(state.TriggerId, state.ReleaseId, _occurrence);
        await tenant.EnqueueFanOutAsync(state.TriggerId, state.ReleaseId, _occurrence);
        await Task.WhenAll(
            ScheduleTriggerFixture.RunUntilIdleAsync("c", first, CancellationToken),
            ScheduleTriggerFixture.RunUntilIdleAsync("c", second, CancellationToken));

        foreach (var id in large)
        {
            var instance = Assert.Single(await InstancesAsync(tenant, id));
            Assert.Equal(("started", (Guid?)instance.Id), await ReceiptAsync(tenant, id));
            Assert.Equal(
                1L,
                await tenant.CountAsync(
                    "SELECT count(*) FROM axis.audit_records WHERE record_id = @id AND action = @action",
                    ("id", id),
                    ("action", AuditActions.ProcessStarted)));
        }

        Assert.Equal(3L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts"));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.TriggerFanOutWorkItemKind)));
    }

    private static void AssertNextBerlinSixOClock(DateTimeOffset next)
    {
        var berlin = TimeZoneInfo.ConvertTime(next, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
        Assert.Equal((6, 0, 0), (berlin.Hour, berlin.Minute, berlin.Second));
        Assert.True(next > DateTimeOffset.UtcNow);

        // The next 06:00 is a day away at most, or 25 hours on the day the clocks go back.
        Assert.True(next <= DateTimeOffset.UtcNow.AddHours(25));
    }

    /// <summary>Makes the trigger's stored occurrence and its schedule item due at <paramref name="occurrence"/>.</summary>
    private static async Task DueAtAsync(ProcessStepFixture tenant, DateTimeOffset occurrence)
    {
        await tenant.ScalarAsync("UPDATE axis.trigger_schedules SET next_occurrence_at = @at", ("at", occurrence));
        await tenant.ScalarAsync("UPDATE axis.process_work_items SET due_at = @at WHERE kind = @kind", ("at", occurrence), ("kind", ProcessStarts.TriggerScheduleWorkItemKind));
    }

    private static async Task<IReadOnlyList<(Guid Id, string State, Guid ReleaseId)>> InstancesAsync(ProcessStepFixture tenant, Guid subjectId)
    {
        await using var command = tenant.DataSource.CreateCommand(
            """
            SELECT i.id, i.state, i.release_id FROM axis.process_instances i
            WHERE i.subject_id = @subject AND i.process_id = @process
            """);
        Assert.True(tenant.Model.TryGetProcess("RoleApprove", out var process));
        command.Parameters.AddWithValue("subject", subjectId);
        command.Parameters.AddWithValue("process", process.Id);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(Guid, string, Guid)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2)));
        }

        return rows;
    }

    private static async Task<(string Outcome, Guid? InstanceId)> ReceiptAsync(ProcessStepFixture tenant, Guid subjectId)
    {
        await using var command = tenant.DataSource.CreateCommand(
            """
            SELECT outcome, process_instance_id FROM axis.trigger_starts
            WHERE trigger_id = (SELECT trigger_id FROM axis.trigger_schedules) AND source = '2026-01-01T05:00:00Z' AND subject_id = @subject
            """);
        command.Parameters.AddWithValue("subject", subjectId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }
}
