using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

public sealed class PackageCallTests(PackageCallFixture fixture) : IClassFixture<PackageCallFixture>
{
    private const string Secret = "The input 2 is not valid.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_sum_call_writes_the_total_moves_on_to_next_and_records_the_operation_identity()
    {
        var tenant = fixture.Tenant;
        var (subjectId, instanceId) = await fixture.StageAsync("PackageSum", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync(CancellationToken));

        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await tenant.InstanceAsync(instanceId));
        var record = await fixture.RequestAsync(subjectId);
        Assert.Equal(5L, record.Values["total"]!.GetValue<long>());
        Assert.Equal(2L, record.Version);
        Assert.Equal((PackageCalls.Succeeded, 1, (string?)null, (string?)null), await CallAsync(callId));

        var row = (await tenant.HistoryAsync(instanceId))[1];
        Assert.Equal(("sum", 2L, (string?)null), (row.Step, row.Revision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["operationId"] = callId, ["outcome"] = "succeeded", ["next"] = "done", ["subjectVersion"] = 2 },
            JsonNode.Parse(row.Output!)));

        // The next step's item was queued, and it completes the instance.
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync(CancellationToken));
        Assert.Equal((ProcessStarts.Completed, 4L, "done", true), await tenant.InstanceAsync(instanceId));
    }

    [Fact]
    public async Task Staging_leaves_the_instance_waiting_and_the_call_pending_with_a_history_row_and_one_audit_record()
    {
        var tenant = fixture.Tenant;
        var (_, instanceId) = await fixture.StageAsync("PackageSum", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal((ProcessStarts.Waiting, 2L, "sum", false), await tenant.InstanceAsync(instanceId));
        Assert.Equal((PackageCalls.Pending, 0, (string?)null, "{\"a\": 2, \"b\": 3}"), await CallAsync(callId));
        Assert.Equal(1L, await tenant.CountAsync(
            "SELECT count(*) FROM axis.process_work_items WHERE kind = @kind AND package_call_id = @id",
            ("kind", ProcessStarts.PackageCallWorkItemKind),
            ("id", callId)));

        var row = Assert.Single(await tenant.HistoryAsync(instanceId));
        Assert.Equal(("sum", 1L, (string?)null), (row.Step, row.Revision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["state"] = "waiting", ["operationId"] = callId },
            JsonNode.Parse(row.Output!)));
        Assert.Equal(AuditActions.OperationStaged, Assert.Single(await tenant.SystemAuditActionsAsync(instanceId)));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["step"] = "sum",
                ["package"] = "test-package",
                ["version"] = "1.0.0",
                ["operation"] = "sum",
                ["operationId"] = callId,
            },
            JsonNode.Parse(await AuditDetailsAsync(instanceId, AuditActions.OperationStaged))));
    }

    [Fact]
    public async Task A_settled_sum_call_writes_exactly_one_called_audit_record()
    {
        var tenant = fixture.Tenant;
        var (_, instanceId) = await fixture.StageAsync("PackageSum", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync(CancellationToken));

        // The records of one transaction share its time, so their order is not checked.
        Assert.Equal(
            new[] { AuditActions.OperationStaged, AuditActions.RecordUpdated, AuditActions.OperationCalled }.Order(StringComparer.Ordinal),
            (await tenant.SystemAuditActionsAsync(instanceId)).Order(StringComparer.Ordinal));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["package"] = "test-package",
                ["version"] = "1.0.0",
                ["operation"] = "sum",
                ["operationId"] = callId,
                ["attempts"] = 1,
                ["outcome"] = "succeeded",
            },
            JsonNode.Parse(await AuditDetailsAsync(instanceId, AuditActions.OperationCalled))));
    }

    [Fact]
    public async Task A_fail_call_fails_the_call_and_the_instance_and_records_only_the_exception_type()
    {
        var tenant = fixture.Tenant;
        var (_, instanceId) = await fixture.StageAsync("PackageFail", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallAsync(CancellationToken));

        Assert.Equal((ProcessStarts.Failed, 3L, "fail", true), await tenant.InstanceAsync(instanceId));
        Assert.Equal((PackageCalls.Failed, 1, (string?)"System.FormatException", (string?)null), await CallAsync(callId));

        // The attempt row holds the exception type, the same fixed text as the call.
        var failed = Assert.Single(await AttemptsAsync(callId));
        Assert.Equal(("attempt", 1, "failed", (int?)null, (string?)"System.FormatException"), (failed.Kind, failed.Number, failed.Outcome, failed.HttpStatus, failed.Error));
        var row = (await tenant.HistoryAsync(instanceId))[1];
        Assert.Equal(("fail", 2L, "System.FormatException"), (row.Step, row.Revision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["operationId"] = callId, ["outcome"] = "failed" },
            JsonNode.Parse(row.Output!)));
        Assert.Equal(
            new[] { AuditActions.OperationStaged, AuditActions.OperationCalled, AuditActions.ProcessFailed }.Order(StringComparer.Ordinal),
            (await tenant.SystemAuditActionsAsync(instanceId)).Order(StringComparer.Ordinal));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["package"] = "test-package",
                ["version"] = "1.0.0",
                ["operation"] = "fail",
                ["operationId"] = callId,
                ["attempts"] = 1,
                ["outcome"] = "failed",
            },
            JsonNode.Parse(await AuditDetailsAsync(instanceId, AuditActions.OperationCalled))));
    }

    [Fact]
    public async Task The_exception_message_and_the_stored_input_appear_nowhere_once_the_call_settles()
    {
        var tenant = fixture.Tenant;
        var (_, instanceId) = await fixture.StageAsync("PackageFail", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallAsync(CancellationToken));

        var call = await CallAsync(callId);
        Assert.Null(call.Input);
        var texts = new List<string> { call.LastError!, await AuditDetailsAsync(instanceId) };
        foreach (var attempt in await AttemptsAsync(callId))
        {
            texts.Add(attempt.Error ?? "");
        }

        foreach (var row in await tenant.HistoryAsync(instanceId))
        {
            texts.AddRange([row.Input, row.Output!, row.Error ?? ""]);
        }

        foreach (var text in texts)
        {
            Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_call_found_sending_settles_unknown_and_leaves_the_instance_waiting_without_failing_it()
    {
        var tenant = fixture.Tenant;
        var (subjectId, instanceId) = await fixture.StageAsync("PackageSum", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        // A worker marked the call and crashed before it recorded a result.
        await tenant.ScalarAsync("UPDATE axis.package_calls SET state = 'sending', attempts = 1 WHERE id = @id", ("id", callId));

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync(CancellationToken));

        Assert.Equal((ProcessStarts.Waiting, 2L, "sum", false), await tenant.InstanceAsync(instanceId));
        Assert.Equal(
            (PackageCalls.Unknown, 1, (string?)PackageCallHandler.UnknownOutcome, (string?)"{\"a\": 2, \"b\": 3}"),
            await CallAsync(callId));
        Assert.Null((await fixture.RequestAsync(subjectId)).Values["total"]);

        var row = (await tenant.HistoryAsync(instanceId))[1];
        Assert.Equal(("sum", 2L, (string?)PackageCallHandler.UnknownOutcome), (row.Step, row.Revision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["operationId"] = callId, ["outcome"] = "unknown" },
            JsonNode.Parse(row.Output!)));
        Assert.Equal(AuditActions.OperationStaged, Assert.Single(await tenant.SystemAuditActionsAsync(instanceId)));

        // The lost attempt gets its row, with the outcome unknown and the fixed text.
        var lost = Assert.Single(await AttemptsAsync(callId));
        Assert.Equal(("attempt", 1, "unknown", (int?)null, (string?)PackageCallHandler.UnknownOutcome), (lost.Kind, lost.Number, lost.Outcome, lost.HttpStatus, lost.Error));

        // The item is gone, so the operation is never run again.
        Assert.Null(await fixture.RunCallAsync(CancellationToken));
    }

    [Fact]
    public async Task A_flaky_call_that_throws_twice_settles_succeeded_after_three_attempts_and_moves_on_to_next()
    {
        var tenant = fixture.Tenant;
        var (subjectId, instanceId) = await fixture.StageAsync("PackageFlaky", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        await RunAndAssertRescheduledAsync(callId, TimeSpan.FromSeconds(10));
        await fixture.MakeDueAsync(callId);
        await RunAndAssertRescheduledAsync(callId, TimeSpan.FromSeconds(20));
        await fixture.MakeDueAsync(callId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync(CancellationToken));

        Assert.Equal((PackageCalls.Succeeded, 3, (string?)null, (string?)null), await CallAsync(callId));
        Assert.Equal(
            [("attempt", 1, "retryable"), ("attempt", 2, "retryable"), ("attempt", 3, "succeeded")],
            (await AttemptsAsync(callId)).Select(row => (row.Kind, row.Number, row.Outcome)));
        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await tenant.InstanceAsync(instanceId));
        Assert.Equal(3L, (await fixture.RequestAsync(subjectId)).Values["total"]!.GetValue<long>());
        Assert.Equal(2, (await tenant.HistoryAsync(instanceId)).Count);
        Assert.Equal(
            new[] { AuditActions.OperationStaged, AuditActions.RecordUpdated, AuditActions.OperationCalled }.Order(StringComparer.Ordinal),
            (await tenant.SystemAuditActionsAsync(instanceId)).Order(StringComparer.Ordinal));
        var called = JsonNode.Parse(await AuditDetailsAsync(instanceId, AuditActions.OperationCalled))!;
        Assert.Equal((3, "succeeded"), (called["attempts"]!.GetValue<int>(), called["outcome"]!.GetValue<string>()));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync(CancellationToken));
    }

    [Fact]
    public async Task A_call_that_throws_a_retryable_error_once_and_then_succeeds_has_two_attempt_rows_with_their_times_and_no_status()
    {
        var (_, instanceId) = await fixture.StageAsync("PackageFlaky", CancellationToken, divisor: 1);
        var callId = await CallIdAsync(instanceId);

        await RunAndAssertRescheduledAsync(callId, TimeSpan.FromSeconds(10));
        await fixture.MakeDueAsync(callId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync(CancellationToken));

        var attempts = await AttemptsAsync(callId);
        Assert.Equal(
            [("attempt", 1, "retryable", (int?)null, (string?)"Axis.Extensions.RetryableException"), ("attempt", 2, "succeeded", (int?)null, (string?)null)],
            attempts.Select(row => (row.Kind, row.Number, row.Outcome, row.HttpStatus, row.Error)));
        Assert.All(attempts, row => Assert.True(row.StartedAt <= row.FinishedAt));

        // A settled package call still clears its input.
        var call = await CallAsync(callId);
        Assert.Equal((PackageCalls.Succeeded, 2), (call.State, call.Attempts));
        Assert.Null(call.Input);
    }

    [Fact]
    public async Task A_retry_sets_the_call_pending_and_writes_no_history_row_and_no_audit_record()
    {
        var tenant = fixture.Tenant;
        var (_, instanceId) = await fixture.StageAsync("PackageFlaky", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        await RunAndAssertRescheduledAsync(callId, TimeSpan.FromSeconds(10));
        var call = await CallAsync(callId);
        Assert.Equal((PackageCalls.Pending, 1, (string?)"Axis.Extensions.RetryableException"), (call.State, call.Attempts, call.LastError));
        Assert.NotNull(call.Input);

        await fixture.MakeDueAsync(callId);
        await RunAndAssertRescheduledAsync(callId, TimeSpan.FromSeconds(20));
        call = await CallAsync(callId);
        Assert.Equal((PackageCalls.Pending, 2), (call.State, call.Attempts));
        Assert.NotNull(call.Input);

        Assert.Equal((ProcessStarts.Waiting, 2L, "flaky", false), await tenant.InstanceAsync(instanceId));
        Assert.Single(await tenant.HistoryAsync(instanceId));
        Assert.Equal(AuditActions.OperationStaged, Assert.Single(await tenant.SystemAuditActionsAsync(instanceId)));
    }

    [Fact]
    public async Task A_flaky_call_that_throws_more_often_than_its_attempts_allow_fails_the_instance()
    {
        var tenant = fixture.Tenant;
        var (_, instanceId) = await fixture.StageAsync("PackageFlakyShort", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallAsync(CancellationToken));
        await fixture.MakeDueAsync(callId);
        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallAsync(CancellationToken));

        Assert.Equal((PackageCalls.Failed, 2, (string?)"Axis.Extensions.RetryableException", (string?)null), await CallAsync(callId));

        // The last attempt is a retryable class, although the call failed: no attempts were left.
        Assert.Equal(
            [("attempt", 1, "retryable"), ("attempt", 2, "retryable")],
            (await AttemptsAsync(callId)).Select(row => (row.Kind, row.Number, row.Outcome)));
        Assert.Equal((ProcessStarts.Failed, 3L, "flaky", true), await tenant.InstanceAsync(instanceId));
        var row = (await tenant.HistoryAsync(instanceId))[1];
        Assert.Equal(("flaky", 2L, "Axis.Extensions.RetryableException"), (row.Step, row.Revision, row.Error));
        Assert.Equal(
            new[] { AuditActions.OperationStaged, AuditActions.OperationCalled, AuditActions.ProcessFailed }.Order(StringComparer.Ordinal),
            (await tenant.SystemAuditActionsAsync(instanceId)).Order(StringComparer.Ordinal));
        var called = JsonNode.Parse(await AuditDetailsAsync(instanceId, AuditActions.OperationCalled))!;
        Assert.Equal((2, "failed"), (called["attempts"]!.GetValue<int>(), called["outcome"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_worker_that_does_not_hold_the_pinned_version_counts_an_attempt_and_reschedules_the_call()
    {
        var tenant = fixture.Tenant;
        var (_, instanceId) = await fixture.StageAsync("PackageSum", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        var before = await fixture.DatabaseNowAsync();
        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallWithoutPackagesAsync(CancellationToken));
        var after = await fixture.DatabaseNowAsync();

        // The step's backoff is the default PT30S.
        var dueAt = await fixture.DueAtAsync(callId);
        Assert.NotNull(dueAt);
        Assert.InRange(dueAt.Value, before + TimeSpan.FromSeconds(30), after + TimeSpan.FromSeconds(30));
        var call = await CallAsync(callId);
        Assert.Equal((PackageCalls.Pending, 1), (call.State, call.Attempts));
        Assert.Equal("The package 'test-package' 1.0.0 is not installed on this worker.", call.LastError);
        Assert.Equal("{\"a\": 2, \"b\": 3}", call.Input);
        var missing = Assert.Single(await AttemptsAsync(callId));
        Assert.Equal(("attempt", 1, "retryable", (int?)null, (string?)call.LastError), (missing.Kind, missing.Number, missing.Outcome, missing.HttpStatus, missing.Error));
        Assert.Equal((ProcessStarts.Waiting, 2L, "sum", false), await tenant.InstanceAsync(instanceId));
        Assert.Single(await tenant.HistoryAsync(instanceId));

        // A worker that holds the version runs the retry.
        await fixture.MakeDueAsync(callId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync(CancellationToken));
        var settled = await CallAsync(callId);
        Assert.Equal((PackageCalls.Succeeded, 2), (settled.State, settled.Attempts));
        Assert.Equal(
            [("attempt", 1, "retryable"), ("attempt", 2, "succeeded")],
            (await AttemptsAsync(callId)).Select(row => (row.Kind, row.Number, row.Outcome)));
    }

    [Fact]
    public async Task A_slow_call_that_is_not_idempotent_times_out_and_is_left_unknown_with_the_instance_waiting()
    {
        var tenant = fixture.Tenant;
        var (_, instanceId) = await fixture.StageAsync("PackageSlow", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync(CancellationToken));

        Assert.Equal((ProcessStarts.Waiting, 2L, "slow", false), await tenant.InstanceAsync(instanceId));
        var call = await CallAsync(callId);
        Assert.Equal((PackageCalls.Unknown, 1, (string?)PackageCallHandler.TimedOut), (call.State, call.Attempts, call.LastError));
        Assert.NotNull(call.Input);
        var timedOut = Assert.Single(await AttemptsAsync(callId));
        Assert.Equal(("attempt", 1, "unknown", (int?)null, (string?)PackageCallHandler.TimedOut), (timedOut.Kind, timedOut.Number, timedOut.Outcome, timedOut.HttpStatus, timedOut.Error));
        var row = (await tenant.HistoryAsync(instanceId))[1];
        Assert.Equal(("slow", 2L, (string?)PackageCallHandler.TimedOut), (row.Step, row.Revision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["operationId"] = callId, ["outcome"] = "unknown" },
            JsonNode.Parse(row.Output!)));
        Assert.Equal(AuditActions.OperationStaged, Assert.Single(await tenant.SystemAuditActionsAsync(instanceId)));
        Assert.Null(await fixture.RunCallAsync(CancellationToken));
    }

    [Fact]
    public async Task An_idempotent_call_found_sending_goes_back_to_pending_and_is_rescheduled()
    {
        var tenant = fixture.Tenant;
        var (_, instanceId) = await fixture.StageAsync("PackageFlaky", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        // A worker marked the call and crashed before it recorded a result.
        await tenant.ScalarAsync("UPDATE axis.package_calls SET state = 'sending', attempts = 1 WHERE id = @id", ("id", callId));

        await RunAndAssertRescheduledAsync(callId, TimeSpan.FromSeconds(10));

        var call = await CallAsync(callId);
        Assert.Equal((PackageCalls.Pending, 1, (string?)PackageCallHandler.UnknownOutcome), (call.State, call.Attempts, call.LastError));
        Assert.NotNull(call.Input);
        Assert.Equal((ProcessStarts.Waiting, 2L, "flaky", false), await tenant.InstanceAsync(instanceId));
        Assert.Single(await tenant.HistoryAsync(instanceId));
    }

    /// <summary>
    /// Runs the call's item and checks it is rescheduled to <paramref name="delay"/> after the
    /// database time of the run, read before and after it.
    /// </summary>
    private async Task RunAndAssertRescheduledAsync(Guid callId, TimeSpan delay)
    {
        var before = await fixture.DatabaseNowAsync();
        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallAsync(CancellationToken));
        var after = await fixture.DatabaseNowAsync();
        var dueAt = await fixture.DueAtAsync(callId);
        Assert.NotNull(dueAt);
        Assert.InRange(dueAt.Value, before + delay, after + delay);
    }

    private async Task<Guid> CallIdAsync(Guid instanceId) =>
        (Guid)(await fixture.Tenant.ScalarAsync("SELECT id FROM axis.package_calls WHERE process_instance_id = @id", ("id", instanceId)))!;

    /// <summary>The call's state, attempts, last error and stored input.</summary>
    private async Task<(string State, int Attempts, string? LastError, string? Input)> CallAsync(Guid callId)
    {
        await using var command = fixture.Tenant.DataSource.CreateCommand(
            "SELECT state, attempts, last_error, input::text FROM axis.package_calls WHERE id = @id");
        command.Parameters.AddWithValue("id", callId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (
            reader.GetString(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    /// <summary>The rows of <c>axis.process_call_attempts</c> for the call: attempts first, then lookups, each in order.</summary>
    private async Task<List<(string Kind, int Number, string Outcome, int? HttpStatus, string? Error, DateTimeOffset StartedAt, DateTimeOffset FinishedAt)>> AttemptsAsync(Guid callId)
    {
        await using var command = fixture.Tenant.DataSource.CreateCommand(
            """
            SELECT kind, number, outcome, http_status, error, started_at, finished_at
            FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package' ORDER BY kind, number
            """);
        command.Parameters.AddWithValue("id", callId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<(string, int, string, int?, string?, DateTimeOffset, DateTimeOffset)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add((
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return rows;
    }

    /// <summary>The details of the instance's system audit record with <paramref name="action"/>.</summary>
    private async Task<string> AuditDetailsAsync(Guid instanceId, string action) =>
        (string)(await fixture.Tenant.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action AND actor = 'system'",
            ("id", instanceId),
            ("action", action)))!;

    /// <summary>The details of every audit record of the instance.</summary>
    private async Task<string> AuditDetailsAsync(Guid instanceId) =>
        (string)(await fixture.Tenant.ScalarAsync(
            "SELECT string_agg(details::text, ' ') FROM axis.audit_records WHERE process_instance_id = @id",
            ("id", instanceId)))!;
}
