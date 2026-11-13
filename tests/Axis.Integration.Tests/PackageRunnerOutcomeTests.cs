using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// What a worker records when a call that it sends to a runner does not get an answer, see
/// docs/reference/process-execution.md#package-call-outcomes. <c>PackageSlow</c> runs in a real runner of a
/// package host in this process. A <see cref="RecordingRelay"/> that closes the connection after the <c>run</c>
/// stands in for a runner that exits. A closed port, and no setting, stand in for a package host that is not there. Each
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
    public async Task A_package_host_that_cannot_be_reached_makes_the_attempt_retryable_and_names_the_setting()
    {
        var (_, instanceId) = await fixture.StageAsync("PackageSum", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(Process(TestPackageHost.ClosedAddress()), CancellationToken));

        await AssertCallAsync(callId, PackageCalls.Pending, PackageCallHandler.RunnerUnreachable, "retryable");
        Assert.Contains(PackageIsolation.DotnetRunnerSetting, PackageCallHandler.RunnerUnreachable, StringComparison.Ordinal);
        Assert.Equal(ProcessStarts.Waiting, (await fixture.Tenant.InstanceAsync(instanceId)).State);
    }

    [Fact]
    public async Task No_package_host_makes_the_attempt_retryable_and_names_the_setting()
    {
        var (_, instanceId) = await fixture.StageAsync("PackageSum", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(
            WorkItemOutcome.Rescheduled,
            await fixture.RunCallOnAsync(new Dictionary<string, string?> { [PackageIsolation.IsolationSetting] = "process" }, CancellationToken));

        await AssertCallAsync(callId, PackageCalls.Pending, PackageCallHandler.RunnerNotSet, "retryable");
        Assert.Contains(PackageIsolation.DotnetRunnerSetting, PackageCallHandler.RunnerNotSet, StringComparison.Ordinal);
        Assert.Equal(ProcessStarts.Waiting, (await fixture.Tenant.InstanceAsync(instanceId)).State);
    }

    private static Dictionary<string, string?> Process(string address) =>
        new()
        {
            [PackageIsolation.IsolationSetting] = "process",
            [PackageIsolation.DotnetRunnerSetting] = address,
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
