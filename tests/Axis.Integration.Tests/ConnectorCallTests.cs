using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

public sealed class ConnectorCallTests(ConnectorCallFixture fixture) : IClassFixture<ConnectorCallFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Call_sends_one_request_with_the_operation_identity_and_the_bearer_token_and_resumes_the_instance_with_the_output_written()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await fixture.StageAsync("a", CancellationToken);
        var callId = await CallIdAsync(tenant, instanceId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var request = Assert.Single(fixture.Stub.RequestsWithKey(callId));
        Assert.Equal(("POST", "/tenant-a/orders", $"Bearer {fixture.Token("a")}"), (request.Method, request.Path, request.Authorization));
        var body = Assert.IsType<JsonObject>(JsonNode.Parse(request.Body));
        Assert.Equal($"Request {subjectId:N}", body["reference"]!.GetValue<string>());

        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await tenant.InstanceAsync(instanceId));
        var record = await tenant.RequestAsync(subjectId);
        Assert.Equal("PO-1", record.Values["comment"]!.GetValue<string>());
        Assert.Equal(2L, record.Version);
        Assert.Equal(
            (ConnectorCalls.Succeeded, 1, (int?)200, (string?)null, (string?)null),
            await CallAsync(tenant, callId));

        var row = (await tenant.HistoryAsync(instanceId))[1];
        Assert.Equal(("createOrder", 2L, (string?)null), (row.Step, row.Revision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["operationId"] = callId, ["outcome"] = "succeeded", ["next"] = "done", ["subjectVersion"] = 2 },
            JsonNode.Parse(row.Output!)));
        // The records of one transaction share its time, so their order is not checked.
        Assert.Equal(
            new[] { AuditActions.ConnectorCallStaged, AuditActions.RecordUpdated, AuditActions.ConnectorCalled }.Order(StringComparer.Ordinal),
            (await tenant.SystemAuditActionsAsync(instanceId)).Order(StringComparer.Ordinal));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["connector"] = "Purchasing",
                ["operation"] = "createOrder",
                ["operationId"] = callId,
                ["attempts"] = 1,
                ["outcome"] = "succeeded",
                ["lastStatus"] = 200,
            },
            JsonNode.Parse(await CalledDetailsAsync(tenant, instanceId))));

        // The next step's item was queued, and it completes the instance.
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
        Assert.Equal((ProcessStarts.Completed, 4L, "done", true), await tenant.InstanceAsync(instanceId));
    }

    [Fact]
    public async Task No_database_transaction_is_open_while_the_request_is_in_flight()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken);
        var held = fixture.Stub.Hold();

        var run = fixture.RunCallAsync("a", CancellationToken);
        try
        {
            await held.Arrived.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken);
            Assert.Equal(0L, await tenant.CountAsync(
                """
                SELECT count(*) FROM pg_stat_activity
                WHERE datname = current_database() AND pid <> pg_backend_pid()
                  AND backend_type = 'client backend' AND xact_start IS NOT NULL
                """));

            // The call is already marked, so the attempt counts even though no answer arrived yet.
            Assert.Equal(ConnectorCalls.Sending, (await CallAsync(tenant, await CallIdAsync(tenant, instanceId))).State);
        }
        finally
        {
            held.Release();
        }

        Assert.Equal(WorkItemOutcome.Completed, await run);
        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await tenant.InstanceAsync(instanceId));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
    }

    [Fact]
    public async Task Status_400_fails_the_call_and_the_instance_with_the_status_and_no_body_in_history_call_or_audit()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await fixture.StageAsync("a", CancellationToken);
        var callId = await CallIdAsync(tenant, instanceId);
        var marker = $"marker-{Guid.NewGuid():N}";
        fixture.Stub.Respond(400, $$"""{"error":"{{marker}}"}""");

        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallAsync("a", CancellationToken));

        Assert.Single(fixture.Stub.RequestsWithKey(callId));
        Assert.Equal((ProcessStarts.Failed, 3L, "createOrder", true), await tenant.InstanceAsync(instanceId));
        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Failed, 1, (int?)400, (string?)null), (call.State, call.Attempts, call.LastStatus, call.Input));
        Assert.Contains("400", call.LastError, StringComparison.Ordinal);

        var row = (await tenant.HistoryAsync(instanceId))[1];
        Assert.Equal(("createOrder", 2L, call.LastError), (row.Step, row.Revision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["operationId"] = callId, ["outcome"] = "failed" },
            JsonNode.Parse(row.Output!)));
        Assert.Equal(
            new[] { AuditActions.ConnectorCallStaged, AuditActions.ConnectorCalled, AuditActions.ProcessFailed }.Order(StringComparer.Ordinal),
            (await tenant.SystemAuditActionsAsync(instanceId)).Order(StringComparer.Ordinal));
        var details = await CalledDetailsAsync(tenant, instanceId);
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["connector"] = "Purchasing",
                ["operation"] = "createOrder",
                ["operationId"] = callId,
                ["attempts"] = 1,
                ["outcome"] = "failed",
                ["lastStatus"] = 400,
            },
            JsonNode.Parse(details)));

        // Neither the response body nor the request body is kept anywhere.
        var reference = $"Request {subjectId:N}";
        foreach (var text in new[] { row.Input, row.Output!, row.Error!, call.LastError!, details, await AuditDetailsAsync(tenant, instanceId) })
        {
            Assert.DoesNotContain(marker, text, StringComparison.Ordinal);
            Assert.DoesNotContain(reference, text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains(marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_audit_record_history_row_call_row_or_log_entry_holds_the_secret_token()
    {
        var tenant = fixture.Tenant("a");
        var (_, succeeded) = await fixture.StageAsync("a", CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
        var (_, failed) = await fixture.StageAsync("a", CancellationToken);
        fixture.Stub.Respond(500, """{"error":"down"}""");
        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallAsync("a", CancellationToken));

        // Both requests carried the token, so it was in use.
        var token = fixture.Token("a");
        Assert.Equal($"Bearer {token}", Assert.Single(fixture.Stub.RequestsWithKey(await CallIdAsync(tenant, succeeded))).Authorization);
        Assert.Equal($"Bearer {token}", Assert.Single(fixture.Stub.RequestsWithKey(await CallIdAsync(tenant, failed))).Authorization);

        var stored = await tenant.ScalarAsync(
            """
            SELECT concat_ws(' ',
                (SELECT string_agg(details::text, ' ') FROM axis.audit_records),
                (SELECT string_agg(concat_ws(' ', input::text, output::text, error), ' ') FROM axis.process_step_history),
                (SELECT string_agg(concat_ws(' ', input::text, last_error), ' ') FROM axis.connector_calls))
            """);
        var text = Assert.IsType<string>(stored);
        Assert.Contains("connector.called", await AuditActionsTextAsync(tenant), StringComparison.Ordinal);
        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        Assert.NotEmpty(fixture.Logs.Entries);
        Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Each_tenant_call_uses_its_own_url_and_token()
    {
        var (_, instanceA) = await fixture.StageAsync("a", CancellationToken);
        var (_, instanceB) = await fixture.StageAsync("b", CancellationToken);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("b", CancellationToken));

        var requestA = Assert.Single(fixture.Stub.RequestsWithKey(await CallIdAsync(fixture.Tenant("a"), instanceA)));
        var requestB = Assert.Single(fixture.Stub.RequestsWithKey(await CallIdAsync(fixture.Tenant("b"), instanceB)));
        Assert.Equal(("/tenant-a/orders", $"Bearer {fixture.Token("a")}"), (requestA.Path, requestA.Authorization));
        Assert.Equal(("/tenant-b/orders", $"Bearer {fixture.Token("b")}"), (requestB.Path, requestB.Authorization));
        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await fixture.Tenant("a").InstanceAsync(instanceA));
        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await fixture.Tenant("b").InstanceAsync(instanceB));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("b", CancellationToken));
    }

    [Fact]
    public async Task Worker_outside_Development_sends_nothing_to_an_http_url_and_fails_the_call_naming_the_binding()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken);
        var callId = await CallIdAsync(tenant, instanceId);

        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallOutsideDevelopmentAsync("a", CancellationToken));

        Assert.Empty(fixture.Stub.RequestsWithKey(callId));
        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Failed, 1, (int?)null), (call.State, call.Attempts, call.LastStatus));
        Assert.Equal((ProcessStarts.Failed, 3L, "createOrder", true), await tenant.InstanceAsync(instanceId));
        var error = (await tenant.HistoryAsync(instanceId))[1].Error;
        Assert.Equal("Tenant 'a' binding 'PurchasingUrl' is not an https URL. Only Development allows http.", error);
        Assert.Equal(error, call.LastError);
        Assert.DoesNotContain(fixture.Stub.BaseUrl, error, StringComparison.Ordinal);
    }

    private static async Task<Guid> CallIdAsync(ProcessStepFixture tenant, Guid instanceId) =>
        (Guid)(await tenant.ScalarAsync("SELECT id FROM axis.connector_calls WHERE process_instance_id = @id", ("id", instanceId)))!;

    /// <summary>The call's state, attempts, last status, last error and stored input.</summary>
    private static async Task<(string State, int Attempts, int? LastStatus, string? LastError, string? Input)> CallAsync(ProcessStepFixture tenant, Guid callId)
    {
        await using var command = tenant.DataSource.CreateCommand(
            "SELECT state, attempts, last_status, last_error, input::text FROM axis.connector_calls WHERE id = @id");
        command.Parameters.AddWithValue("id", callId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (
            reader.GetString(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private static async Task<string> CalledDetailsAsync(ProcessStepFixture tenant, Guid instanceId) =>
        (string)(await tenant.ScalarAsync(
            "SELECT details::text FROM axis.audit_records WHERE process_instance_id = @id AND action = @action AND actor = 'system'",
            ("id", instanceId),
            ("action", AuditActions.ConnectorCalled)))!;

    private static async Task<string> AuditDetailsAsync(ProcessStepFixture tenant, Guid instanceId) =>
        (string)(await tenant.ScalarAsync(
            "SELECT string_agg(details::text, ' ') FROM axis.audit_records WHERE process_instance_id = @id",
            ("id", instanceId)))!;

    private static async Task<string> AuditActionsTextAsync(ProcessStepFixture tenant) =>
        (string)(await tenant.ScalarAsync("SELECT string_agg(action, ' ') FROM axis.audit_records"))!;
}
