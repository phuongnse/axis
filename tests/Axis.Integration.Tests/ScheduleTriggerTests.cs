using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Processes.Work;

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

        // The stored occurrence is months old, so the worker ran only the latest daily one.
        var state = Assert.Single(await tenant.TriggerSchedulesAsync());
        var occurrence = state.LastOccurrenceAt ?? throw new InvalidOperationException("No occurrence ran.");
        Assert.True(occurrence > _occurrence && occurrence <= DateTimeOffset.UtcNow);
        AssertBerlinSixOClock(occurrence);

        foreach (var id in large)
        {
            var instance = Assert.Single(await InstancesAsync(tenant, id));
            Assert.Equal((ProcessStarts.Waiting, tenant.FirstReleaseId), (instance.State, instance.ReleaseId));
            Assert.Equal(("started", (Guid?)instance.Id), await ReceiptAsync(tenant, id, occurrence));
        }

        Assert.Empty(await InstancesAsync(tenant, small));
        Assert.Equal(2L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts"));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.TriggerFanOutWorkItemKind)));

        // The item and the state wait for the next 06:00 in Berlin.
        var item = Assert.Single(await tenant.ScheduleItemsAsync());
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
                ["occurrenceAt"] = Source(occurrence),
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
        var occurrence = state.LastOccurrenceAt ?? throw new InvalidOperationException("No occurrence ran.");
        await tenant.EnqueueFanOutAsync(state.TriggerId, state.ReleaseId, occurrence);
        await tenant.EnqueueFanOutAsync(state.TriggerId, state.ReleaseId, occurrence);
        await Task.WhenAll(
            ScheduleTriggerFixture.RunUntilIdleAsync("c", first, CancellationToken),
            ScheduleTriggerFixture.RunUntilIdleAsync("c", second, CancellationToken));

        foreach (var id in large)
        {
            var instance = Assert.Single(await InstancesAsync(tenant, id));
            Assert.Equal(("started", (Guid?)instance.Id), await ReceiptAsync(tenant, id, occurrence));
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

    [Fact]
    public async Task Worker_stopped_in_the_middle_of_a_fan_out_leaves_each_record_started_once_and_a_new_worker_completes_it()
    {
        var tenant = fixture.Tenant("d");
        for (var index = 0; index < 1000; index++)
        {
            await tenant.InsertRequestAsync(2000m, 1);
        }

        await DueAtAsync(tenant, _occurrence);

        // The schedule item, then three batches of 100 records.
        await fixture.RunAsync("d", fixture.NewWorker(), 4, CancellationToken);

        // A worker claims the next batch and stops before it commits anything.
        await using (var connection = await tenant.DataSource.OpenConnectionAsync(CancellationToken))
        {
            Assert.NotNull(await WorkItemQueue.ClaimAsync(connection, "d", [ProcessStarts.TriggerFanOutWorkItemKind], TimeSpan.Zero, CancellationToken));
        }

        Assert.Equal(300L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts WHERE outcome = 'started'"));
        Assert.Equal(
            0L,
            await tenant.CountAsync(
                "SELECT count(*) FROM (SELECT subject_id FROM axis.process_instances GROUP BY subject_id HAVING count(*) > 1) d"));
        Assert.Equal(300L, await tenant.CountAsync("SELECT count(*) FROM axis.process_instances"));
        Assert.Equal(1L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind AND cursor IS NOT NULL", ("kind", ProcessStarts.TriggerFanOutWorkItemKind)));

        await ScheduleTriggerFixture.RunUntilIdleAsync("d", fixture.NewWorker(), CancellationToken);

        Assert.Equal(1000L, await tenant.CountAsync("SELECT count(*) FROM axis.process_instances"));
        Assert.Equal(1000L, await tenant.CountAsync("SELECT count(DISTINCT subject_id) FROM axis.process_instances"));
        Assert.Equal(1000L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts WHERE outcome = 'started'"));
        Assert.Equal(1000L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts"));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.TriggerFanOutWorkItemKind)));
    }

    [Fact]
    public async Task Missed_hourly_occurrences_run_only_the_latest_once_and_the_next_is_due_an_hour_later()
    {
        var tenant = fixture.Tenant("e");
        await AssertHourlyInUtcAsync(tenant);
        var record = await tenant.InsertRequestAsync(2000m, 1);
        var stored = await MoveBackThreeHoursAsync(tenant);

        await ScheduleTriggerFixture.RunUntilIdleAsync("e", fixture.NewWorker(), CancellationToken);

        await AssertOnlyTheLatestHourlyOccurrenceRanAsync(tenant, record, stored);
    }

    [Fact]
    public async Task A_release_that_removes_the_trigger_lets_the_running_fan_out_finish_and_runs_no_further_occurrence()
    {
        var tenant = fixture.Tenant("f");
        for (var index = 0; index < 250; index++)
        {
            await tenant.InsertRequestAsync(2000m, 1);
        }

        await DueAtAsync(tenant, _occurrence);
        var worker = fixture.NewWorker();

        // The schedule item, then the first batch of 100 records.
        await fixture.RunAsync("f", worker, 2, CancellationToken);
        Assert.Equal(100L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts"));

        await tenant.ActivateAsync(ProcessStepFixture.StepAppFolder, []);

        Assert.Empty(await tenant.TriggerSchedulesAsync());
        Assert.Empty(await tenant.ScheduleItemsAsync());

        await ScheduleTriggerFixture.RunUntilIdleAsync("f", worker, CancellationToken);

        Assert.Equal(250L, await tenant.CountAsync("SELECT count(*) FROM axis.process_instances"));
        Assert.Equal(250L, await tenant.CountAsync("SELECT count(DISTINCT subject_id) FROM axis.process_instances"));
        Assert.Equal(250L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts WHERE outcome = 'started'"));
        Assert.Equal(1L, await tenant.CountAsync("SELECT count(DISTINCT source) FROM axis.trigger_starts"));

        // No later occurrence runs.
        await ScheduleTriggerFixture.RunUntilIdleAsync("f", worker, CancellationToken);

        Assert.Equal(250L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts"));
        Assert.Equal(
            0L,
            await tenant.CountAsync(
                "SELECT count(*) FROM axis.process_work_items WHERE kind = ANY(@kinds)",
                ("kinds", new[] { ProcessStarts.TriggerScheduleWorkItemKind, ProcessStarts.TriggerFanOutWorkItemKind })));
    }

    [Fact]
    public async Task Activating_the_same_release_again_keeps_a_missed_next_occurrence_and_the_worker_runs_only_the_latest()
    {
        var tenant = fixture.Tenant("g");
        await AssertHourlyInUtcAsync(tenant);
        var record = await tenant.InsertRequestAsync(2000m, 1);
        var stored = await MoveBackThreeHoursAsync(tenant);

        await tenant.ActivateAsync(fixture.HourlyFolder, []);
        await AssertHourlyInUtcAsync(tenant);

        var state = Assert.Single(await tenant.TriggerSchedulesAsync());
        var item = Assert.Single(await tenant.ScheduleItemsAsync());
        Assert.Equal(stored, state.NextOccurrenceAt);
        Assert.Equal(stored, item.DueAt);
        Assert.Null(state.LastOccurrenceAt);

        await ScheduleTriggerFixture.RunUntilIdleAsync("g", fixture.NewWorker(), CancellationToken);

        await AssertOnlyTheLatestHourlyOccurrenceRanAsync(tenant, record, stored);
    }

    [Fact]
    public async Task Fan_out_over_a_data_source_filter_that_reads_a_pending_field_starts_nothing_and_keeps_its_item_until_the_backfill_ends()
    {
        var tenant = fixture.Tenant("i");
        using var folder = new TemporaryFolder();
        foreach (var file in Directory.EnumerateFiles(fixture.Folder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(fixture.Folder, file);
            var content = await File.ReadAllTextAsync(file, CancellationToken);
            content = relativePath switch
            {
                "entities/request.json" => content.Replace(
                    "\"fields\": [",
                    "\"fields\": [\n    { \"name\": \"doubled\", \"type\": \"integer\", \"expression\": \"divisor * 2\" },",
                    StringComparison.Ordinal),
                "data-sources/large-requests.json" => content.Replace("amount >= minAmount", "amount >= minAmount and doubled > 0", StringComparison.Ordinal),
                _ => content,
            };
            folder.With(relativePath, content);
        }

        var large = new[] { await tenant.InsertRequestAsync(2000m, 1), await tenant.InsertRequestAsync(1500m, 1) };

        // R1 computes doubled inline. R2 changes its expression and leaves the backfill to the worker.
        await tenant.ActivateAsync(folder.Path, []);
        folder.With("entities/request.json", (await File.ReadAllTextAsync(Path.Combine(folder.Path, "entities", "request.json"), CancellationToken)).Replace("divisor * 2", "divisor * 3", StringComparison.Ordinal));
        await tenant.ActivateAsync(folder.Path, [], inlineRecomputeLimit: 0);
        Assert.Equal(1L, await tenant.CountAsync("SELECT count(*) FROM axis.pending_computed_fields"));
        await DueAtAsync(tenant, _occurrence);
        var worker = fixture.NewWorker();

        // The schedule item, then the fan-out, which the pending field refuses.
        await fixture.RunAsync("i", worker, 2, CancellationToken);

        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_instances"));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts"));
        Assert.Equal(
            (true, true),
            (
                (bool)(await tenant.ScalarAsync("SELECT due_at > now() FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.TriggerFanOutWorkItemKind)))!,
                (bool)(await tenant.ScalarAsync("SELECT claim_token IS NULL FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.TriggerFanOutWorkItemKind)))!));
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.StartsWith("Warning:", StringComparison.Ordinal) && entry.Contains("being recomputed", StringComparison.Ordinal));

        // Only the backfill is due. Then the fan-out runs again, as it does after its lease.
        await ScheduleTriggerFixture.RunUntilIdleAsync("i", worker, CancellationToken);
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.pending_computed_fields"));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts"));
        await tenant.ScalarAsync("UPDATE axis.process_work_items SET due_at = now() WHERE kind = @kind", ("kind", ProcessStarts.TriggerFanOutWorkItemKind));

        await fixture.RunAsync("i", worker, 1, CancellationToken);

        var occurrence = Assert.Single(await tenant.TriggerSchedulesAsync()).LastOccurrenceAt ?? throw new InvalidOperationException("No occurrence ran.");
        foreach (var id in large)
        {
            var instance = Assert.Single(await InstancesAsync(tenant, id));
            Assert.Equal(("started", (Guid?)instance.Id), await ReceiptAsync(tenant, id, occurrence));
        }

        Assert.Equal(2L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts WHERE outcome = 'started'"));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.TriggerFanOutWorkItemKind)));
    }

    [Fact]
    public async Task Activating_a_release_with_a_changed_schedule_computes_the_next_occurrence_from_the_activation_time()
    {
        var tenant = fixture.Tenant("h");
        await AwayFromTheEndOfTheHourAsync(tenant);
        await DueAtAsync(tenant, _occurrence);

        var (releaseId, _) = await tenant.ActivateAsync(
            fixture.HourlyFolder,
            [],
            activatedAt: DateTimeOffset.Parse("2026-03-04T10:20:30.123456Z", CultureInfo.InvariantCulture));

        var expected = DateTimeOffset.Parse("2026-03-04T11:00:00Z", CultureInfo.InvariantCulture);
        var state = Assert.Single(await tenant.TriggerSchedulesAsync());
        var item = Assert.Single(await tenant.ScheduleItemsAsync());
        Assert.Equal(releaseId, state.ReleaseId);
        Assert.Equal(expected, state.NextOccurrenceAt);
        Assert.Equal(expected, item.DueAt);

        // Each activation changes the schedule, so each one recomputes the next occurrence.
        // Activating exactly on the hour gives the following hour, not the same instant.
        await tenant.ActivateAsync(fixture.Folder, [], activatedAt: DateTimeOffset.Parse("2026-03-04T10:20:30Z", CultureInfo.InvariantCulture));
        var (hourlyReleaseId, _) = await tenant.ActivateAsync(
            fixture.HourlyFolder,
            [],
            activatedAt: DateTimeOffset.Parse("2026-03-04T12:00:00Z", CultureInfo.InvariantCulture));

        var onTheHour = DateTimeOffset.Parse("2026-03-04T13:00:00Z", CultureInfo.InvariantCulture);
        state = Assert.Single(await tenant.TriggerSchedulesAsync());
        item = Assert.Single(await tenant.ScheduleItemsAsync());
        Assert.Equal(hourlyReleaseId, state.ReleaseId);
        Assert.Equal(onTheHour, state.NextOccurrenceAt);
        Assert.Equal(onTheHour, item.DueAt);

        // 00:20Z falls just before Berlin's repeated hour. A Berlin hourly schedule would give 02:00Z, so UTC keeps the hour steps even.
        await tenant.ActivateAsync(fixture.Folder, [], activatedAt: DateTimeOffset.Parse("2026-10-25T00:10:00Z", CultureInfo.InvariantCulture));
        var (autumnReleaseId, _) = await tenant.ActivateAsync(
            fixture.HourlyFolder,
            [],
            activatedAt: DateTimeOffset.Parse("2026-10-25T00:20:00Z", CultureInfo.InvariantCulture));
        await AssertHourlyInUtcAsync(tenant);

        var afterClockChange = DateTimeOffset.Parse("2026-10-25T01:00:00Z", CultureInfo.InvariantCulture);
        state = Assert.Single(await tenant.TriggerSchedulesAsync());
        item = Assert.Single(await tenant.ScheduleItemsAsync());
        Assert.Equal(autumnReleaseId, state.ReleaseId);
        Assert.Equal(afterClockChange, state.NextOccurrenceAt);
        Assert.Equal(afterClockChange, item.DueAt);
    }

    /// <summary>Waits for the next hour when less than a minute of the current one is left, so that no hourly occurrence falls due while the test runs.</summary>
    private static async Task AwayFromTheEndOfTheHourAsync(ProcessStepFixture tenant)
    {
        var left = (TimeSpan)(await tenant.ScalarAsync("SELECT date_trunc('hour', now()) + interval '1 hour' - now()"))!;
        if (left < TimeSpan.FromMinutes(1))
        {
            await Task.Delay(left + TimeSpan.FromSeconds(1), CancellationToken);
        }
    }

    /// <summary>
    /// Checks that the trigger is hourly in UTC. The hourly tests rely on even hour steps, which
    /// Berlin breaks at the autumn clock change.
    /// </summary>
    private static async Task AssertHourlyInUtcAsync(ProcessStepFixture tenant) =>
        Assert.Equal("0 * * * * UTC", await tenant.ScalarAsync("SELECT schedule || ' ' || time_zone FROM axis.trigger_schedules"));

    /// <summary>
    /// Moves the stored next occurrence and the schedule item three hours back, to the start of the
    /// hour, so that three hourly occurrences are missed. Returns the stored time.
    /// </summary>
    private static async Task<DateTimeOffset> MoveBackThreeHoursAsync(ProcessStepFixture tenant)
    {
        await AwayFromTheEndOfTheHourAsync(tenant);
        await tenant.ScalarAsync("UPDATE axis.trigger_schedules SET next_occurrence_at = date_trunc('hour', now()) - interval '2 hours'");
        await tenant.ScalarAsync(
            "UPDATE axis.process_work_items SET due_at = date_trunc('hour', now()) - interval '2 hours' WHERE kind = @kind",
            ("kind", ProcessStarts.TriggerScheduleWorkItemKind));
        return Assert.Single(await tenant.TriggerSchedulesAsync()).NextOccurrenceAt;
    }

    /// <summary>
    /// Checks that the worker skipped the occurrences missed since <paramref name="stored"/> and ran
    /// the latest one once for <paramref name="record"/>, and that the next occurrence is an hour later.
    /// The test waits away from the end of the hour first, so no further occurrence falls due before the checks.
    /// </summary>
    private static async Task AssertOnlyTheLatestHourlyOccurrenceRanAsync(ProcessStepFixture tenant, Guid record, DateTimeOffset stored)
    {
        var state = Assert.Single(await tenant.TriggerSchedulesAsync());
        var item = Assert.Single(await tenant.ScheduleItemsAsync());
        var latest = state.LastOccurrenceAt ?? throw new InvalidOperationException("No occurrence ran.");
        Assert.True(latest >= stored.AddHours(2));
        Assert.True((bool)(await tenant.ScalarAsync("SELECT @latest <= now() AND now() - @latest < interval '1 hour'", ("latest", latest)))!);
        Assert.Equal(latest.AddHours(1), state.NextOccurrenceAt);
        Assert.Equal(state.NextOccurrenceAt, item.DueAt);

        Assert.Equal(1L, await tenant.CountAsync("SELECT count(*) FROM axis.trigger_starts"));
        var instance = Assert.Single(await InstancesAsync(tenant, record));
        Assert.Equal(("started", (Guid?)instance.Id), await ReceiptAsync(tenant, record, latest));
        Assert.Equal(0L, await tenant.CountAsync("SELECT count(*) FROM axis.process_work_items WHERE kind = @kind", ("kind", ProcessStarts.TriggerFanOutWorkItemKind)));
    }

    private static void AssertBerlinSixOClock(DateTimeOffset instant)
    {
        var berlin = TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));
        Assert.Equal((6, 0, 0), (berlin.Hour, berlin.Minute, berlin.Second));
    }

    private static void AssertNextBerlinSixOClock(DateTimeOffset next)
    {
        AssertBerlinSixOClock(next);
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

    private static string Source(DateTimeOffset occurrence) =>
        occurrence.UtcDateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);

    private static async Task<(string Outcome, Guid? InstanceId)> ReceiptAsync(ProcessStepFixture tenant, Guid subjectId, DateTimeOffset occurrence)
    {
        await using var command = tenant.DataSource.CreateCommand(
            """
            SELECT outcome, process_instance_id FROM axis.trigger_starts
            WHERE trigger_id = (SELECT trigger_id FROM axis.trigger_schedules) AND source = @source AND subject_id = @subject
            """);
        command.Parameters.AddWithValue("source", Source(occurrence));
        command.Parameters.AddWithValue("subject", subjectId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }
}
