using System.Text.Json.Nodes;
using Axis.Configuration.Diagnostics;
using Axis.Data.Audit;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

public sealed class PackageCapabilityTests(PackageCapabilityFixture fixture) : IClassFixture<PackageCapabilityFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_call_reaches_the_declared_url_with_the_declared_secret()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId, callId) = await fixture.StageAsync("a", "PackageProbeDeclared", CancellationToken);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));

        var request = Assert.Single(fixture.Stub.RequestsWithKey(callId));
        Assert.Equal(("POST", "/tenant-a/probe", $"Bearer {fixture.Token("a")}"), (request.Method, request.Path, request.Authorization));
        Assert.Equal(PackageCalls.Succeeded, (await CallAsync("a", callId)).State);
        Assert.Equal((ProcessStarts.Running, 3L, "done", false), await tenant.InstanceAsync(instanceId));
    }

    [Fact]
    public async Task A_call_to_another_host_fails_the_call_and_the_instance_and_names_the_binding()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId, callId) = await fixture.StageAsync("a", "PackageProbeOtherHost", CancellationToken);

        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallAsync("a", CancellationToken));

        Assert.Empty(fixture.Stub.RequestsWithKey(callId));
        Assert.Equal(ProcessStarts.Failed, (await tenant.InstanceAsync(instanceId)).State);
        var call = await CallAsync("a", callId);
        Assert.Equal(PackageCalls.Failed, call.State);
        var error = (await tenant.HistoryAsync(instanceId))[1].Error;
        Assert.Equal(call.LastError, error);
        Assert.Contains("'probe'", error, StringComparison.Ordinal);
        Assert.Contains("'test-package'", error, StringComparison.Ordinal);
        Assert.Contains("'TestUrl'", error, StringComparison.Ordinal);
        Assert.DoesNotContain("other.example.test", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PackageProbeUndeclaredSecret")]
    [InlineData("PackageProbeSwallow")]
    public async Task Reading_an_undeclared_secret_fails_the_instance_even_when_the_operation_catches_it(string process)
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId, callId) = await fixture.StageAsync("a", process, CancellationToken);

        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallAsync("a", CancellationToken));

        Assert.Equal(ProcessStarts.Failed, (await tenant.InstanceAsync(instanceId)).State);
        var call = await CallAsync("a", callId);
        Assert.Equal(PackageCalls.Failed, call.State);
        var history = await tenant.HistoryAsync(instanceId);
        Assert.Contains("'OtherToken'", history[1].Error, StringComparison.Ordinal);
        Assert.Contains("'OtherToken'", call.LastError, StringComparison.Ordinal);

        // The token is a declared secret of the tenant, and it shows nowhere.
        var token = fixture.Token("a");
        var texts = new List<string> { call.LastError!, await AuditDetailsAsync("a", instanceId) };
        foreach (var row in history)
        {
            texts.AddRange([row.Input, row.Output!, row.Error ?? ""]);
        }

        foreach (var text in texts)
        {
            Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains(token, StringComparison.Ordinal));
        Assert.Contains(AuditActions.ProcessFailed, await tenant.SystemAuditActionsAsync(instanceId));
    }

    [Fact]
    public async Task The_same_call_reaches_the_url_and_the_secret_of_its_own_tenant()
    {
        var (_, _, callA) = await fixture.StageAsync("a", "PackageProbeDeclared", CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("a", CancellationToken));
        var (_, _, callB) = await fixture.StageAsync("b", "PackageProbeDeclared", CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallAsync("b", CancellationToken));

        var requestA = Assert.Single(fixture.Stub.RequestsWithKey(callA));
        var requestB = Assert.Single(fixture.Stub.RequestsWithKey(callB));
        Assert.Equal(("/tenant-a/probe", $"Bearer {fixture.Token("a")}"), (requestA.Path, requestA.Authorization));
        Assert.Equal(("/tenant-b/probe", $"Bearer {fixture.Token("b")}"), (requestB.Path, requestB.Authorization));
        Assert.NotEqual(fixture.Token("a"), fixture.Token("b"));
    }

    [Fact]
    public async Task A_runner_gets_only_the_declared_bindings_of_the_calls_tenant_and_one_runner_serves_both_tenants()
    {
        var (_, _, callA) = await fixture.StageAsync("a", "PackageProbeDeclared", CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallInRunnerAsync("a", CancellationToken));
        var connections = fixture.Relay.Connections;
        Assert.True(connections >= 1);
        var (_, _, callB) = await fixture.StageAsync("b", "PackageProbeDeclared", CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallInRunnerAsync("b", CancellationToken));

        // The second call found the runner of the first one in the pool.
        Assert.Equal(connections, fixture.Relay.Connections);
        foreach (var (tenant, call, other) in new[] { ("a", callA, "b"), ("b", callB, "a") })
        {
            var request = Assert.Single(fixture.Stub.RequestsWithKey(call));
            Assert.Equal(($"/tenant-{tenant}/probe", $"Bearer {fixture.Token(tenant)}"), (request.Path, request.Authorization));

            var line = Assert.Single(fixture.Relay.LinesOf("run"), candidate => candidate.Contains(call.ToString("D"), StringComparison.Ordinal));
            var bindings = JsonNode.Parse(line)!["bindings"]!.AsObject();
            Assert.Equal(["TestUrl"], bindings["urls"]!.AsObject().Select(pair => pair.Key));
            Assert.Equal(["TestToken"], bindings["secrets"]!.AsObject().Select(pair => pair.Key));
            Assert.Equal(fixture.Token(tenant), bindings["secrets"]!["TestToken"]!.GetValue<string>());
            Assert.EndsWith($"/tenant-{tenant}", bindings["urls"]!["TestUrl"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.DoesNotContain("OtherToken", line, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.OtherToken(tenant), line, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Token(other), line, StringComparison.Ordinal);
            Assert.DoesNotContain($"tenant-{other}", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_runner_that_is_asked_for_an_undeclared_secret_fails_the_call_after_one_attempt_and_names_the_binding()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId, callId) = await fixture.StageAsync("a", "PackageProbeUndeclaredSecret", CancellationToken);

        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallInRunnerAsync("a", CancellationToken));

        Assert.Equal(ProcessStarts.Failed, (await tenant.InstanceAsync(instanceId)).State);
        var call = await CallAsync("a", callId);
        Assert.Equal(PackageCalls.Failed, call.State);
        Assert.Equal(1, await tenant.ScalarAsync("SELECT attempts FROM axis.package_calls WHERE id = @id", ("id", callId)));
        Assert.Equal(
            "failed",
            await tenant.ScalarAsync("SELECT outcome FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId)));
        Assert.Equal(1L, await tenant.CountAsync("SELECT count(*) FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId)));
        Assert.Contains("'OtherToken'", call.LastError, StringComparison.Ordinal);
        Assert.Contains("'probe'", call.LastError, StringComparison.Ordinal);
        Assert.Contains("'test-package'", call.LastError, StringComparison.Ordinal);
        Assert.Equal(call.LastError, (await tenant.HistoryAsync(instanceId))[1].Error);
        Assert.DoesNotContain(fixture.OtherToken("a"), call.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_runner_that_throws_a_retryable_exception_leaves_the_call_pending_with_a_retryable_attempt()
    {
        var tenant = fixture.Tenant("a");
        var (_, instanceId, callId) = await fixture.StageAsync("a", "PackageFlakyOnce", CancellationToken);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallInRunnerAsync("a", CancellationToken));

        var call = await CallAsync("a", callId);
        Assert.Equal((PackageCalls.Pending, "Axis.Extensions.RetryableException"), (call.State, call.LastError));
        Assert.Equal(ProcessStarts.Waiting, (await tenant.InstanceAsync(instanceId)).State);
        Assert.Equal(
            "retryable",
            await tenant.ScalarAsync("SELECT outcome FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId)));
    }

    [Fact]
    public async Task Activating_a_release_for_a_tenant_without_the_declared_url_binding_is_refused()
    {
        var result = await fixture.TryActivateAsync("a", ProcessStepFixture.StepAppBindings("a"));

        var missing = result.Diagnostics.Where(diagnostic => diagnostic.Message.Contains("'TestUrl'", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, missing.Count);
        Assert.All(missing, diagnostic => Assert.Equal((DiagnosticCodes.MissingDeploymentBinding, "/steps/0/operation"), (diagnostic.Code, diagnostic.Path)));
        var declared = Assert.Single(missing, diagnostic => diagnostic.File == "processes/package-probe-declared.json");
        Assert.Contains("'a'", declared.Message, StringComparison.Ordinal);
        Assert.Contains("'TestToken'", result.Diagnostics.Select(diagnostic => diagnostic.Message).First(message => !message.Contains("'TestUrl'", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Activating_a_release_with_an_http_url_binding_outside_development_is_refused()
    {
        var result = await fixture.TryActivateAsync(
            "a",
            ProcessStepFixture.PackageBindings("a", new Uri("http://test.example.test"), allowHttp: false));

        Assert.NotEmpty(result.Diagnostics);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal((DiagnosticCodes.InsecureBaseUrl, "/steps/0/operation"), (diagnostic.Code, diagnostic.Path)));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.File == "processes/package-probe-declared.json" && diagnostic.Message.Contains("'TestUrl'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_worker_outside_development_retries_a_call_whose_url_binding_is_http_and_runs_nothing()
    {
        var (_, instanceId, callId) = await fixture.StageAsync("a", "PackageProbeDeclared", CancellationToken);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOutsideDevelopmentAsync("a", CancellationToken));

        Assert.Empty(fixture.Stub.RequestsWithKey(callId));
        var call = await CallAsync("a", callId);
        Assert.Equal(PackageCalls.Pending, call.State);
        Assert.Contains("is not an https URL", call.LastError, StringComparison.Ordinal);
        Assert.Equal(ProcessStarts.Waiting, (await fixture.Tenant("a").InstanceAsync(instanceId)).State);
    }

    /// <summary>The call's state and last error.</summary>
    private async Task<(string State, string? LastError)> CallAsync(string tenantId, Guid callId)
    {
        await using var command = fixture.Tenant(tenantId).DataSource.CreateCommand("SELECT state, last_error FROM axis.package_calls WHERE id = @id");
        command.Parameters.AddWithValue("id", callId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    /// <summary>The details of every audit record of the instance.</summary>
    private async Task<string> AuditDetailsAsync(string tenantId, Guid instanceId) =>
        (string)(await fixture.Tenant(tenantId).ScalarAsync(
            "SELECT string_agg(details::text, ' ') FROM axis.audit_records WHERE process_instance_id = @id",
            ("id", instanceId)))!;
}
