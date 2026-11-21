using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using Axis.Configuration.Tests;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The publisher of a cell counts the CPU seconds of a <c>run</c> request's call in the tenant's package run time a day, never holds or refuses the
/// run, and keeps them out of the stored <c>run</c>. Outside a cell it counts nothing, see
/// <c>docs/reference/tenant-quotas.md#package-run-time-a-day</c>. The tests run the real <c>publisher</c> command under the directory source on a real
/// PostgreSQL database against <c>TestPackageHost</c> with a stub Trivy, and use the Python starter of <see cref="PublisherRunRequestTests"/> with an
/// operation that spins for a given number of CPU milliseconds. They use a stub shell script for Trivy, so they are skipped on Windows.
/// </summary>
[Collection(PackageBuilds.Name)]
[UnsupportedOSPlatform("windows")]
public sealed class PublisherRunRunTimeTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Counter = "packageCpuSeconds";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task In_a_cell_a_run_counts_its_cpu_seconds_rounded_up_is_not_refused_at_the_default_quota_and_stores_no_cpu_field()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);
        var before = DateOnly.FromDateTime(DateTime.UtcNow);

        var (status, run) = await RunSpinAsync(scenario, tenant, cell: true);

        var after = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal("succeeded", status);
        Assert.Equal(["durationMs", "error", "operation", "outcome", "output"], run.Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Equal("succeeded", run["outcome"]!.GetValue<string>());

        // 1200 CPU milliseconds are 2 seconds rounded up, and the default quota of a cell is 0.
        Assert.Equal(
            2L,
            await PostgreSqlServer.AdminScalarAsync(
                scenario.Platform,
                $"SELECT coalesce(sum(value), 0)::bigint FROM platform.tenant_daily_counters WHERE tenant_id = '{tenant}' AND counter = '{Counter}' AND day IN ('{before:yyyy-MM-dd}', '{after:yyyy-MM-dd}')",
                CancellationToken));
    }

    [Fact]
    public async Task Outside_a_cell_a_run_ends_succeeded_and_changes_no_counter()
    {
        await using var scenario = await AdminScenario.NewAsync(database, migrate: true, CancellationToken);
        var tenant = await CreateTenantAsync(scenario);

        var (status, _) = await RunSpinAsync(scenario, tenant, cell: false);

        Assert.Equal("succeeded", status);
        Assert.Equal(
            0L,
            await PostgreSqlServer.AdminScalarAsync(
                scenario.Platform, $"SELECT count(*) FROM platform.tenant_daily_counters WHERE tenant_id = '{tenant}'", CancellationToken));
    }

    private static async Task<string> CreateTenantAsync(AdminScenario scenario)
    {
        var id = scenario.NewTenantId();
        Assert.Equal(0, (await scenario.CreateAsync(id, $"{id}.example.test")).Exit);
        return id;
    }

    // Queues a run of the spin operation for 1200 CPU milliseconds, runs the publisher until the request ends, and reads its status and run.
    private static async Task<(string Status, JsonObject Run)> RunSpinAsync(AdminScenario scenario, string tenant, bool cell)
    {
        var files = PublisherRunRequestTests.StarterFiles();
        files["adapter.py"] = "import time\n\n" + files["adapter.py"] + "\n\ndef spin(input, context):\n    until = time.process_time() + input[\"milliseconds\"] / 1000\n    while time.process_time() < until:\n        pass\n    return {}\n";
        files["axis-package.json"] = PublisherRunRequestTests.WithOperation(
            files["axis-package.json"],
            """{ "name": "spin", "type": "spin", "idempotent": true, "input": [{ "name": "milliseconds", "type": "integer", "required": true }], "output": [] }""");
        var tenantDatabase = new NpgsqlConnectionStringBuilder(await PostgreSqlServer.AdminConnectionStringAsync()) { Database = $"axis_{tenant}" }.ConnectionString;
        using var trivy = new StubTrivy(StubTrivy.NoFindings);
        using var extensions = new TemporaryFolder();
        await using var host = await TestPackageHost.StartAsync(
            extensions.Path, settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = trivy.Command });
        var request = await PublisherRunRequestTests.QueueAsync(tenantDatabase, files, "spin", """{"milliseconds":1200}""");
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["Extensions:ApplicationPackages"] = "true",
            ["Extensions:Runners:Application"] = host.Address,
            ["Extensions:Runners:ProxyPort"] = "0",
            ["PackageBuild:Python"] = "axis-no-such-python-9183746520",
        };
        if (cell)
        {
            settings["Cell:Id"] = "vn-1";
            settings["Cell:Region"] = "vn";
            settings["Cell:TenantDomain"] = "vn.example.test";
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var publisher = scenario.RunAsync(["publisher"], stop: stop, extra: settings);
        try
        {
            await PublisherRunRequestTests.WaitForFinalAsync(tenantDatabase, request);
        }
        finally
        {
            await stop.CancelAsync();
        }

        var result = await publisher;
        Assert.Equal(0, result.Exit);
        var status = (await PublisherRunRequestTests.ColumnAsync(tenantDatabase, request, "status"))!;
        var run = JsonNode.Parse((await PublisherRunRequestTests.ColumnAsync(tenantDatabase, request, "run"))!)!.AsObject();
        return (status, run);
    }
}
