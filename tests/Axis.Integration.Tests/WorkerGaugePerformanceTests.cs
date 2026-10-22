using System.Diagnostics;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The worker's gauge refresh over a hundred thousand work items, most of them due in the future, answers
/// within the time limit and reports the due items. The query that reads the due items uses the due-time index.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class WorkerGaugePerformanceTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string Kind = "test.gauge-performance";
    private const int FutureWorkItems = 100_000;
    private const int DueWorkItems = 100;

    // The refresh reads the due items through the due-time index and the pending fields of the tenant, so it is
    // expected to take a few milliseconds. The bound is far above that, so shared CI runners do not make the test
    // flaky, and it still fails when the read scans the table. The plan check also catches a lost index.
    private static readonly TimeSpan _refreshTime = TimeSpan.FromMilliseconds(500);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_gauge_refresh_over_a_hundred_thousand_work_items_answers_within_the_time_limit_and_reports_the_due_ones()
    {
        // A healthy queue is mostly future work, such as deadlines, with a little work due now. On a tiny or
        // fully due queue a full table read is the right plan, so the plan check would prove nothing.
        var tenant = $"gauge-performance-{Guid.NewGuid():N}";
        var connectionString = await CreateTenantDatabaseAsync();
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using (var insert = dataSource.CreateCommand(
            $"""
            INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at)
            SELECT gen_random_uuid(), @tenant, @kind,
                   CASE WHEN n <= {DueWorkItems} THEN now() - interval '1 minute' ELSE now() + interval '1 day' END
            FROM generate_series(1, {FutureWorkItems + DueWorkItems}) AS n
            """))
        {
            insert.Parameters.AddWithValue("tenant", tenant);
            insert.Parameters.AddWithValue("kind", Kind);
            await insert.ExecuteNonQueryAsync(CancellationToken);
        }

        await using (var analyze = dataSource.CreateCommand("ANALYZE axis.process_work_items"))
        {
            await analyze.ExecuteNonQueryAsync(CancellationToken);
        }

        await using var services = BuildWorker(tenant, connectionString);
        using var collector = new MetricCollector();
        var gauges = services.GetRequiredService<WorkerGauges>();
        await gauges.RefreshAsync(CancellationToken);

        var stopwatch = Stopwatch.StartNew();
        await gauges.RefreshAsync(CancellationToken);
        stopwatch.Stop();

        TestContext.Current.TestOutputHelper?.WriteLine($"The gauge refresh took {stopwatch.Elapsed}.");
        Assert.True(stopwatch.Elapsed < _refreshTime, $"The gauge refresh took {stopwatch.Elapsed}.");
        Assert.Equal(DueWorkItems, collector.GaugeValue("axis.work_items.due", ("axis.tenant", tenant), ("axis.work_item.kind", Kind)));

        var plan = await QueryPlans.ExplainAsync(dataSource, WorkItemQueue.ReadDueSql, command => command.Parameters.AddWithValue("tenant", tenant), CancellationToken);
        QueryPlans.AssertUsesIndex(plan, "ix_process_work_items_due_at");
    }

    private static ServiceProvider BuildWorker(string tenant, string connectionString)
    {
        var settings = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Tenants:{tenant}:Hosts:0"] = $"{tenant}.example.test",
                [$"Tenants:{tenant}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(connectionString),
            })
            .Build();
        return new ServiceCollection()
            .AddLogging()
            .AddAxisWorker(settings, new HostingEnvironment { EnvironmentName = "Testing" })
            .BuildServiceProvider();
    }

    private async Task<string> CreateTenantDatabaseAsync()
    {
        var connectionString = await database.CreateDatabaseAsync("worker_gauge_performance", CancellationToken);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        await using var processes = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options);
        await configuration.Database.MigrateAsync(CancellationToken);
        await data.Database.MigrateAsync(CancellationToken);
        await processes.Database.MigrateAsync(CancellationToken);
        await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
        return connectionString;
    }
}
