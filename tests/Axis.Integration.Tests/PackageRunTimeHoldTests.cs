using System.Globalization;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Processes.Instances;
using Axis.Processes.Work;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

/// <summary>
/// What a cell's worker does with a call to an application's own package when the tenant's <c>packageCpuSeconds</c> counter of the day is at or
/// past its <c>packageCpuSecondsPerDay</c> quota, see docs/reference/tenant-quotas.md#package-run-time-a-day. The calls run through a worker built
/// from the tenant directory. A held call never reaches a runner, so those tests start no package host. The tests need <c>python3</c> on the
/// path where a runner starts, so they are skipped on Windows. The clock of the worker stands still, so a test reads the counter of one day, and
/// no test waits for elapsed time.
/// </summary>
public sealed class PackageRunTimeHoldTests(ApplicationPackageCallFixture fixture) : IClassFixture<ApplicationPackageCallFixture>
{
    private const string PythonOnWindows = "The Python runner tests need python3 on the path.";

    private const string Tenant = ApplicationPackageCallFixture.TenantA;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(3600, 3600)]
    [InlineData(0, null)]
    public async Task At_the_quota_a_call_stays_pending_with_no_attempt_and_is_due_at_the_next_midnight(int quota, int? used)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        await ClearNoticesAsync();
        var (platform, catalog) = await fixture.PlatformAsync(CancellationToken);
        var clock = new ManualClock();
        await using var worker = fixture.BuildWorker(null, directory: new(platform, catalog, clock, Cell: true, PackageCpuSecondsPerDay: quota));
        if (used is { } seconds)
        {
            await SeedAsync(platform, Today(clock), "packageCpuSeconds", seconds);
        }

        var (_, instanceId) = await fixture.StageAsync(Tenant, "ApplicationSpin", CancellationToken);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(Tenant, worker, CancellationToken));

        await AssertHeldAsync(instanceId, clock);
    }

    [Fact]
    public async Task Two_held_calls_of_a_day_store_one_notice_with_the_limit_and_a_hold_the_next_day_stores_one_more()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        await ClearNoticesAsync();
        var (platform, catalog) = await fixture.PlatformAsync(CancellationToken);
        var clock = new ManualClock();
        await using var worker = fixture.BuildWorker(null, directory: new(platform, catalog, clock, Cell: true));
        await SeedAsync(platform, Today(clock), "packageCpuSeconds", 3600);

        await HoldOneAsync(worker, clock);
        await HoldOneAsync(worker, clock);

        Assert.Equal([(TenantNotices.PackagesHeldSubjectKey, TenantNotices.PackagesHeldBodyKey, "3600", "integer")], await NoticesAsync());
        Assert.Equal(1L, await CounterAsync(platform, Today(clock), "packageHeldNotice"));

        clock.Advance(TimeSpan.FromDays(1));
        await SeedAsync(platform, Today(clock), "packageCpuSeconds", 3600);
        await HoldOneAsync(worker, clock);

        Assert.Equal(2, (await NoticesAsync()).Count);
        Assert.Equal(1L, await CounterAsync(platform, Today(clock), "packageHeldNotice"));
    }

    [Fact]
    public async Task A_call_that_starts_one_second_under_the_quota_finishes_and_takes_the_counter_past_it()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        await ClearNoticesAsync();
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(extensions.Path);
        var (platform, catalog) = await fixture.PlatformAsync(CancellationToken);
        var clock = new ManualClock();
        await using var worker = fixture.BuildWorker(host.Address, directory: new(platform, catalog, clock, Cell: true));
        await SeedAsync(platform, Today(clock), "packageCpuSeconds", 3599);

        await fixture.StageAsync(Tenant, "ApplicationSpin", CancellationToken);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(Tenant, worker, CancellationToken));
        Assert.Equal(3601L, await CounterAsync(platform, Today(clock), "packageCpuSeconds"));
        Assert.Empty(await NoticesAsync());
    }

    [Fact]
    public async Task An_unreadable_platform_database_keeps_the_calls_pending_and_logs_one_warning_for_the_tenant()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        await ClearNoticesAsync();
        var (platform, catalog) = await fixture.PlatformAsync(CancellationToken);
        await PostgreSqlServer.AdminScalarAsync(
            platform, $"REVOKE SELECT ON platform.tenant_daily_counters FROM \"{PostgreSqlServer.RuntimeRole}\"", CancellationToken);
        var clock = new ManualClock();
        await using var worker = fixture.BuildWorker(null, directory: new(platform, catalog, clock, Cell: true));
        var warned = fixture.Logs.Entries.Count(Warning);

        for (var call = 0; call < 2; call++)
        {
            var (_, instanceId) = await fixture.StageAsync(Tenant, "ApplicationSpin", CancellationToken);
            Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(Tenant, worker, CancellationToken));
            Assert.Equal((PackageCalls.Pending, 0L), await CallAsync(instanceId));
            Assert.Equal(0L, await AttemptRowsAsync(instanceId));
        }

        Assert.Equal(1, fixture.Logs.Entries.Count(Warning) - warned);
        Assert.Empty(await NoticesAsync());
    }

    [Fact]
    public async Task A_tenant_with_no_limit_is_never_held_and_stores_no_notice()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        await ClearNoticesAsync();
        var (platform, catalog) = await fixture.PlatformAsync(CancellationToken, noPackageLimit: true);
        var clock = new ManualClock();
        await using var worker = fixture.BuildWorker(null, directory: new(platform, catalog, clock, Cell: true, PackageCpuSecondsPerDay: 0));
        await SeedAsync(platform, Today(clock), "packageCpuSeconds", 3600);

        var (_, instanceId) = await fixture.StageAsync(Tenant, "ApplicationSpin", CancellationToken);

        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(Tenant, worker, CancellationToken));
        Assert.Equal((PackageCalls.Pending, 1L), await CallAsync(instanceId));
        Assert.Equal(PackageCallHandler.RunnerNotSet(ApplicationPackageSettings.RunnerSetting), await fixture.Tenant(Tenant).ScalarAsync(
            "SELECT last_error FROM axis.package_calls WHERE process_instance_id = @id", ("id", instanceId)));
        Assert.Empty(await NoticesAsync());
    }

    private static bool Warning(string entry) =>
        entry.StartsWith("Warning: The platform database could not be reached to check the package run time quota of tenant a,", StringComparison.Ordinal);

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>Stages a call and runs it, which the quota holds.</summary>
    private async Task HoldOneAsync(ServiceProvider worker, ManualClock clock)
    {
        var (_, instanceId) = await fixture.StageAsync(Tenant, "ApplicationSpin", CancellationToken);
        Assert.Equal(WorkItemOutcome.Rescheduled, await fixture.RunCallOnAsync(Tenant, worker, CancellationToken));
        await AssertHeldAsync(instanceId, clock);
    }

    /// <summary>
    /// The call is <c>pending</c> with no attempt and no attempt row, its instance waits, and its one item is due at 00:00 UTC of the day after
    /// the clock's day and not claimed.
    /// </summary>
    private async Task AssertHeldAsync(Guid instanceId, TimeProvider clock)
    {
        Assert.Equal((PackageCalls.Pending, 0L), await CallAsync(instanceId));
        Assert.Equal(0L, await AttemptRowsAsync(instanceId));
        Assert.Equal(ProcessStarts.Waiting, (await fixture.Tenant(Tenant).InstanceAsync(instanceId)).State);
        await using var command = fixture.Tenant(Tenant).DataSource.CreateCommand(
            "SELECT due_at, claim_token IS NULL FROM axis.process_work_items WHERE kind = 'package.call'");
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        var dueAt = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc));
        Assert.Equal(new DateTimeOffset(Today(clock).AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), dueAt);
        Assert.True(reader.GetBoolean(1));
        Assert.False(await reader.ReadAsync(CancellationToken));
    }

    private async Task<(string State, long Attempts)> CallAsync(Guid instanceId)
    {
        await using var command = fixture.Tenant(Tenant).DataSource.CreateCommand(
            "SELECT state, attempts FROM axis.package_calls WHERE process_instance_id = @id");
        command.Parameters.AddWithValue("id", instanceId);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (reader.GetString(0), reader.GetInt32(1));
    }

    private async Task<long> AttemptRowsAsync(Guid instanceId) =>
        await fixture.Tenant(Tenant).CountAsync(
            """
            SELECT count(*) FROM axis.process_call_attempts a
            JOIN axis.package_calls c ON c.id = a.call_id AND a.call_kind = 'package'
            WHERE c.process_instance_id = @id
            """,
            ("id", instanceId));

    private static Task<object?> SeedAsync(string platform, DateOnly day, string counter, long value) =>
        PostgreSqlServer.AdminScalarAsync(
            platform,
            $"INSERT INTO platform.tenant_daily_counters (tenant_id, day, counter, value) VALUES ('{Tenant}', '{day:yyyy-MM-dd}', '{counter}', {value}) ON CONFLICT (tenant_id, day, counter) DO UPDATE SET value = excluded.value",
            CancellationToken);

    private static async Task<long> CounterAsync(string platform, DateOnly day, string counter) =>
        Convert.ToInt64(
            await PostgreSqlServer.AdminScalarAsync(
                platform,
                $"SELECT coalesce(sum(value), 0) FROM platform.tenant_daily_counters WHERE tenant_id = '{Tenant}' AND day = '{day:yyyy-MM-dd}' AND counter = '{counter}'",
                CancellationToken),
            CultureInfo.InvariantCulture);

    /// <summary>The tenant notices of the shared tenant database, which have no application: subject key, body key, limit and its type.</summary>
    private async Task<IReadOnlyList<(string Subject, string Body, string Limit, string Type)>> NoticesAsync()
    {
        await using var command = fixture.Tenant(Tenant).DataSource.CreateCommand(
            """
            SELECT subject_key, body_key, "values"->>'limit', value_types->>'limit'
            FROM axis.notifications
            WHERE application_id IS NULL AND subject_key LIKE 'notices.packagesHeld.%'
            ORDER BY created_at
            """);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var rows = new List<(string, string, string, string)>();
        while (await reader.ReadAsync(CancellationToken))
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return rows;
    }

    /// <summary>The tests share the tenant database, so each starts with no tenant notice.</summary>
    private async Task ClearNoticesAsync() => await fixture.Tenant(Tenant).ScalarAsync("DELETE FROM axis.notifications WHERE application_id IS NULL");
}
