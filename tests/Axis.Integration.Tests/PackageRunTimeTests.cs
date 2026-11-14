using System.Globalization;
using Axis.Configuration.Tests;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Tenancy;

namespace Axis.Integration.Tests;

/// <summary>
/// What a cell's worker counts in the tenant's <c>packageCpuSeconds</c> counter of the day when it sends the attempt of a call to an application's
/// own package to a runner, see docs/reference/tenant-quotas.md#package-run-time-a-day. The calls run through a worker built from the tenant
/// directory, a package host in this process and a real Python runner process that the host starts from the built files of the call's release.
/// The tests need <c>python3</c> on the path, so they are skipped on Windows. The clock of the worker stands still, so a test reads the
/// counter of one day, and no test waits for elapsed time.
/// </summary>
public sealed class PackageRunTimeTests(ApplicationPackageCallFixture fixture) : IClassFixture<ApplicationPackageCallFixture>
{
    private const string PythonOnWindows = "The Python runner tests need python3 on the path.";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_cell_worker_adds_the_CPU_seconds_of_a_call_rounded_up_to_the_tenants_day()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path);
        var (platform, catalog) = await fixture.PlatformAsync(CancellationToken);
        var clock = new ManualClock();
        await using var worker = fixture.BuildWorker(host.Address, directory: new(platform, catalog, clock, Cell: true));

        await fixture.StageAsync(ApplicationPackageCallFixture.TenantA, "ApplicationSpin", CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(ApplicationPackageCallFixture.TenantA, worker, CancellationToken));

        Assert.Equal(2L, await CounterAsync(platform, Today(clock)));
    }

    [Fact]
    public async Task A_worker_outside_a_cell_completes_the_call_and_counts_nothing_even_with_a_quota_of_0()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path);
        var (platform, catalog) = await fixture.PlatformAsync(CancellationToken);
        await using var worker = fixture.BuildWorker(
            host.Address, directory: new(platform, catalog, new ManualClock(), Cell: false, PackageCpuSecondsPerDay: 0));

        await fixture.StageAsync(ApplicationPackageCallFixture.TenantA, "ApplicationSpin", CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(ApplicationPackageCallFixture.TenantA, worker, CancellationToken));

        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(platform, "SELECT count(*) FROM platform.tenant_daily_counters", CancellationToken));
    }

    [Fact]
    public async Task A_call_that_passes_its_timeout_counts_the_seconds_from_the_run_to_its_end()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path);
        var (platform, catalog) = await fixture.PlatformAsync(CancellationToken);
        var clock = new ManualClock();
        await using var worker = fixture.BuildWorker(host.Address, directory: new(platform, catalog, clock, Cell: true));

        await fixture.StageAsync(ApplicationPackageCallFixture.TenantA, "ApplicationSpinPastTimeout", CancellationToken);
        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(ApplicationPackageCallFixture.TenantA, worker, CancellationToken));

        Assert.True(await CounterAsync(platform, Today(clock)) >= 10);
    }

    [Fact]
    public async Task A_count_that_fails_does_not_fail_the_calls_and_logs_one_warning_for_the_tenant()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path);
        var (platform, catalog) = await fixture.PlatformAsync(CancellationToken);
        await PostgreSqlServer.AdminScalarAsync(
            platform, $"REVOKE INSERT, UPDATE ON platform.tenant_daily_counters FROM \"{PostgreSqlServer.RuntimeRole}\"", CancellationToken);
        var clock = new ManualClock();
        await using var worker = fixture.BuildWorker(host.Address, directory: new(platform, catalog, clock, Cell: true));
        var warned = fixture.Logs.Entries.Count(Warning);

        await fixture.StageAsync(ApplicationPackageCallFixture.TenantA, "ApplicationSpin", CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(ApplicationPackageCallFixture.TenantA, worker, CancellationToken));
        await fixture.StageAsync(ApplicationPackageCallFixture.TenantA, "ApplicationSpin", CancellationToken);
        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(ApplicationPackageCallFixture.TenantA, worker, CancellationToken));

        Assert.Equal(1, fixture.Logs.Entries.Count(Warning) - warned);
        Assert.Equal(0L, await CounterAsync(platform, Today(clock)));
    }

    private static bool Warning(string entry) =>
        entry.StartsWith("Warning: The platform database could not be reached to count the package run time of tenant a.", StringComparison.Ordinal);

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private static async Task<long> CounterAsync(string platform, DateOnly day) =>
        Convert.ToInt64(
            await PostgreSqlServer.AdminScalarAsync(
                platform,
                $"SELECT coalesce(sum(value), 0) FROM platform.tenant_daily_counters WHERE tenant_id = '{ApplicationPackageCallFixture.TenantA}' AND day = '{day:yyyy-MM-dd}' AND counter = 'packageCpuSeconds'",
                CancellationToken),
            CultureInfo.InvariantCulture);
}
