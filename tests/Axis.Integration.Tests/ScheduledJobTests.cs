using System.Globalization;
using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

/// <summary>
/// The worker's run of a scheduled job, a schedule trigger without a data source. The fixture's
/// trigger <c>TallyDaily</c> starts <c>Tally</c>, a process without a subject, once for each occurrence
/// with its fixed parameters. Its receipt has the nil UUID as the record. The tests share tenant <c>l</c>,
/// so each one uses occurrences of its own and compares counts with values it read before.
/// </summary>
public sealed class ScheduledJobTests(ScheduleTriggerFixture fixture) : IClassFixture<ScheduleTriggerFixture>
{
    private const string Trigger = "TallyDaily";

    private static readonly DateTimeOffset _firstTestStart = DateTimeOffset.Parse("2026-01-01T05:00:00Z");

    private static readonly DateTimeOffset _secondTestStart = DateTimeOffset.Parse("2026-02-01T05:00:00Z");

    private static readonly DateTimeOffset _thirdTestStart = DateTimeOffset.Parse("2026-03-01T05:00:00Z");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_occurrence_starts_the_process_once_with_the_trigger_parameters_and_a_repeat_starts_nothing()
    {
        var tenant = fixture.Tenant("l");
        var state = Assert.Single(await tenant.TriggerSchedulesAsync());
        var worker = fixture.NewWorker();
        var first = _firstTestStart;
        var second = _firstTestStart.AddDays(1);

        foreach (var occurrence in new[] { first, second })
        {
            await tenant.EnqueueFanOutAsync(state.TriggerId, state.ReleaseId, occurrence);
            await fixture.RunAsync("l", worker, 1, CancellationToken);
            await ScheduleTriggerFixture.RunUntilIdleAsync("l", worker, CancellationToken);
        }

        Assert.True(tenant.Model.TryGetProcess("Tally", out var process));
        foreach (var occurrence in new[] { first, second })
        {
            var (outcome, instanceId) = await ReceiptAsync(tenant, state.TriggerId, occurrence);
            Assert.Equal("started", outcome);
            Assert.NotNull(instanceId);

            // The instance has no subject, holds the trigger's values in the stored forms and has run to its end.
            Assert.Equal(
                (true, "3", "2026-01-01T00:00:00.000000Z", "completed"),
                await InstanceAsync(tenant, instanceId.Value));

            var details = (string?)await tenant.ScalarAsync(
                "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @instance AND action = @action AND actor = 'system'",
                ("instance", instanceId.Value),
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

        // A fan-out of the first occurrence again, as after a crash, starts nothing.
        var before = await InstanceCountAsync(tenant, process.Id);
        await tenant.EnqueueFanOutAsync(state.TriggerId, state.ReleaseId, first);
        await fixture.RunAsync("l", worker, 1, CancellationToken);
        await ScheduleTriggerFixture.RunUntilIdleAsync("l", worker, CancellationToken);

        Assert.Equal(before, await InstanceCountAsync(tenant, process.Id));
        Assert.Equal("started", (await ReceiptAsync(tenant, state.TriggerId, first)).Outcome);
    }

    [Fact]
    public async Task An_occurrence_while_the_process_still_runs_is_skipped_and_logged()
    {
        var tenant = fixture.Tenant("l");
        var state = Assert.Single(await tenant.TriggerSchedulesAsync());
        Assert.True(tenant.Model.TryGetProcess("Tally", out var process));
        var worker = fixture.NewWorker();
        var before = await InstanceCountAsync(tenant, process.Id);

        // The first fan-out starts the instance, and its step has not run, so the instance is still running.
        var first = _secondTestStart;
        await tenant.EnqueueFanOutAsync(state.TriggerId, state.ReleaseId, first);
        await fixture.RunAsync("l", worker, 1, CancellationToken);
        Assert.Equal(before + 1, await InstanceCountAsync(tenant, process.Id));
        var (firstOutcome, firstInstance) = await ReceiptAsync(tenant, state.TriggerId, first);
        Assert.Equal("started", firstOutcome);
        Assert.Equal("running", await InstanceStateAsync(tenant, firstInstance!.Value));

        var second = _secondTestStart.AddDays(1);
        await tenant.EnqueueFanOutAsync(state.TriggerId, state.ReleaseId, second);
        await fixture.RunAsync("l", worker, 1, CancellationToken);

        Assert.Equal(before + 1, await InstanceCountAsync(tenant, process.Id));
        Assert.Equal(("skipped", (Guid?)null), await ReceiptAsync(tenant, state.TriggerId, second));
        Assert.Contains(
            fixture.Logs.Entries,
            entry => entry.StartsWith("Information:", StringComparison.Ordinal)
                && entry.Contains(Trigger, StringComparison.Ordinal)
                && entry.Contains(Source(second), StringComparison.Ordinal)
                && entry.Contains("already running", StringComparison.Ordinal));

        await ScheduleTriggerFixture.RunUntilIdleAsync("l", worker, CancellationToken);
    }

    [Fact]
    public async Task Redelivering_the_dead_letter_of_a_scheduled_job_queues_the_fan_out_and_starts_the_process_once()
    {
        var tenant = fixture.Tenant("l");
        var state = Assert.Single(await tenant.TriggerSchedulesAsync());
        Assert.True(tenant.Model.TryGetProcess("Tally", out var process));
        var worker = fixture.NewWorker();
        var occurrence = _thirdTestStart;
        var id = Guid.CreateVersion7();
        var before = await InstanceCountAsync(tenant, process.Id);
        await tenant.ScalarAsync(
            """
            INSERT INTO axis.dead_letters (id, kind, application_id, process_id, trigger_id, occurrence_at, attempts, last_error, state, created_at)
            VALUES (@id, 'trigger.fanOut', @application, @process, @trigger, @occurrence, 5, 'System.InvalidOperationException', 'open', now())
            """,
            ("id", id),
            ("application", tenant.Model.Manifest.Id),
            ("process", process.Id),
            ("trigger", state.TriggerId),
            ("occurrence", occurrence));

        DeadLetterResult result;
        await using (var connection = await tenant.DataSource.OpenConnectionAsync(CancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(CancellationToken))
        {
            result = await DeadLetterCommand.RedeliverAsync(
                connection,
                transaction,
                tenant.Tenant,
                tenant.Model,
                state.ReleaseId,
                process.Id,
                id,
                "maria",
                "The job is fixed.",
                CancellationToken);
            await transaction.CommitAsync(CancellationToken);
        }

        Assert.Equal(DeadLetterOutcome.Settled, result.Outcome);
        Assert.Equal("redelivered", (string?)await tenant.ScalarAsync("SELECT state FROM axis.dead_letters WHERE id = @id", ("id", id)));
        await tenant.ScalarAsync("UPDATE axis.process_work_items SET due_at = now() - interval '1 minute' WHERE kind = @kind", ("kind", ProcessStarts.TriggerFanOutWorkItemKind));
        await fixture.RunAsync("l", worker, 1, CancellationToken);

        Assert.Equal(before + 1, await InstanceCountAsync(tenant, process.Id));
        Assert.Equal("started", (await ReceiptAsync(tenant, state.TriggerId, occurrence)).Outcome);
        await ScheduleTriggerFixture.RunUntilIdleAsync("l", worker, CancellationToken);
    }

    private static string Source(DateTimeOffset occurrence) =>
        occurrence.UtcDateTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture);

    private static async Task<(string Outcome, Guid? InstanceId)> ReceiptAsync(ProcessStepFixture tenant, Guid triggerId, DateTimeOffset occurrence)
    {
        await using var command = tenant.DataSource.CreateCommand(
            "SELECT outcome, process_instance_id FROM axis.trigger_starts WHERE trigger_id = @trigger AND source = @source AND subject_id = @subject");
        command.Parameters.AddWithValue("trigger", triggerId);
        command.Parameters.AddWithValue("source", Source(occurrence));
        command.Parameters.AddWithValue("subject", Guid.Empty);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }

    private static async Task<(bool NoSubject, string? Days, string? Since, string State)> InstanceAsync(ProcessStepFixture tenant, Guid instanceId)
    {
        await using var command = tenant.DataSource.CreateCommand(
            "SELECT subject_id IS NULL, parameters->>'days', parameters->>'since', state FROM axis.process_instances WHERE id = @id");
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetBoolean(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3));
    }

    private static async Task<string> InstanceStateAsync(ProcessStepFixture tenant, Guid instanceId) =>
        (string)(await tenant.ScalarAsync("SELECT state FROM axis.process_instances WHERE id = @id", ("id", instanceId)))!;

    private static Task<long> InstanceCountAsync(ProcessStepFixture tenant, Guid processId) =>
        tenant.CountAsync("SELECT count(*) FROM axis.process_instances WHERE process_id = @process", ("process", processId));
}
