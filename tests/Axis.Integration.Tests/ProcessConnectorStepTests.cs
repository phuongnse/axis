using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

public sealed class ProcessConnectorStepTests(ProcessStepFixture fixture) : IClassFixture<ProcessStepFixture>
{
    private static readonly TimeSpan _shortLease = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan _afterShortLease = TimeSpan.FromSeconds(1.5);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Connector_step_stages_a_pending_call_and_its_work_item_and_leaves_the_instance_waiting()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("Order", subjectId, fixture.FirstReleaseId);

        Assert.True(await fixture.RunNextStepAsync(CancellationToken));

        Assert.Equal((ProcessStarts.Waiting, 2L, "createOrder", false), await fixture.InstanceAsync(instanceId));
        var call = Assert.Single(await CallsAsync(instanceId));
        Assert.Equal(
            ("createOrder", fixture.FirstReleaseId, "Purchasing", "createOrder", ConnectorCalls.Pending, 0, (int?)null, (string?)null, false),
            (call.Step, call.ReleaseId, call.Connector, call.Operation, call.State, call.Attempts, call.LastStatus, call.LastError, call.Settled));

        // The input holds every mapped input, with null for the optional note the request leaves empty.
        var input = Assert.IsType<JsonObject>(JsonNode.Parse(call.Input!));
        Assert.Equal(["amount", "note", "reference"], input.Select(entry => entry.Key).Order(StringComparer.Ordinal));
        Assert.Equal($"Request {subjectId:N}", input["reference"]!.GetValue<string>());
        Assert.Equal(500m, input["amount"]!.GetValue<decimal>());
        Assert.Null(input["note"]);

        // The call's item names it and is still unclaimed, because only the step's item ran.
        var item = Assert.Single(await WorkItemsAsync(instanceId));
        Assert.Equal((ProcessStarts.ConnectorCallWorkItemKind, (Guid?)call.Id, false), (item.Kind, item.ConnectorCallId, item.Claimed));
    }

    [Fact]
    public async Task Connector_step_writes_its_history_and_a_staged_call_audit_record_without_input_values()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("Order", subjectId, fixture.FirstReleaseId);

        Assert.True(await fixture.RunNextStepAsync(CancellationToken));

        var callId = Assert.Single(await CallsAsync(instanceId)).Id;
        var row = Assert.Single(await fixture.HistoryAsync(instanceId));
        Assert.Equal(("createOrder", 1L, (string?)null, (string?)null), (row.Step, row.Revision, row.Decision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["state"] = ProcessStarts.Waiting, ["operationId"] = callId },
            JsonNode.Parse(row.Output!)));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["subjectId"] = subjectId, ["subjectVersion"] = 1 },
            JsonNode.Parse(row.Input)));

        Assert.Equal([AuditActions.ConnectorCallStaged], await fixture.SystemAuditActionsAsync(instanceId));
        var details = (string?)await fixture.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action AND actor = 'system'",
            ("id", instanceId),
            ("action", AuditActions.ConnectorCallStaged));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["step"] = "createOrder",
                ["connector"] = "Purchasing",
                ["operation"] = "createOrder",
                ["operationId"] = callId,
            },
            JsonNode.Parse(details!)));
        Assert.DoesNotContain($"Request {subjectId:N}", details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Connector_step_of_a_worker_that_never_commits_stages_no_call_and_the_step_after_the_lease_stages_one()
    {
        var subjectId = await fixture.InsertRequestAsync(500m, 1);
        var instanceId = await fixture.StartAsync("Order", subjectId, fixture.FirstReleaseId);

        // The first worker claims the step's item and crashes before it runs it.
        var crashed = await fixture.ClaimAsync(ProcessStarts.StepWorkItemKind, _shortLease, CancellationToken);
        Assert.Equal(instanceId, crashed?.ProcessInstanceId);
        Assert.Empty(await CallsAsync(instanceId));
        Assert.Equal((ProcessStarts.Running, 1L, "createOrder", false), await fixture.InstanceAsync(instanceId));

        await Task.Delay(_afterShortLease, CancellationToken);
        Assert.True(await fixture.RunNextStepAsync(CancellationToken));

        var call = Assert.Single(await CallsAsync(instanceId));
        Assert.Equal((ProcessStarts.Waiting, 2L, "createOrder", false), await fixture.InstanceAsync(instanceId));

        // The crashed worker's claim is gone, so it commits nothing if it ever runs the item.
        Assert.Equal(WorkItemOutcome.LostClaim, await fixture.Runner.ExecuteAsync(crashed!, CancellationToken));
        Assert.Equal(call.Id, Assert.Single(await CallsAsync(instanceId)).Id);
        Assert.Equal(call.Id, Assert.Single(await WorkItemsAsync(instanceId)).ConnectorCallId);
        Assert.Single(await fixture.HistoryAsync(instanceId));
    }

    [Fact]
    public async Task Operation_identity_is_stable_for_a_call_and_differs_between_instances()
    {
        var firstSubject = await fixture.InsertRequestAsync(500m, 1);
        var secondSubject = await fixture.InsertRequestAsync(500m, 1);
        var first = await fixture.StartAsync("Order", firstSubject, fixture.FirstReleaseId);
        var second = await fixture.StartAsync("Order", secondSubject, fixture.FirstReleaseId);

        Assert.True(await fixture.RunNextStepAsync(CancellationToken));
        Assert.True(await fixture.RunNextStepAsync(CancellationToken));

        var firstId = Assert.Single(await CallsAsync(first)).Id;
        Assert.Equal(firstId, Assert.Single(await CallsAsync(first)).Id);
        Assert.Equal(7, firstId.Version);
        var secondId = Assert.Single(await CallsAsync(second)).Id;
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(
            firstId.ToString("D"),
            JsonNode.Parse(Assert.Single(await fixture.HistoryAsync(first)).Output!)!["operationId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Required_input_that_gives_null_fails_the_instance_and_stages_no_call()
    {
        var subjectId = await fixture.InsertRequestAsync(null, 1);
        var instanceId = await fixture.StartAsync("Order", subjectId, fixture.FirstReleaseId);

        Assert.True(await fixture.RunNextStepAsync(CancellationToken));

        Assert.Equal((ProcessStarts.Failed, 2L, "createOrder", true), await fixture.InstanceAsync(instanceId));
        var row = Assert.Single(await fixture.HistoryAsync(instanceId));
        Assert.Equal(("createOrder", "The required input 'amount' of the step 'createOrder' gave no value.", (string?)null), (row.Step, row.Error, row.Output));
        Assert.Empty(await CallsAsync(instanceId));
        Assert.Empty(await WorkItemsAsync(instanceId));
        Assert.Equal([AuditActions.ProcessFailed], await fixture.SystemAuditActionsAsync(instanceId));
    }

    /// <summary>The instance's connector calls, read from SQL.</summary>
    private async Task<IReadOnlyList<CallRow>> CallsAsync(Guid instanceId)
    {
        await using var command = fixture.DataSource.CreateCommand(
            """
            SELECT id, step, release_id, connector, operation, state, attempts, input::text, last_status, last_error, settled_at IS NOT NULL
            FROM axis.connector_calls WHERE process_instance_id = @id ORDER BY id
            """);
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<CallRow>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new CallRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetGuid(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetBoolean(10)));
        }

        return rows;
    }

    /// <summary>The instance's work items, read from SQL. <see cref="WorkItem.Claimed"/> is whether it has a claim token or a lease.</summary>
    private async Task<IReadOnlyList<WorkItem>> WorkItemsAsync(Guid instanceId)
    {
        await using var command = fixture.DataSource.CreateCommand(
            """
            SELECT kind, connector_call_id, claim_token IS NOT NULL OR lease_expires_at IS NOT NULL
            FROM axis.process_work_items WHERE process_instance_id = @id ORDER BY id
            """);
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<WorkItem>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add(new WorkItem(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetBoolean(2)));
        }

        return rows;
    }

    private sealed record CallRow(
        Guid Id,
        string Step,
        Guid ReleaseId,
        string Connector,
        string Operation,
        string State,
        int Attempts,
        string? Input,
        int? LastStatus,
        string? LastError,
        bool Settled);

    private sealed record WorkItem(string Kind, Guid? ConnectorCallId, bool Claimed);
}
