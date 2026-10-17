using System.Diagnostics;
using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Data.Naming;
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
        var settled = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Succeeded, 1, (int?)200, (string?)null), (settled.State, settled.Attempts, settled.LastStatus, settled.LastError));

        // The call keeps the input it sent after it settles.
        Assert.True(JsonNode.DeepEquals(body, JsonNode.Parse(settled.Input!)));
        var attempt = Assert.Single(await AttemptsAsync(tenant, callId));
        Assert.Equal(("attempt", 1, "succeeded", (int?)200, (string?)null), (attempt.Kind, attempt.Number, attempt.Outcome, attempt.HttpStatus, attempt.Error));
        Assert.True(attempt.StartedAt <= attempt.FinishedAt);

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
            Assert.Equal(0L, await tenant.AdminCountAsync(
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

        // A 400 is not retried: one request, and no work item left to send another.
        Assert.Single(fixture.Stub.RequestsWithKey(callId));
        Assert.Null(await fixture.DueAtAsync("a", callId));
        Assert.Equal((ProcessStarts.Failed, 3L, "createOrder", true), await tenant.InstanceAsync(instanceId));
        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Failed, 1, (int?)400), (call.State, call.Attempts, call.LastStatus));
        Assert.Contains("400", call.LastError, StringComparison.Ordinal);

        // A failed call keeps its input too. Its attempt row holds the status and a fixed error, not the body.
        var stored = Assert.IsType<JsonObject>(JsonNode.Parse(call.Input!));
        Assert.Equal($"Request {subjectId:N}", stored["reference"]!.GetValue<string>());
        var failedAttempt = Assert.Single(await AttemptsAsync(tenant, callId));
        Assert.Equal(("attempt", 1, "failed", (int?)400, call.LastError), (failedAttempt.Kind, failedAttempt.Number, failedAttempt.Outcome, failedAttempt.HttpStatus, failedAttempt.Error));
        Assert.DoesNotContain(marker, await AttemptTextAsync(tenant, callId), StringComparison.Ordinal);

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
        fixture.Stub.Respond(400, """{"error":"refused"}""");
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
    public async Task Worker_outside_Development_sends_nothing_to_an_http_url_and_retries_the_call_naming_the_binding()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken);
        var callId = await CallIdAsync(tenant, instanceId);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOutsideDevelopmentAsync("a", CancellationToken));

        Assert.Empty(fixture.Stub.RequestsWithKey(callId));
        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Pending, 1, (int?)null), (call.State, call.Attempts, call.LastStatus));
        Assert.Equal("Tenant 'a' binding 'PurchasingUrl' is not an https URL. Only Development allows http.", call.LastError);
        Assert.DoesNotContain(fixture.Stub.BaseUrl, call.LastError, StringComparison.Ordinal);
        Assert.Equal((ProcessStarts.Waiting, 2L, "createOrder", false), await tenant.InstanceAsync(instanceId));

        // A worker whose host allows the URL sends the retry.
        await fixture.MakeDueAsync("a", callId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));
        Assert.Single(fixture.Stub.RequestsWithKey(callId));
        call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Succeeded, 2, (int?)200, (string?)null), (call.State, call.Attempts, call.LastStatus, call.LastError));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
    }

    [Fact]
    public async Task Status_503_twice_then_200_succeeds_after_three_attempts_with_one_key_and_a_doubling_backoff()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken);
        var callId = await CallIdAsync(tenant, instanceId);
        fixture.Stub.RespondMany((503, """{"error":"busy"}"""), (503, """{"error":"busy"}"""));

        // The step's backoff is the default PT30S, so attempt n waits 30s·2^(n−1).
        await RunAndAssertRescheduledAsync(callId, TimeSpan.FromSeconds(30));
        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Pending, 1, (int?)503), (call.State, call.Attempts, call.LastStatus));
        Assert.Contains("503", call.LastError, StringComparison.Ordinal);
        Assert.NotNull(call.Input);
        Assert.Equal((ProcessStarts.Waiting, 2L, "createOrder", false), await tenant.InstanceAsync(instanceId));

        await fixture.MakeDueAsync("a", callId);
        await RunAndAssertRescheduledAsync(callId, TimeSpan.FromSeconds(60));
        Assert.Equal((ConnectorCalls.Pending, 2), await StateAsync(tenant, callId));

        await fixture.MakeDueAsync("a", callId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var requests = fixture.Stub.RequestsWithKey(callId);
        Assert.Equal(3, requests.Count);
        Assert.Single(requests.Select(request => request.Body).Distinct());
        var settled = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Succeeded, 3, (int?)200, (string?)null), (settled.State, settled.Attempts, settled.LastStatus, settled.LastError));
        Assert.NotNull(settled.Input);
        Assert.Equal(
            [("attempt", 1, "retryable"), ("attempt", 2, "retryable"), ("attempt", 3, "succeeded")],
            (await AttemptsAsync(tenant, callId)).Select(row => (row.Kind, row.Number, row.Outcome)));
        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await tenant.InstanceAsync(instanceId));

        // Only the settled call writes a history row and a connector.called record.
        Assert.Equal(2, (await tenant.HistoryAsync(instanceId)).Count);
        Assert.Equal(3, JsonNode.Parse(await CalledDetailsAsync(tenant, instanceId))!["attempts"]!.GetValue<int>());
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
    }

    [Fact]
    public async Task Status_503_until_attempts_run_out_fails_the_instance_with_the_last_error_and_counts_the_attempts()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken);
        var callId = await CallIdAsync(tenant, instanceId);
        fixture.Stub.RespondMany((503, "{}"), (503, "{}"), (503, "{}"));

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallAsync("a", CancellationToken));
        await fixture.MakeDueAsync("a", callId);
        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallAsync("a", CancellationToken));
        await fixture.MakeDueAsync("a", callId);
        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallAsync("a", CancellationToken));

        Assert.Equal(3, fixture.Stub.RequestsWithKey(callId).Count);
        Assert.Null(await fixture.DueAtAsync("a", callId));
        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Failed, 3, (int?)503), (call.State, call.Attempts, call.LastStatus));
        Assert.NotNull(call.Input);

        // Each attempt has its row. The last is a retryable class, although the call failed: no attempts were left.
        Assert.Equal(
            [("attempt", 1, "retryable", (int?)503), ("attempt", 2, "retryable", (int?)503), ("attempt", 3, "retryable", (int?)503)],
            (await AttemptsAsync(tenant, callId)).Select(row => (row.Kind, row.Number, row.Outcome, row.HttpStatus)));
        Assert.Equal("The operation 'createOrder' of the connector 'Purchasing' answered with the status 503.", call.LastError);
        Assert.Equal((ProcessStarts.Failed, 3L, "createOrder", true), await tenant.InstanceAsync(instanceId));

        var row = (await tenant.HistoryAsync(instanceId))[1];
        Assert.Equal(("createOrder", 2L, call.LastError), (row.Step, row.Revision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["connector"] = "Purchasing",
                ["operation"] = "createOrder",
                ["operationId"] = callId,
                ["attempts"] = 3,
                ["outcome"] = "failed",
                ["lastStatus"] = 503,
            },
            JsonNode.Parse(await CalledDetailsAsync(tenant, instanceId))));
    }

    [Fact]
    public async Task Timeout_on_a_connector_with_idempotency_is_retried_with_the_same_key()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken, "QuickOrder");
        var callId = await CallIdAsync(tenant, instanceId);

        var held = fixture.Stub.Hold();
        try
        {
            // QuickPurchasing times out after PT1S, while the stub holds its answer.
            Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallAsync("a", CancellationToken));
        }
        finally
        {
            held.Release();
        }

        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Pending, 1, (int?)null), (call.State, call.Attempts, call.LastStatus));
        Assert.Equal("The operation 'createOrder' of the connector 'QuickPurchasing' timed out after 1 seconds.", call.LastError);
        Assert.Equal((ProcessStarts.Waiting, 2L, "createOrder", false), await tenant.InstanceAsync(instanceId));

        await fixture.MakeDueAsync("a", callId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var requests = fixture.Stub.RequestsWithKey(callId);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, request => Assert.Equal("/tenant-a/quick-orders", request.Path));
        Assert.Equal((ConnectorCalls.Succeeded, 2), await StateAsync(tenant, callId));
        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await tenant.InstanceAsync(instanceId));

        // The timeout is the first attempt's row, as unknown, and the retry is the second.
        var attempts = await AttemptsAsync(tenant, callId);
        Assert.Equal(
            [("attempt", 1, "unknown", (int?)null, call.LastError), ("attempt", 2, "succeeded", (int?)200, (string?)null)],
            attempts.Select(row => (row.Kind, row.Number, row.Outcome, row.HttpStatus, row.Error)));
        Assert.All(attempts, row => Assert.True(row.StartedAt <= row.FinishedAt));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
    }

    [Fact]
    public async Task Timeout_on_a_connector_without_idempotency_sends_nothing_again_and_leaves_the_call_unknown()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await fixture.StageAsync("a", CancellationToken, "LedgerOrder");
        var callId = await CallIdAsync(tenant, instanceId);

        var held = fixture.Stub.Hold();
        try
        {
            // Ledger times out after PT1S and declares no idempotency.
            Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));
        }
        finally
        {
            held.Release();
        }

        // Ledger sends no key, so its request is found by its path and its reference.
        var reference = $"Request {subjectId:N}";
        var request = Assert.Single(fixture.Stub.Requests, candidate =>
            candidate.Path == "/tenant-a/ledger" && candidate.Body.Contains(reference, StringComparison.Ordinal));
        Assert.Empty(request.IdempotencyKey);

        // The call keeps its input for reconciliation, and has no work item left to send it again.
        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Unknown, 1, (int?)null), (call.State, call.Attempts, call.LastStatus));
        Assert.Equal("The operation 'createOrder' of the connector 'Ledger' timed out after 1 seconds.", call.LastError);
        Assert.NotNull(call.Input);
        Assert.Null(await fixture.DueAtAsync("a", callId));
        Assert.Equal((ProcessStarts.Waiting, 2L, "createOrder", false), await tenant.InstanceAsync(instanceId));
        var unknownAttempt = Assert.Single(await AttemptsAsync(tenant, callId));
        Assert.Equal(("attempt", 1, "unknown", call.LastError), (unknownAttempt.Kind, unknownAttempt.Number, unknownAttempt.Outcome, unknownAttempt.Error));

        var row = (await tenant.HistoryAsync(instanceId))[1];
        Assert.Equal(("createOrder", 2L, call.LastError), (row.Step, row.Revision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["operationId"] = callId, ["outcome"] = "unknown" },
            JsonNode.Parse(row.Output!)));
        Assert.DoesNotContain(reference, row.Input, StringComparison.Ordinal);
        Assert.Equal(AuditActions.ConnectorCallStaged, Assert.Single(await tenant.SystemAuditActionsAsync(instanceId)));
    }

    [Fact]
    public async Task Status_200_whose_body_does_not_fit_leaves_the_call_unknown_even_with_idempotency()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken);
        var callId = await CallIdAsync(tenant, instanceId);
        fixture.Stub.Respond(200, "[]");

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        Assert.Single(fixture.Stub.RequestsWithKey(callId));
        Assert.Null(await fixture.DueAtAsync("a", callId));
        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Unknown, 1, (int?)200), (call.State, call.Attempts, call.LastStatus));
        Assert.Equal("The response did not fit the output of the operation 'createOrder' of the connector 'Purchasing'.", call.LastError);
        Assert.Equal((ProcessStarts.Waiting, 2L, "createOrder", false), await tenant.InstanceAsync(instanceId));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["operationId"] = callId, ["outcome"] = "unknown" },
            JsonNode.Parse((await tenant.HistoryAsync(instanceId))[1].Output!)));
        Assert.Equal(AuditActions.ConnectorCallStaged, Assert.Single(await tenant.SystemAuditActionsAsync(instanceId)));
    }

    [Fact]
    public async Task Worker_restart_between_attempts_keeps_the_attempt_count_and_the_due_time()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken);
        var callId = await CallIdAsync(tenant, instanceId);
        fixture.Stub.Respond(503, "{}");
        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallAsync("a", CancellationToken));
        var dueAt = await fixture.DueAtAsync("a", callId);
        Assert.NotNull(dueAt);

        await fixture.RestartWorkerAsync();

        // The new worker sees what the old one stored, and claims nothing before the due time.
        Assert.Equal((ConnectorCalls.Pending, 1), await StateAsync(tenant, callId));
        Assert.Equal(dueAt, await fixture.DueAtAsync("a", callId));
        Assert.True(dueAt > await fixture.DatabaseNowAsync("a"));
        Assert.Null(await fixture.RunCallAsync("a", CancellationToken));
        Assert.Single(fixture.Stub.RequestsWithKey(callId));

        await fixture.MakeDueAsync("a", callId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));
        Assert.Equal(2, fixture.Stub.RequestsWithKey(callId).Count);
        Assert.Equal((ConnectorCalls.Succeeded, 2), await StateAsync(tenant, callId));
        Assert.Equal(2, JsonNode.Parse(await CalledDetailsAsync(tenant, instanceId))!["attempts"]!.GetValue<int>());
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
    }

    [Fact]
    public async Task Call_found_still_sending_on_a_connector_with_idempotency_waits_out_the_backoff_and_is_sent_again_with_the_same_key()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken);
        var callId = await CallIdAsync(tenant, instanceId);

        // A worker marked the call and crashed before it recorded an outcome.
        await tenant.ScalarAsync("UPDATE axis.connector_calls SET state = 'sending', attempts = 1 WHERE id = @id", ("id", callId));

        // The attempt already counted has an unknown outcome, so nothing is sent at once.
        await RunAndAssertRescheduledAsync(callId, TimeSpan.FromSeconds(30));
        Assert.Empty(fixture.Stub.RequestsWithKey(callId));
        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Pending, 1, (int?)null), (call.State, call.Attempts, call.LastStatus));
        Assert.Equal(ConnectorCallHandler.UnknownOutcome, call.LastError);
        Assert.Equal((ProcessStarts.Waiting, 2L, "createOrder", false), await tenant.InstanceAsync(instanceId));

        // The lost attempt gets its row, with the outcome unknown and the fixed text.
        var lost = Assert.Single(await AttemptsAsync(tenant, callId));
        Assert.Equal(("attempt", 1, "unknown", (int?)null, ConnectorCallHandler.UnknownOutcome), (lost.Kind, lost.Number, lost.Outcome, lost.HttpStatus, lost.Error));

        await fixture.MakeDueAsync("a", callId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));
        Assert.Single(fixture.Stub.RequestsWithKey(callId));
        Assert.Equal((ConnectorCalls.Succeeded, 2), await StateAsync(tenant, callId));
        Assert.Equal(
            [("attempt", 1, "unknown"), ("attempt", 2, "succeeded")],
            (await AttemptsAsync(tenant, callId)).Select(row => (row.Kind, row.Number, row.Outcome)));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
    }

    [Fact]
    public async Task Dropped_connection_is_reconciled_through_the_lookup_which_settles_the_call_with_the_stored_output_and_sends_nothing_again()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await fixture.StageAsync("a", CancellationToken, "TrackedOrder");
        var callId = await CallIdAsync(tenant, instanceId);
        fixture.Stub.DropNext();

        // TrackedPurchasing declares no idempotency, so the unknown outcome goes to its lookup at once.
        await RunAndAssertRescheduledAsync(callId, TimeSpan.Zero);
        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Unknown, 1, (int?)null), (call.State, call.Attempts, call.LastStatus));
        Assert.NotNull(call.LastError);
        Assert.NotNull(call.Input);
        Assert.Equal((ProcessStarts.Waiting, 2L, "createOrder", false), await tenant.InstanceAsync(instanceId));
        Assert.Single(await tenant.HistoryAsync(instanceId));

        // The stub stored the create request before it dropped the connection, so the lookup finds it.
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var create = Assert.Single(fixture.Stub.CreateRequestsFor(callId));
        Assert.Equal(("POST", $"/tenant-a/tracked-orders/{callId:D}"), (create.Method, create.Path));
        var lookup = Assert.Single(fixture.Stub.RequestsFor(callId), request => request.Method == "GET");
        Assert.Equal(
            ($"/tenant-a/tracked-orders/by-key/{callId:D}", $"Bearer {fixture.Token("a")}", ""),
            (lookup.Path, lookup.Authorization, lookup.Body));
        var reconciled = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Succeeded, 1, (int?)200, (string?)null), (reconciled.State, reconciled.Attempts, reconciled.LastStatus, reconciled.LastError));
        Assert.NotNull(reconciled.Input);

        // The dropped attempt is unknown, and the lookup that found the call is numbered apart.
        Assert.Equal(
            [("attempt", 1, "unknown", (int?)null), ("lookup", 1, "found", (int?)200)],
            (await AttemptsAsync(tenant, callId)).Select(row => (row.Kind, row.Number, row.Outcome, row.HttpStatus)));
        Assert.Equal("PO-1", (await tenant.RequestAsync(subjectId)).Values["comment"]!.GetValue<string>());
        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await tenant.InstanceAsync(instanceId));

        var row = (await tenant.HistoryAsync(instanceId))[1];
        Assert.Equal(("createOrder", 2L, (string?)null), (row.Step, row.Revision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["operationId"] = callId, ["outcome"] = "succeeded", ["next"] = "done", ["subjectVersion"] = 2 },
            JsonNode.Parse(row.Output!)));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject
            {
                ["connector"] = "TrackedPurchasing",
                ["operation"] = "createOrder",
                ["operationId"] = callId,
                ["attempts"] = 1,
                ["outcome"] = "succeeded",
                ["lastStatus"] = 200,
            },
            JsonNode.Parse(await CalledDetailsAsync(tenant, instanceId))));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
    }

    [Fact]
    public async Task Lookup_that_finds_no_call_sends_the_call_once_more_with_the_same_operation_identity()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken, "TrackedOrder");
        var callId = await CallIdAsync(tenant, instanceId);

        // A 500 leaves the outcome unknown, and the stub stores no order for it.
        fixture.Stub.Respond(500, "{}");
        await RunAndAssertRescheduledAsync(callId, TimeSpan.Zero);
        Assert.Equal((ConnectorCalls.Unknown, 1), await StateAsync(tenant, callId));

        // The lookup answers 404, so the call waits out the step's backoff for its next attempt.
        await RunAndAssertRescheduledAsync(callId, TimeSpan.FromSeconds(30));
        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Pending, 1, (int?)404), (call.State, call.Attempts, call.LastStatus));
        Assert.Equal("The lookup 'findOrder' of the connector 'TrackedPurchasing' found no call.", call.LastError);
        Assert.Single(fixture.Stub.CreateRequestsFor(callId));
        Assert.Equal(
            [("attempt", 1, "unknown", (int?)500), ("lookup", 1, "notFound", (int?)404)],
            (await AttemptsAsync(tenant, callId)).Select(row => (row.Kind, row.Number, row.Outcome, row.HttpStatus)));

        await fixture.MakeDueAsync("a", callId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var creates = fixture.Stub.CreateRequestsFor(callId);
        Assert.Equal(2, creates.Count);
        Assert.All(creates, request => Assert.Equal($"/tenant-a/tracked-orders/{callId:D}", request.Path));
        Assert.Single(creates.Select(request => request.Body).Distinct());
        Assert.Equal((ConnectorCalls.Succeeded, 2), await StateAsync(tenant, callId));

        // The resend is the next attempt. The lookup keeps its own numbering.
        Assert.Equal(
            [("attempt", 1, "unknown"), ("attempt", 2, "succeeded"), ("lookup", 1, "notFound")],
            (await AttemptsAsync(tenant, callId)).Select(row => (row.Kind, row.Number, row.Outcome)));
        Assert.Equal(2, JsonNode.Parse(await CalledDetailsAsync(tenant, instanceId))!["attempts"]!.GetValue<int>());
        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await tenant.InstanceAsync(instanceId));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
    }

    [Fact]
    public async Task Failed_lookup_keeps_the_call_unknown_and_is_tried_again_after_a_doubling_backoff_across_a_worker_restart()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken, "TrackedOrder");
        var callId = await CallIdAsync(tenant, instanceId);
        fixture.Stub.DropNext();
        await RunAndAssertRescheduledAsync(callId, TimeSpan.Zero);

        fixture.Stub.Respond(500, "{}");
        await RunAndAssertRescheduledAsync(callId, TimeSpan.FromSeconds(30));
        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Unknown, 1, (int?)500), (call.State, call.Attempts, call.LastStatus));
        Assert.Equal("The operation 'findOrder' of the connector 'TrackedPurchasing' answered with the status 500.", call.LastError);
        Assert.Equal(1, await LookupsAsync(tenant, callId));
        Assert.Single(await tenant.HistoryAsync(instanceId));
        var dueAt = await fixture.DueAtAsync("a", callId);

        await fixture.RestartWorkerAsync();

        // The new worker claims nothing before the due time, and the count of failed lookups survives.
        Assert.Equal(dueAt, await fixture.DueAtAsync("a", callId));
        Assert.True(dueAt > await fixture.DatabaseNowAsync("a"));
        Assert.Null(await fixture.RunCallAsync("a", CancellationToken));

        await fixture.MakeDueAsync("a", callId);
        fixture.Stub.Respond(500, "{}");
        await RunAndAssertRescheduledAsync(callId, TimeSpan.FromSeconds(60));
        Assert.Equal(2, await LookupsAsync(tenant, callId));
        Assert.Equal((ConnectorCalls.Unknown, 1), await StateAsync(tenant, callId));

        await fixture.MakeDueAsync("a", callId);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));
        Assert.Single(fixture.Stub.CreateRequestsFor(callId));
        Assert.Equal(3, fixture.Stub.RequestsFor(callId).Count(request => request.Method == "GET"));
        Assert.Equal((ConnectorCalls.Succeeded, 1), await StateAsync(tenant, callId));

        // Every lookup has its row: two failed ones, then the one that found the call.
        Assert.Equal(
            [("attempt", 1, "unknown", (int?)null), ("lookup", 1, "failed", (int?)500), ("lookup", 2, "failed", (int?)500), ("lookup", 3, "found", (int?)200)],
            (await AttemptsAsync(tenant, callId)).Select(row => (row.Kind, row.Number, row.Outcome, row.HttpStatus)));
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
    }

    [Fact]
    public async Task Database_error_while_recording_a_success_stores_the_exception_type_and_no_record_value()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await fixture.StageAsync("a", CancellationToken);
        var callId = await CallIdAsync(tenant, instanceId);
        Assert.True(tenant.Model.TryGetEntity("Request", out var request));
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(request.Id));

        // The error fires only for this test's record and carries its amount in the message.
        await tenant.ScalarAsync(
            $$"""
            CREATE FUNCTION axis.test_success_write_leak() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN
                IF OLD.{{EntityNaming.IdColumn}} = '{{subjectId:D}}'::uuid THEN
                    RAISE EXCEPTION 'amount %', OLD.{{EntityNaming.Column("amount")}};
                END IF;
                RETURN NEW;
            END
            $body$;
            CREATE TRIGGER test_success_write_leak BEFORE UPDATE ON {{table}}
                FOR EACH ROW EXECUTE FUNCTION axis.test_success_write_leak();
            """);
        try
        {
            Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallAsync("a", CancellationToken));
        }
        finally
        {
            await tenant.ScalarAsync(
                $"""
                DROP TRIGGER test_success_write_leak ON {table};
                DROP FUNCTION axis.test_success_write_leak();
                """);
        }

        const string recordFailure = "The outcome of the call could not be recorded: Npgsql.PostgresException.";
        var call = await CallAsync(tenant, callId);
        Assert.Equal(recordFailure, call.LastError);
        Assert.Equal(recordFailure, (await tenant.HistoryAsync(instanceId))[1].Error);
        Assert.Equal(recordFailure, Assert.Single(await AttemptsAsync(tenant, callId)).Error);
        Assert.DoesNotContain("500", call.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dropped_connection_on_a_connector_without_lookup_leaves_the_call_unknown_and_never_sends_it_again()
    {
        var tenant = fixture.Tenant("a");
        var (subjectId, instanceId) = await fixture.StageAsync("a", CancellationToken, "LedgerOrder");
        var callId = await CallIdAsync(tenant, instanceId);
        fixture.Stub.DropNext();

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var call = await CallAsync(tenant, callId);
        Assert.Equal((ConnectorCalls.Unknown, 1, (int?)null), (call.State, call.Attempts, call.LastStatus));
        const string sendFailure = "The operation 'createOrder' of the connector 'Ledger' could not be called: System.Net.Http.HttpRequestException.";
        Assert.Equal(sendFailure, call.LastError);
        Assert.Equal(sendFailure, (await AttemptsAsync(tenant, callId)).Single(attempt => attempt.Outcome == "unknown").Error);
        Assert.Null(await fixture.DueAtAsync("a", callId));
        Assert.Equal((ProcessStarts.Waiting, 2L, "createOrder", false), await tenant.InstanceAsync(instanceId));
        var row = (await tenant.HistoryAsync(instanceId))[1];
        Assert.Equal(("createOrder", 2L, call.LastError), (row.Step, row.Revision, row.Error));
        Assert.True(JsonNode.DeepEquals(
            new JsonObject { ["operationId"] = callId, ["outcome"] = "unknown" },
            JsonNode.Parse(row.Output!)));

        // Neither this worker nor a restarted one finds anything to send.
        Assert.Null(await fixture.RunCallAsync("a", CancellationToken));
        await fixture.RestartWorkerAsync();
        Assert.Null(await fixture.RunCallAsync("a", CancellationToken));

        // Ledger sends no key, so its request is found by its path and its reference.
        var reference = $"Request {subjectId:N}";
        Assert.Single(fixture.Stub.Requests, candidate =>
            candidate.Path == "/tenant-a/ledger" && candidate.Body.Contains(reference, StringComparison.Ordinal));
        Assert.Equal((ConnectorCalls.Unknown, 1), await StateAsync(tenant, callId));
    }

    /// <summary>
    /// Runs the call's item and checks it is rescheduled to <paramref name="delay"/> after the
    /// database time of the run, read before and after it.
    /// </summary>
    private async Task RunAndAssertRescheduledAsync(Guid callId, TimeSpan delay)
    {
        var before = await fixture.DatabaseNowAsync("a");
        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallAsync("a", CancellationToken));
        var after = await fixture.DatabaseNowAsync("a");
        var dueAt = await fixture.DueAtAsync("a", callId);
        Assert.NotNull(dueAt);
        Assert.InRange(dueAt.Value, before + delay, after + delay);
    }

    [Fact]
    public async Task Attempt_has_an_http_client_span_under_it_that_names_the_path_template_and_no_address_and_the_call_carries_the_trace()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId) = await fixture.StageAsync("a", CancellationToken, "TrackedOrder");
        var callId = await CallIdAsync(tenant, instanceId);
        using var collector = new SpanCollector();

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        // The stub got the trace context of the HTTP span, so its trace id names the attempt's trace.
        var request = Assert.Single(fixture.Stub.CreateRequestsFor(callId));
        var traceId = request.Traceparent.Split('-')[1];
        var spans = collector.Spans.Where(span => span.TraceId.ToHexString() == traceId).ToList();
        var attempt = Assert.Single(spans, span => span.OperationName == "axis.connector.attempt");
        Assert.Equal("succeeded", attempt.GetTagItem("axis.outcome"));
        var http = Assert.Single(spans, span => span.Kind == ActivityKind.Client && span.GetTagItem("http.request.method") is not null && span.ParentSpanId == attempt.SpanId);
        Assert.Equal("/tracked-orders/{idempotencyKey}", http.GetTagItem("url.full"));
        Assert.All(["server.address", "server.port", "url.query"], key => Assert.Null(http.GetTagItem(key)));
        Assert.DoesNotContain(http.TagObjects, tag => tag.Value?.ToString()?.Contains(fixture.Stub.BaseUrl, StringComparison.Ordinal) == true);
        Assert.Equal(http.SpanId.ToHexString(), request.Traceparent.Split('-')[2]);

        var row = Assert.Single(await AttemptsAsync(tenant, callId));
        Assert.Equal("attempt", row.Kind);
        Assert.Equal(
            traceId,
            await tenant.ScalarAsync("SELECT trace_id FROM axis.process_call_attempts WHERE call_id = @id", ("id", callId)));

        // The next step's item was queued. Running it leaves nothing due for the next test.
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunStepAsync("a", CancellationToken));
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

    /// <summary>The rows of <c>axis.process_call_attempts</c> for the call: attempts first, then lookups, each in order.</summary>
    private static async Task<List<(string Kind, int Number, string Outcome, int? HttpStatus, string? Error, DateTimeOffset StartedAt, DateTimeOffset FinishedAt)>> AttemptsAsync(
        ProcessStepFixture tenant,
        Guid callId)
    {
        await using var command = tenant.DataSource.CreateCommand(
            """
            SELECT kind, number, outcome, http_status, error, started_at, finished_at
            FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'connector' ORDER BY kind, number
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

    /// <summary>All text in the call's attempt rows, to check that no body is in it.</summary>
    private static async Task<string> AttemptTextAsync(ProcessStepFixture tenant, Guid callId) =>
        (string)(await tenant.ScalarAsync(
            "SELECT string_agg(row_to_json(a)::text, ' ') FROM axis.process_call_attempts a WHERE call_id = @id",
            ("id", callId)))!;

    /// <summary>The failed lookups of the call since it became <c>unknown</c>.</summary>
    private static async Task<int> LookupsAsync(ProcessStepFixture tenant, Guid callId) =>
        (int)(await tenant.ScalarAsync("SELECT lookups FROM axis.connector_calls WHERE id = @id", ("id", callId)))!;

    /// <summary>The call's state and attempts.</summary>
    private static async Task<(string State, int Attempts)> StateAsync(ProcessStepFixture tenant, Guid callId)
    {
        var call = await CallAsync(tenant, callId);
        return (call.State, call.Attempts);
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
