using Axis.Data.Storage;
using Axis.Processes.Storage;
using Axis.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

public sealed class WorkerSessionCleanupTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_worker_deletes_the_sessions_past_the_idle_limit_or_the_8_hour_limit_and_keeps_the_live_one()
    {
        var clock = new TestClock();
        var tenant = await CreateTenantDatabaseAsync("worker_sessions");
        var live = await InsertAsync(tenant, clock.GetUtcNow().AddHours(-1), clock.GetUtcNow().AddMinutes(-10));
        await InsertAsync(tenant, clock.GetUtcNow().AddHours(-2), clock.GetUtcNow().AddMinutes(-61));
        await InsertAsync(tenant, clock.GetUtcNow().AddHours(-8).AddMinutes(-1), clock.GetUtcNow().AddMinutes(-5));
        var logs = new LogCollector();
        using var host = BuildHost(clock, logs, ("a", tenant));

        // The real time is months after the clock, so a worker that used the database's time would delete the live row too.
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await CountAsync(tenant) == 1, "The expired sessions were not deleted.");
        await host.StopAsync(CancellationToken);

        Assert.Equal([live], await HashesAsync(tenant));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_worker_runs_the_clean_up_again_only_after_5_minutes_of_its_clock()
    {
        var clock = new TestClock();
        var tenant = await CreateTenantDatabaseAsync("worker_sessions_due");
        await InsertAsync(tenant, clock.GetUtcNow().AddHours(-2), clock.GetUtcNow().AddMinutes(-61));
        using var host = BuildHost(clock, new LogCollector(), ("a", tenant));
        await host.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await CountAsync(tenant) == 0, "The first clean-up did not run.");

        await InsertAsync(tenant, clock.GetUtcNow().AddHours(-2), clock.GetUtcNow().AddMinutes(-61));
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        Assert.Equal(1, await CountAsync(tenant));

        clock.Advance(TimeSpan.FromMinutes(5));
        await WaitUntilAsync(async () => await CountAsync(tenant) == 0, "The clean-up did not run again after 5 minutes.");
        await host.StopAsync(CancellationToken);
    }

    [Fact]
    public async Task The_worker_leaves_the_expired_sessions_of_a_tenant_it_does_not_serve_to_that_tenants_own_worker()
    {
        var clock = new TestClock();
        var tenantA = await CreateTenantDatabaseAsync("worker_sessions_a");
        var tenantB = await CreateTenantDatabaseAsync("worker_sessions_b");
        await InsertAsync(tenantA, clock.GetUtcNow().AddHours(-9), clock.GetUtcNow().AddMinutes(-5));
        await InsertAsync(tenantB, clock.GetUtcNow().AddHours(-9), clock.GetUtcNow().AddMinutes(-5));

        var logsA = new LogCollector();
        using (var hostA = BuildHost(clock, logsA, ("a", tenantA)))
        {
            await hostA.StartAsync(CancellationToken);
            await WaitUntilAsync(async () => await CountAsync(tenantA) == 0, "The worker of tenant a did not delete its expired session.");

            // Tenant a was cleaned in the first poll, so the worker had every chance to touch tenant b too.
            await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken);
            await hostA.StopAsync(CancellationToken);
        }

        Assert.Equal(1, await CountAsync(tenantB));

        var logsB = new LogCollector();
        using (var hostB = BuildHost(clock, logsB, ("b", tenantB)))
        {
            await hostB.StartAsync(CancellationToken);
            await WaitUntilAsync(async () => await CountAsync(tenantB) == 0, "The worker of tenant b did not delete its expired session.");
            await hostB.StopAsync(CancellationToken);
        }

        Assert.DoesNotContain(logsA.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
        Assert.DoesNotContain(logsB.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    private static IHost BuildHost(TimeProvider clock, LogCollector logs, params (string TenantId, string ConnectionString)[] tenants)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        var settings = new Dictionary<string, string?> { ["Worker:PollInterval"] = "00:00:00.05" };
        foreach (var (tenantId, connectionString) in tenants)
        {
            settings[$"Tenants:{tenantId}:Hosts:0"] = $"{tenantId}.example.test";
            settings[$"Tenants:{tenantId}:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(connectionString);
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(clock);
        return builder.Build();
    }

    // The worker is ready with the processes migrations alone, and the sessions table comes from the data migrations.
    private async Task<string> CreateTenantDatabaseAsync(string prefix)
    {
        var connectionString = await database.CreateDatabaseAsync(prefix, CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(connectionString, CancellationToken);
        await using (var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connectionString).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        await PostgreSqlServer.GrantRuntimeRoleAsync(connectionString, CancellationToken);
        return connectionString;
    }

    private static async Task<byte[]> InsertAsync(string connectionString, DateTimeOffset signedInAt, DateTimeOffset lastRequestAt)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray());
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO axis.sessions (id_hash, user_id, display_name, roles, signed_in_at, last_request_at)
            VALUES (@hash, 'anna', 'Anna Employee', '{employee}', @signedIn, @lastRequest)
            """);
        command.Parameters.AddWithValue("hash", hash);
        command.Parameters.AddWithValue("signedIn", signedInAt);
        command.Parameters.AddWithValue("lastRequest", lastRequestAt);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return hash;
    }

    private static async Task<long> CountAsync(string connectionString)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM axis.sessions");
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private static async Task<List<byte[]>> HashesAsync(string connectionString)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var command = dataSource.CreateCommand("SELECT id_hash FROM axis.sessions");
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var hashes = new List<byte[]>();
        while (await reader.ReadAsync(CancellationToken))
        {
            hashes.Add(reader.GetFieldValue<byte[]>(0));
        }

        return hashes;
    }

    // Months before the real time, and still until a test moves it.
    private sealed class TestClock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero).UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan time) => Interlocked.Add(ref _ticks, time.Ticks);
    }
}
