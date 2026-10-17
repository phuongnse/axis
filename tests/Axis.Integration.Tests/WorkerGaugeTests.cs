using Axis.Core.Tenancy;
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
/// The worker reads the due work items of a tenant database into the <c>axis.work_items.due</c> and
/// <c>axis.work_items.oldest_due_age</c> gauges. The tenant ids are unique, because tests share the process
/// and its meters.
/// </summary>
public sealed class WorkerGaugeTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string DueKind = "test.gauge-due";
    private const string IdleKind = "test.gauge-idle";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_work_item_due_ten_minutes_ago_gives_an_oldest_due_age_of_at_least_ten_minutes()
    {
        var tenant = $"gauge-age-{Guid.NewGuid():N}";
        var connectionString = await CreateTenantDatabaseAsync("worker_gauge_age");
        await ExecuteAsync(connectionString, $"INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at) VALUES (gen_random_uuid(), '{tenant}', '{DueKind}', now() - interval '10 minutes')");
        await ExecuteAsync(connectionString, $"INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at) VALUES (gen_random_uuid(), '{tenant}', '{DueKind}', now() + interval '1 hour')");
        await using var services = BuildWorker(tenant, connectionString);
        using var collector = new MetricCollector();

        await services.GetRequiredService<WorkerGauges>().RefreshAsync(CancellationToken);

        var tags = new[] { ("axis.tenant", tenant), ("axis.work_item.kind", DueKind) };
        Assert.True(collector.GaugeValue("axis.work_items.oldest_due_age", tags) >= 600);
        Assert.Equal(1, collector.GaugeValue("axis.work_items.due", tags));
    }

    [Fact]
    public async Task A_claimed_work_item_whose_due_time_has_passed_keeps_counting_and_aging()
    {
        var tenant = $"gauge-claimed-{Guid.NewGuid():N}";
        var connectionString = await CreateTenantDatabaseAsync("worker_gauge_claimed");
        await ExecuteAsync(
            connectionString,
            $"INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, claim_token, lease_expires_at) VALUES (gen_random_uuid(), '{tenant}', '{DueKind}', now() - interval '5 minutes', gen_random_uuid(), now() + interval '1 hour')");
        await using var services = BuildWorker(tenant, connectionString);
        using var collector = new MetricCollector();

        await services.GetRequiredService<WorkerGauges>().RefreshAsync(CancellationToken);

        var tags = new[] { ("axis.tenant", tenant), ("axis.work_item.kind", DueKind) };
        Assert.Equal(1, collector.GaugeValue("axis.work_items.due", tags));
        Assert.True(collector.GaugeValue("axis.work_items.oldest_due_age", tags) >= 300);
    }

    [Fact]
    public async Task A_kind_the_worker_handles_reports_zero_when_nothing_is_due()
    {
        var tenant = $"gauge-idle-{Guid.NewGuid():N}";
        var connectionString = await CreateTenantDatabaseAsync("worker_gauge_idle");
        await using var services = BuildWorker(tenant, connectionString);
        using var collector = new MetricCollector();

        await services.GetRequiredService<WorkerGauges>().RefreshAsync(CancellationToken);

        var tags = new[] { ("axis.tenant", tenant), ("axis.work_item.kind", IdleKind) };
        Assert.Equal(0, collector.GaugeValue("axis.work_items.due", tags));
        Assert.Equal(0, collector.GaugeValue("axis.work_items.oldest_due_age", tags));
    }

    [Fact]
    public async Task A_tenant_whose_database_cannot_be_read_reports_no_data_after_a_good_read()
    {
        var tenant = $"gauge-down-{Guid.NewGuid():N}";
        var connectionString = await CreateTenantDatabaseAsync("worker_gauge_down");
        await using var services = BuildWorker(tenant, connectionString);
        using var collector = new MetricCollector();
        var gauges = services.GetRequiredService<WorkerGauges>();
        await gauges.RefreshAsync(CancellationToken);
        var tags = new[] { ("axis.tenant", tenant), ("axis.work_item.kind", IdleKind) };
        Assert.True(collector.HasPoint("axis.work_items.due", tags));

        var exported = collector.Metrics.Count;
        await ExecuteAsync(connectionString, "ALTER TABLE axis.active_releases RENAME TO active_releases_gone");
        await gauges.RefreshAsync(CancellationToken);

        // The exports after the failed read hold no point of the tenant.
        foreach (var metric in collector.Metrics.Skip(exported).Where(metric => metric.Name == "axis.work_items.due"))
        {
            foreach (var point in metric.MetricPoints)
            {
                foreach (var tag in point.Tags)
                {
                    Assert.False(tag.Key == "axis.tenant" && Equals(tag.Value, tenant), "The tenant still reports a point.");
                }
            }
        }
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
            .AddSingleton<IWorkItemHandler>(new IdleHandler())
            .BuildServiceProvider();
    }

    private async Task<string> CreateTenantDatabaseAsync(string prefix)
    {
        var connectionString = await database.CreateDatabaseAsync(prefix, CancellationToken);
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

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    /// <summary>A handler that gives the worker a kind to report, and never runs.</summary>
    private sealed class IdleHandler : IWorkItemHandler
    {
        public string Kind => IdleKind;

        public Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
