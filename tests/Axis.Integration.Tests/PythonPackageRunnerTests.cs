using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// What a worker records when it sends the attempt of a Python package call to the Python package host, see
/// docs/reference/extension-packages.md#package-runners. The calls run through the worker, a package host in this
/// process and a real Python runner process. A closed port stands in for a host that is not there. The tests need
/// <c>python3</c> on the path, so they are skipped on Windows. Each wait ends on a state, never on elapsed time.
/// </summary>
public sealed class PythonPackageRunnerTests(PackageCallFixture fixture) : IClassFixture<PackageCallFixture>
{
    private const string PythonOnWindows = "The Python runner tests need python3 on the path.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_python_call_runs_in_a_python_runner_and_writes_its_output_to_the_record()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (subjectId, instanceId) = await fixture.StageAsync("PythonGreet", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(Process(TestPackageHost.ClosedAddress(), host.Address), CancellationToken));

        var request = await fixture.RequestAsync(subjectId);
        Assert.Equal("Hello, " + request.Values["title"]!.GetValue<string>(), request.Values["comment"]!.GetValue<string>());
        await AssertCallAsync(callId, PackageCalls.Succeeded, null, "succeeded");
        Assert.Single(host.RunnerIds(TestPackages.PythonId, "1.0.0"));
    }

    [Fact]
    public async Task A_python_retryable_error_sets_the_call_back_to_pending_with_a_retryable_attempt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (_, instanceId) = await fixture.StageAsync("PythonFlaky", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(Process(TestPackageHost.ClosedAddress(), host.Address), CancellationToken));

        await AssertCallAsync(callId, PackageCalls.Pending, "axis_sdk.RetryableError", "retryable");
        Assert.NotNull(await fixture.DueAtAsync(callId));
        Assert.Single(host.RunnerIds(TestPackages.PythonId, "1.0.0"));
    }

    [Fact]
    public async Task No_python_package_host_makes_the_attempt_retryable_and_names_the_setting_while_a_dotnet_host_is_live()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (_, instanceId) = await fixture.StageAsync("PythonGreet", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(Process(host.Address, null), CancellationToken));

        var error = PackageCallHandler.RunnerNotSet(PackageLanguage.Python);
        await AssertCallAsync(callId, PackageCalls.Pending, error, "retryable");
        Assert.Contains(PackageIsolation.PythonRunnerSetting, error, StringComparison.Ordinal);
        Assert.Empty(host.RunnerIds(TestPackages.PythonId, "1.0.0"));
    }

    [Fact]
    public async Task A_python_package_host_that_cannot_be_reached_makes_the_attempt_retryable_and_names_the_setting()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (_, instanceId) = await fixture.StageAsync("PythonGreet", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(Process(host.Address, TestPackageHost.ClosedAddress()), CancellationToken));

        var error = PackageCallHandler.RunnerUnreachable(PackageLanguage.Python);
        await AssertCallAsync(callId, PackageCalls.Pending, error, "retryable");
        Assert.Contains(PackageIsolation.PythonRunnerSetting, error, StringComparison.Ordinal);
        Assert.Empty(host.RunnerIds(TestPackages.PythonId, "1.0.0"));
    }

    private static Dictionary<string, string?> Process(string dotnet, string? python) =>
        new()
        {
            [PackageIsolation.IsolationSetting] = "process",
            [PackageIsolation.DotnetRunnerSetting] = dotnet,
            [PackageIsolation.PythonRunnerSetting] = python,
            [PackageIsolation.ProxySetting] = "0",
        };

    private async Task<Guid> CallIdAsync(Guid instanceId) =>
        (Guid)(await fixture.Tenant.ScalarAsync("SELECT id FROM axis.package_calls WHERE process_instance_id = @id", ("id", instanceId)))!;

    /// <summary>The call holds <paramref name="state"/> and <paramref name="error"/>, and its one attempt row holds <paramref name="outcome"/> and the same error.</summary>
    private async Task AssertCallAsync(Guid callId, string state, string? error, string outcome)
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
