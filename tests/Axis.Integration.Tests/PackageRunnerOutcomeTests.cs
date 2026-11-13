using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// What a worker records when a call that it sends to a runner does not get an answer, see
/// docs/reference/process-execution.md#package-call-outcomes. <c>PackageSlow</c> runs in a real runner of a
/// package host in this process. <c>PackageExit</c> and <c>PackageExitIdempotent</c> end their own runner process and
/// stand in for a runner that crashes. A <see cref="RecordingRelay"/> that closes the connection after the <c>run</c>
/// stands in for a runner that closes its connection. A closed port, and no setting, stand in for a package host that is not there. Each
/// wait ends on a state, under a deadline that only bounds a hang.
/// </summary>
public sealed class PackageRunnerOutcomeTests(PackageCallFixture fixture) : IClassFixture<PackageCallFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_call_that_runs_into_its_timeout_in_a_runner_is_left_unknown_and_the_runner_process_exits()
    {
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (_, instanceId) = await fixture.StageAsync("PackageSlow", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(Process(host.Address), CancellationToken));

        await AssertCallAsync(callId, PackageCalls.Unknown, PackageCallHandler.TimedOut, "unknown");
        Assert.Equal((ProcessStarts.Waiting, 2L, "slow", false), await fixture.Tenant.InstanceAsync(instanceId));
        var runner = Assert.Single(host.RunnerIds(TestPackages.Id, "1.0.0"));
        await Polling.WaitUntilAsync(() => TestPackageHost.HasExited(runner), "The runner process did not exit after the timeout.");
    }

    [Fact]
    public async Task An_idempotent_call_that_runs_into_its_timeout_in_a_runner_is_retried_and_the_runner_process_exits()
    {
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (_, instanceId) = await fixture.StageAsync("PackageSlowIdempotent", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(Process(host.Address), CancellationToken));

        await AssertCallAsync(callId, PackageCalls.Pending, PackageCallHandler.TimedOut, "unknown");
        Assert.NotNull(await fixture.DueAtAsync(callId));
        var runner = Assert.Single(host.RunnerIds(TestPackages.Id, "1.0.0"));
        await Polling.WaitUntilAsync(() => TestPackageHost.HasExited(runner), "The runner process did not exit after the timeout.");
    }

    [Fact]
    public async Task An_operation_that_ends_its_runner_process_leaves_the_call_unknown()
    {
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (_, instanceId) = await fixture.StageAsync("PackageExit", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(Process(host.Address), CancellationToken));

        await AssertCallAsync(callId, PackageCalls.Unknown, PackageCallHandler.RunnerExited, "unknown");
        var instance = await fixture.Tenant.InstanceAsync(instanceId);
        Assert.Equal((ProcessStarts.Waiting, "exit"), (instance.State, instance.Step));
        var runner = Assert.Single(host.RunnerIds(TestPackages.Id, "1.0.0"));
        await Polling.WaitUntilAsync(() => TestPackageHost.HasExited(runner), "The runner process did not exit after it ended itself.");
    }

    [Fact]
    public async Task An_idempotent_operation_that_ends_its_runner_process_is_retried()
    {
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (_, instanceId) = await fixture.StageAsync("PackageExitIdempotent", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(Process(host.Address), CancellationToken));

        await AssertCallAsync(callId, PackageCalls.Pending, PackageCallHandler.RunnerExited, "unknown");
        Assert.NotNull(await fixture.DueAtAsync(callId));
        var runner = Assert.Single(host.RunnerIds(TestPackages.Id, "1.0.0"));
        await Polling.WaitUntilAsync(() => TestPackageHost.HasExited(runner), "The runner process did not exit after it ended itself.");
    }

    [Theory]
    [InlineData("PackageSlow")]
    [InlineData("PackageExit")]
    public async Task After_a_runner_is_killed_or_crashes_the_next_call_runs_in_a_new_runner(string failing)
    {
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        await using var worker = fixture.BuildWorkerWith(Process(host.Address));
        await fixture.StageAsync(failing, CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(worker, CancellationToken));
        var first = Assert.Single(host.RunnerIds(TestPackages.Id, "1.0.0"));
        await Polling.WaitUntilAsync(() => TestPackageHost.HasExited(first), "The runner process did not exit after the failure.");

        var (subjectId, _) = await fixture.StageAsync("PackageSum", CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(worker, CancellationToken));

        var runners = host.RunnerIds(TestPackages.Id, "1.0.0");
        Assert.Equal(2, runners.Distinct().Count());
        Assert.Equal(5L, (await fixture.RequestAsync(subjectId)).Values["total"]!.GetValue<long>());
    }

    [Fact]
    public async Task A_runner_that_closes_its_connection_after_the_run_leaves_the_call_unknown()
    {
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        await using var relay = new RecordingRelay(host.Endpoint, closeAfterRun: true);
        var (_, instanceId) = await fixture.StageAsync("PackageSlow", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(Process(relay.Address), CancellationToken));

        await AssertCallAsync(callId, PackageCalls.Unknown, PackageCallHandler.RunnerExited, "unknown");
        Assert.Single(relay.LinesOf("run"));
        Assert.Equal((ProcessStarts.Waiting, 2L, "slow", false), await fixture.Tenant.InstanceAsync(instanceId));
        var runner = Assert.Single(host.RunnerIds(TestPackages.Id, "1.0.0"));
        await Polling.WaitUntilAsync(() => TestPackageHost.HasExited(runner), "The runner process did not exit after its connection closed.");
    }

    [Fact]
    public async Task A_node_call_goes_to_a_runner_of_the_node_host_with_inProcess_and_its_output_lands_on_the_record()
    {
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (subjectId, _) = await fixture.StageAsync("PackageNodeGreet", CancellationToken);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(Process(null, host.Address, "inProcess"), CancellationToken));

        var request = await fixture.RequestAsync(subjectId);
        Assert.Contains(request.Values["title"]!.GetValue<string>(), request.Values["greeting"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Single(host.RunnerIds("node-test-package", "1.0.0"));
    }

    [Fact]
    public async Task A_node_call_goes_to_a_runner_of_the_node_host_with_process_and_its_output_lands_on_the_record()
    {
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (subjectId, _) = await fixture.StageAsync("PackageNodeSum", CancellationToken);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(Process(null, host.Address, "process"), CancellationToken));

        Assert.Equal(5L, (await fixture.RequestAsync(subjectId)).Values["total"]!.GetValue<long>());
        Assert.Single(host.RunnerIds("node-probe-package", "1.0.0"));
    }

    [Theory]
    [InlineData("PackageSum", PackageIsolation.DotnetRunnerSetting)]
    [InlineData("PackageNodeSum", PackageIsolation.NodeRunnerSetting)]
    public async Task A_package_host_that_cannot_be_reached_makes_the_attempt_retryable_and_names_the_setting(string process, string setting)
    {
        var (_, instanceId) = await fixture.StageAsync(process, CancellationToken);
        var callId = await CallIdAsync(instanceId);
        var values = Process(null, null, "process");
        values[setting] = TestPackageHost.ClosedAddress();

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(values, CancellationToken));

        await AssertCallAsync(callId, PackageCalls.Pending, PackageCallHandler.RunnerUnreachable(setting), "retryable");
        Assert.Contains(setting, PackageCallHandler.RunnerUnreachable(setting), StringComparison.Ordinal);
        Assert.Equal(ProcessStarts.Waiting, (await fixture.Tenant.InstanceAsync(instanceId)).State);
    }

    [Theory]
    [InlineData("PackageSum", PackageIsolation.DotnetRunnerSetting)]
    [InlineData("PackageNodeSum", PackageIsolation.NodeRunnerSetting)]
    public async Task No_package_host_makes_the_attempt_retryable_and_names_the_setting(string process, string setting)
    {
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (_, instanceId) = await fixture.StageAsync(process, CancellationToken);
        var callId = await CallIdAsync(instanceId);

        // The host of the other runtime is live, so only the setting of the call's runtime is missing.
        var values = Process(null, null, "process");
        values[setting == PackageIsolation.DotnetRunnerSetting ? PackageIsolation.NodeRunnerSetting : PackageIsolation.DotnetRunnerSetting] = host.Address;

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(values, CancellationToken));

        await AssertCallAsync(callId, PackageCalls.Pending, PackageCallHandler.RunnerNotSet(setting), "retryable");
        Assert.Contains(setting, PackageCallHandler.RunnerNotSet(setting), StringComparison.Ordinal);
        Assert.Equal(ProcessStarts.Waiting, (await fixture.Tenant.InstanceAsync(instanceId)).State);
        Assert.Empty(host.RunnerIds(TestPackages.Id, "1.0.0"));
        Assert.Empty(host.RunnerIds("node-probe-package", "1.0.0"));
    }

    private static Dictionary<string, string?> Process(string address) => Process(address, null, "process");

    /// <summary>The settings of a worker with <paramref name="isolation"/> that sends C# attempts to <paramref name="dotnet"/> and <c>node</c> attempts to <paramref name="node"/>, each unset when <see langword="null"/>.</summary>
    private static Dictionary<string, string?> Process(string? dotnet, string? node, string isolation) =>
        new()
        {
            [PackageIsolation.IsolationSetting] = isolation,
            [PackageIsolation.DotnetRunnerSetting] = dotnet,
            [PackageIsolation.NodeRunnerSetting] = node,
            [PackageIsolation.ProxySetting] = "0",
        };

    private async Task<Guid> CallIdAsync(Guid instanceId) =>
        (Guid)(await fixture.Tenant.ScalarAsync("SELECT id FROM axis.package_calls WHERE process_instance_id = @id", ("id", instanceId)))!;

    /// <summary>The call holds <paramref name="state"/> and <paramref name="error"/>, and its one attempt row holds <paramref name="outcome"/> and the same error.</summary>
    private async Task AssertCallAsync(Guid callId, string state, string error, string outcome)
    {
        Assert.Equal(
            (state, error),
            ((string)(await fixture.Tenant.ScalarAsync("SELECT state FROM axis.package_calls WHERE id = @id", ("id", callId)))!,
                (string?)await fixture.Tenant.ScalarAsync("SELECT last_error FROM axis.package_calls WHERE id = @id", ("id", callId))));
        Assert.Equal(1, await fixture.Tenant.ScalarAsync("SELECT attempts FROM axis.package_calls WHERE id = @id", ("id", callId)));
        Assert.Equal(
            (outcome, error, 1L),
            ((string)(await fixture.Tenant.ScalarAsync("SELECT outcome FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId)))!,
                (string?)await fixture.Tenant.ScalarAsync("SELECT error FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId)),
                await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId))));
    }
}
