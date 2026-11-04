using System.Security.Cryptography;
using Axis.Core.Tenancy;
using Axis.Data.Storage;
using Axis.Processes.Instances;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>
/// In a cell the worker claims a connector call or a package call only with a lease of the tenant's
/// <c>concurrentConnectorCalls</c> quota, see <c>docs/reference/tenant-quotas.md#work-at-once</c>. With no lease free the
/// call stays due and unclaimed, and the work items of other tenants run.
/// </summary>
public sealed class WorkerCallLeaseTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string RecordKind = "test.record";
    private const string TestSecretKey = TestPlatform.SecretKey;

    private static readonly (string Key, string Value)[] _cellSettings =
    [
        ("Tenancy:Source", "directory"),
        ("Cell:Id", "vn-1"),
        ("Cell:Region", "vn"),
        ("Cell:TenantDomain", "vn.example.test"),
    ];

    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestSecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_a_quota_of_two_five_slow_calls_run_two_at_a_time_across_two_workers_and_all_finish()
    {
        var tenant = await PrepareTenantAsync("a");
        var platform = await database.CreateDatabaseAsync("call_lease_platform", CancellationToken);
        await SeedPlatformAsync(platform, tenant);
        var calls = new CallCounter();
        await EnqueueAsync(tenant, ProcessStarts.ConnectorCallWorkItemKind, 3);
        await EnqueueAsync(tenant, ProcessStarts.PackageCallWorkItemKind, 2);
        await using var first = BuildWorker(platform, calls, "2");
        await using var second = BuildWorker(platform, calls, "2");

        await first.StartAsync(CancellationToken);
        await second.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await ItemsAsync(tenant) == 0, "The calls did not all finish.", TimeSpan.FromSeconds(60));

        Assert.Equal(5, calls.Started);
        Assert.Equal(2, calls.Maximum);
        await WaitUntilAsync(async () => await LeasesAsync(platform) == 0, "A lease was not released.");
    }

    [Fact]
    public async Task While_a_tenant_has_no_lease_free_its_calls_stay_unclaimed_and_the_work_of_another_tenant_runs()
    {
        var a = await PrepareTenantAsync("a");
        var b = await PrepareTenantAsync("b");
        var platform = await database.CreateDatabaseAsync("call_lease_platform", CancellationToken);
        await SeedPlatformAsync(platform, a, b);
        await using var source = NpgsqlDataSource.Create(PostgreSqlServer.RuntimeConnectionString(platform));
        var held = await new TenantLeases(source, NullLogger<TenantLeases>.Instance).TryTakeAsync("a", QuotaKeys.ConcurrentConnectorCalls, 1, CancellationToken);
        Assert.NotNull(held);
        var calls = new CallCounter();
        await EnqueueAsync(a, ProcessStarts.ConnectorCallWorkItemKind, 1);
        await EnqueueAsync(a, ProcessStarts.PackageCallWorkItemKind, 1);
        await EnqueueAsync(b, ProcessStarts.ConnectorCallWorkItemKind, 2);
        await EnqueueAsync(b, RecordKind, 1);
        await using var app = BuildWorker(platform, calls, "1");

        await app.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await ItemsAsync(b) == 0, "The work of the other tenant did not run.");
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken);

        Assert.Equal(2, await ItemsAsync(a));
        Assert.Equal(2, await UnclaimedAsync(a));
        Assert.Equal(2, calls.Started);

        await held.DisposeAsync();
        await WaitUntilAsync(async () => await ItemsAsync(a) == 0, "The calls did not run once the lease was free.");
        Assert.Equal(4, calls.Started);
    }

    private static WebApplication BuildWorker(string platform, CallCounter calls, string connectorCalls)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Platform"] = PostgreSqlServer.RuntimeConnectionString(platform),
            ["Platform:SecretKey"] = TestSecretKey,
            ["Health:Urls"] = "http://127.0.0.1:0",
            ["Worker:PollInterval"] = "00:00:00.05",
            ["Worker:IdlePollCeiling"] = "00:00:00.05",
            ["Quotas:Defaults:concurrentConnectorCalls"] = connectorCalls,
        };
        foreach (var (key, value) in _cellSettings)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new LogCollector());
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        builder.Services.RemoveAll<IExternalWorkItemHandler>();
        builder.Services.AddSingleton<IExternalWorkItemHandler>(new SlowCallHandler(ProcessStarts.ConnectorCallWorkItemKind, calls));
        builder.Services.AddSingleton<IExternalWorkItemHandler>(new SlowCallHandler(ProcessStarts.PackageCallWorkItemKind, calls));
        builder.Services.AddSingleton<IWorkItemHandler, NothingHandler>();
        var app = builder.Build();
        app.MapWorkerHealth();
        return app;
    }

    // The worker reads the directory and runs the items as the runtime role.
    private async Task<CallTenant> PrepareTenantAsync(string id)
    {
        var schemaConnection = await database.CreateDatabaseAsync($"worker_calls_{id}", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(schemaConnection, CancellationToken);
        await using (var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(schemaConnection).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        var role = $"wcall_{Guid.NewGuid():N}"[..16];
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        await PostgreSqlServer.AdminScalarAsync(schemaConnection, $"CREATE ROLE {role} LOGIN PASSWORD '{password}'", CancellationToken);
        await using (var connection = new NpgsqlConnection(schemaConnection))
        {
            await connection.OpenAsync(CancellationToken);
            await RuntimeRoleGrants.ApplyAsync(connection, role, CancellationToken);
        }

        var builder = new NpgsqlConnectionStringBuilder(schemaConnection);
        return new CallTenant(id, builder.Host!, builder.Port, builder.Database!, role, password, schemaConnection);
    }

    private static async Task SeedPlatformAsync(string platform, params CallTenant[] tenants)
    {
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        await context.Database.MigrateAsync(CancellationToken);
        foreach (var tenant in tenants)
        {
            context.Tenants.Add(new TenantRow
            {
                Id = tenant.Id,
                Status = "active",
                SchemaVersion = TenantSchemaVersion.Current,
                DatabaseHost = tenant.DatabaseHost,
                DatabasePort = tenant.DatabasePort,
                DatabaseName = tenant.DatabaseName,
                RuntimeUser = tenant.Role,
                SchemaUser = PostgreSqlServer.SchemaRole,
                RuntimePassword = _cipher.Encrypt(tenant.Id, TenantSecretFields.RuntimePassword, tenant.Password),
                SchemaPassword = _cipher.Encrypt(tenant.Id, TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword),
                OidcAuthority = "https://127.0.0.1:1/realms/axis",
                OidcClientId = "axis",
                OidcClientSecret = _cipher.Encrypt(tenant.Id, TenantSecretFields.OidcClientSecret, "test"),
            });
            context.TenantHosts.Add(new TenantHostRow { TenantId = tenant.Id, Host = $"{tenant.Id}.example.test" });
        }

        await context.SaveChangesAsync(CancellationToken);
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
    }

    private static async Task EnqueueAsync(CallTenant tenant, string kind, int count)
    {
        await using var connection = new NpgsqlConnection(tenant.SchemaConnectionString);
        await connection.OpenAsync(CancellationToken);
        for (var i = 0; i < count; i++)
        {
            await WorkItemQueue.EnqueueAsync(connection, null, tenant.Id, kind, DateTimeOffset.UtcNow.AddMinutes(-1), CancellationToken);
        }
    }

    private static async Task<long> ItemsAsync(CallTenant tenant) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(tenant.SchemaConnectionString, "SELECT count(*) FROM axis.process_work_items", CancellationToken))!;

    // Items nobody claimed: no lease expiry is set.
    private static async Task<long> UnclaimedAsync(CallTenant tenant) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(
            tenant.SchemaConnectionString,
            "SELECT count(*) FROM axis.process_work_items WHERE claim_token IS NULL AND lease_expires_at IS NULL",
            CancellationToken))!;

    private static async Task<long> LeasesAsync(string platform) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(platform, "SELECT count(*) FROM platform.tenant_leases", CancellationToken))!;

    private sealed record CallTenant(string Id, string DatabaseHost, int DatabasePort, string DatabaseName, string Role, string Password, string SchemaConnectionString);

    /// <summary>Counts the calls that run at once, across every worker that shares it.</summary>
    private sealed class CallCounter
    {
        private int _running;
        private int _maximum;
        private int _started;

        public int Maximum => Volatile.Read(ref _maximum);

        public int Started => Volatile.Read(ref _started);

        public void Enter()
        {
            Interlocked.Increment(ref _started);
            var now = Interlocked.Increment(ref _running);
            int seen;
            while (now > (seen = Volatile.Read(ref _maximum)) && Interlocked.CompareExchange(ref _maximum, now, seen) != seen)
            {
            }
        }

        public void Leave() => Interlocked.Decrement(ref _running);
    }

    /// <summary>Stands in for a connector call or a package call handler: it takes a while, deletes its item and reports it completed.</summary>
    private sealed class SlowCallHandler(string kind, CallCounter calls) : IExternalWorkItemHandler
    {
        public string Kind => kind;

        public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            calls.Enter();
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
                await using var command = new NpgsqlCommand("DELETE FROM axis.process_work_items WHERE id = @id", connection);
                command.Parameters.AddWithValue("id", item.Id);
                await command.ExecuteNonQueryAsync(cancellationToken);
                return WorkItemOutcome.Completed;
            }
            finally
            {
                calls.Leave();
            }
        }
    }

    private sealed class NothingHandler : IWorkItemHandler
    {
        public string Kind => RecordKind;

        public Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
