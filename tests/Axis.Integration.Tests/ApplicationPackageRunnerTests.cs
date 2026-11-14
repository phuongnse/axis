using Axis.Configuration.Model;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// What a worker records when it sends the attempt of a call to an application's own package to a runner of
/// <c>application-package-host</c>, see docs/reference/extension-packages.md#an-applications-packages. The calls run through the worker, a
/// package host in this process, which stands in for <c>application-package-host</c>, and a real Python runner process that the host starts from the
/// built files of the call's release. The tests need <c>python3</c> on the path, so they are skipped on Windows. Each wait ends on a state,
/// never on elapsed time.
/// </summary>
public sealed class ApplicationPackageRunnerTests(ApplicationPackageCallFixture fixture) : IClassFixture<ApplicationPackageCallFixture>
{
    private const string PythonOnWindows = "The Python runner tests need python3 on the path.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_tenants_calls_run_in_runners_of_its_own()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path);
        await using var worker = fixture.BuildWorker(host.Address);

        var firstA = await RunPidAsync(ApplicationPackageCallFixture.TenantA, worker);
        var b = await RunPidAsync(ApplicationPackageCallFixture.TenantB, worker);
        var secondA = await RunPidAsync(ApplicationPackageCallFixture.TenantA, worker);

        Assert.NotEqual(firstA, b);
        Assert.Equal(firstA, secondA);
        Assert.Equal([firstA], host.TenantRunnerIds(ApplicationPackageCallFixture.TenantA, TestPackages.PythonId, "1.0.0"));
        Assert.Equal([b], host.TenantRunnerIds(ApplicationPackageCallFixture.TenantB, TestPackages.PythonId, "1.0.0"));
    }

    [Fact]
    public async Task A_release_with_other_built_files_of_the_same_version_runs_in_a_new_runner()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path);
        await using var worker = fixture.BuildWorker(host.Address);

        var first = await RunPidAsync(ApplicationPackageCallFixture.TenantA, worker);
        var changed = await RunPidAsync(ApplicationPackageCallFixture.TenantA, worker, fixture.ChangedBuildReleaseA);

        Assert.NotEqual(first, changed);
        Assert.Equal([first, changed], host.TenantRunnerIds(ApplicationPackageCallFixture.TenantA, TestPackages.PythonId, "1.0.0"));
    }

    [Fact]
    public async Task A_request_to_a_host_that_no_url_binding_declares_fails_the_call_at_the_egress_proxy()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path);
        await using var worker = fixture.BuildWorker(host.Address);
        var (_, instanceId) = await fixture.StageAsync(ApplicationPackageCallFixture.TenantA, "ApplicationProbeOwnOtherHost", CancellationToken);
        var callId = await CallIdAsync(ApplicationPackageCallFixture.TenantA, instanceId);

        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallOnAsync(ApplicationPackageCallFixture.TenantA, worker, CancellationToken));

        var error = PackageOperationContext.RefusedMessage(
            "probe", TestPackages.PythonId, "sent a request through the egress proxy to a host that none of its url bindings declares.");
        await AssertCallAsync(ApplicationPackageCallFixture.TenantA, callId, PackageCalls.Failed, error, "failed", 1);
        Assert.Single(host.TenantRunnerIds(ApplicationPackageCallFixture.TenantA, TestPackages.PythonId, "1.0.0"));
    }

    [Fact]
    public async Task A_release_that_holds_the_source_but_no_built_files_fails_the_call_at_once_and_starts_no_runner()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path);
        await using var worker = fixture.BuildWorker(host.Address);
        var (_, instanceId) = await fixture.StageAsync(ApplicationPackageCallFixture.TenantA, "ApplicationPid", CancellationToken, fixture.NoBuiltFilesReleaseA);
        var callId = await CallIdAsync(ApplicationPackageCallFixture.TenantA, instanceId);

        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallOnAsync(ApplicationPackageCallFixture.TenantA, worker, CancellationToken));

        var error = PackageCallHandler.NoBuiltFiles(TestPackages.PythonId);
        Assert.DoesNotContain("/", error, StringComparison.Ordinal);
        await AssertCallAsync(ApplicationPackageCallFixture.TenantA, callId, PackageCalls.Failed, error, null, 0);
        Assert.Empty(host.TenantRunnerIds(ApplicationPackageCallFixture.TenantA, TestPackages.PythonId, "1.0.0"));
    }

    [Fact]
    public async Task No_application_package_host_makes_the_attempt_retryable_and_names_the_setting()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path);
        await using var worker = fixture.BuildWorker(null);
        var (_, instanceId) = await fixture.StageAsync(ApplicationPackageCallFixture.TenantA, "ApplicationPid", CancellationToken);
        var callId = await CallIdAsync(ApplicationPackageCallFixture.TenantA, instanceId);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(ApplicationPackageCallFixture.TenantA, worker, CancellationToken));

        var error = PackageCallHandler.RunnerNotSet(ApplicationPackageSettings.RunnerSetting);
        Assert.Contains(ApplicationPackageSettings.RunnerSetting, error, StringComparison.Ordinal);
        await AssertCallAsync(ApplicationPackageCallFixture.TenantA, callId, PackageCalls.Pending, error, "retryable", 1);
        Assert.Empty(host.TenantRunnerIds(ApplicationPackageCallFixture.TenantA, TestPackages.PythonId, "1.0.0"));
    }

    /// <summary>Stages <c>ApplicationPid</c> in <paramref name="tenant"/> on <paramref name="release"/> or the tenant's own, runs its call on <paramref name="worker"/> and returns the process id the runner wrote.</summary>
    private async Task<int> RunPidAsync(
        string tenant, ServiceProvider worker, (Guid ReleaseId, ApplicationModel Model)? release = null)
    {
        var (subjectId, _) = await fixture.StageAsync(tenant, "ApplicationPid", CancellationToken, release);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(tenant, worker, CancellationToken));
        return await fixture.RunnerPidAsync(tenant, subjectId);
    }

    private async Task<Guid> CallIdAsync(string tenant, Guid instanceId) =>
        (Guid)(await fixture.Tenant(tenant).ScalarAsync("SELECT id FROM axis.package_calls WHERE process_instance_id = @id", ("id", instanceId)))!;

    /// <summary>
    /// The call holds <paramref name="state"/>, <paramref name="error"/> and <paramref name="attempts"/> attempts. It has one attempt row,
    /// which holds <paramref name="outcome"/> and the same error, or none when <paramref name="outcome"/> is <see langword="null"/>.
    /// </summary>
    private async Task AssertCallAsync(string tenant, Guid callId, string state, string error, string? outcome, int attempts)
    {
        var database = fixture.Tenant(tenant);
        Assert.Equal(
            (state, error),
            ((string)(await database.ScalarAsync("SELECT state FROM axis.package_calls WHERE id = @id", ("id", callId)))!,
                (string?)await database.ScalarAsync("SELECT last_error FROM axis.package_calls WHERE id = @id", ("id", callId))));
        Assert.Equal(attempts, await database.ScalarAsync("SELECT attempts FROM axis.package_calls WHERE id = @id", ("id", callId)));
        if (outcome is null)
        {
            Assert.Equal(0L, await database.CountAsync("SELECT count(*) FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId)));
            return;
        }

        Assert.Equal(
            (outcome, error, 1L),
            ((string)(await database.ScalarAsync("SELECT outcome FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId)))!,
                (string?)await database.ScalarAsync("SELECT error FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId)),
                await database.CountAsync("SELECT count(*) FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId))));
    }
}
