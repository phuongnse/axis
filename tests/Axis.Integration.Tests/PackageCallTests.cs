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

        // The item is gone, so the operation is never run again.
        Assert.Null(await fixture.RunCallAsync(CancellationToken));
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
