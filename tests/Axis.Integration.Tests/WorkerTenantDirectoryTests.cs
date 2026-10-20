using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Axis.Admin;
using Axis.Core.Tenancy;
using Axis.Data.Storage;
using Axis.Processes.Storage;
using Axis.Processes.Work;
using Axis.Tenancy.Platform;
using Axis.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>Under <c>Tenancy:Source=directory</c> the worker reads its tenants from the tenant directory.</summary>
public sealed class WorkerTenantDirectoryTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string RecordKind = "test.record";
    private const string TestSecretKey = TestPlatform.SecretKey;
    private const string LoadFailedLog = "Loading the tenant directory failed";
    private const string RefreshedLog = "Read the tenant directory again";
    private const string RefreshFailedLog = "could not be read again";

    private static readonly TenantSecretCipher _cipher = TenantSecretCipher.Load(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [TenantSecretCipher.KeySetting] = TestSecretKey }).Build());

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_directory_tenant_runs_its_due_work_on_its_own_database_as_its_own_role()
    {
        var a = await PrepareTenantAsync("a");
        var b = await PrepareTenantAsync("b");
        var itemA = await EnqueueAsync(a);
        var itemB = await EnqueueAsync(b);
        var platform = await database.CreateDatabaseAsync("worker_platform", CancellationToken);
        await SeedPlatformAsync(platform, a, b);
        await GrantPlatformAsync(platform);
        var logs = new LogCollector();
        await using var app = BuildWorker(platform, logs);

        await app.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await RunsAsync(a) is { Count: 1 } && await RunsAsync(b) is { Count: 1 }, "The work did not run.");

        Assert.Equal([new Run(itemA, "a", a.DatabaseName, a.Role.Name)], await RunsAsync(a));
        Assert.Equal([new Run(itemB, "b", b.DatabaseName, b.Role.Name)], await RunsAsync(b));
        Assert.Contains("Information: Loaded 2 active tenant(s) from the tenant directory.", logs.Entries);
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_worker_is_live_but_not_ready_until_the_first_directory_read_works()
    {
        var a = await PrepareTenantAsync("a");
        var platform = await database.CreateDatabaseAsync("worker_platform", CancellationToken);
        var logs = new LogCollector();
        await using var app = BuildWorker(platform, logs);
        await app.StartAsync(CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        await WaitUntilAsync(() => Task.FromResult(logs.Entries.Any(entry => entry.Contains(LoadFailedLog, StringComparison.Ordinal))), "The first read did not fail.");

        Assert.Equal((HttpStatusCode.ServiceUnavailable, "Unhealthy"), await GetAsync(client, "/health/ready"));
        Assert.Equal((HttpStatusCode.OK, "Healthy"), await GetAsync(client, "/health/live"));

        // The tables exist, but the reader role may not read them yet, so reads still fail.
        await SeedPlatformAsync(platform, a);
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "Unhealthy"), await GetAsync(client, "/health/ready"));

        await GrantPlatformAsync(platform);

        await WaitUntilAsync(async () => await GetAsync(client, "/health/ready") == (HttpStatusCode.OK, "Healthy"), "The worker did not become ready.");
        Assert.Equal((HttpStatusCode.OK, "Healthy"), await GetAsync(client, "/health/live"));
        Assert.Single(logs.Entries, entry => entry.Contains(LoadFailedLog, StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_log_holds_a_runtime_password_or_a_connection_string_when_a_read_or_a_tenant_database_fails()
    {
        var a = await PrepareTenantAsync("a");
        var unreachable = a with { Id = "c", DatabasePort = 1, Role = NewRole() };
        var itemA = await EnqueueAsync(a);
        var platform = await database.CreateDatabaseAsync("worker_platform", CancellationToken);
        var logs = new LogCollector();
        await using var app = BuildWorker(platform, logs);
        await app.StartAsync(CancellationToken);
        await WaitUntilAsync(() => Task.FromResult(logs.Entries.Any(entry => entry.Contains(LoadFailedLog, StringComparison.Ordinal))), "The first read did not fail.");
        await SeedPlatformAsync(platform, a, unreachable);
        await GrantPlatformAsync(platform);

        await WaitUntilAsync(async () => await RunsAsync(a) is { Count: 1 }, "The reachable tenant did not run.");
        await WaitUntilAsync(
            () => Task.FromResult(logs.Entries.Any(entry => entry.Contains("Running work items for tenant c failed.", StringComparison.Ordinal))),
            "The unreachable tenant did not fail.");

        Assert.Equal(itemA, (await RunsAsync(a)).Single().ItemId);
        Assert.NotEmpty(logs.Entries);
        Assert.All(logs.Entries, text =>
        {
            Assert.DoesNotContain(a.Role.Password, text, StringComparison.Ordinal);
            Assert.DoesNotContain(unreachable.Role.Password, text, StringComparison.Ordinal);
            Assert.DoesNotContain("Password=", text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task A_Tenants_section_is_ignored_with_a_warning_and_its_work_does_not_run()
    {
        var a = await PrepareTenantAsync("a");
        var x = await PrepareTenantAsync("x");
        await EnqueueAsync(a);
        await EnqueueAsync(x);
        var platform = await database.CreateDatabaseAsync("worker_platform", CancellationToken);
        await SeedPlatformAsync(platform, a);
        await GrantPlatformAsync(platform);
        var logs = new LogCollector();
        await using var app = BuildWorker(
            platform,
            logs,
            ("Tenants:x:Hosts:0", "x.example.test"),
            ("Tenants:x:ConnectionString", PostgreSqlServer.RuntimeConnectionString(x.SchemaConnectionString)));

        await app.StartAsync(CancellationToken);
        await WaitUntilAsync(async () => await RunsAsync(a) is { Count: 1 }, "The directory tenant did not run.");
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);

        Assert.Empty(await RunsAsync(x));
        Assert.Equal(1L, await AdminCountAsync(x.SchemaConnectionString, "axis.process_work_items"));
        Assert.Contains(
            logs.Entries,
            entry => entry.StartsWith("Warning:", StringComparison.Ordinal) && entry.Contains("Tenants section is ignored", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tenant_suspended_at_a_refresh_gets_no_new_work_claimed_while_another_tenant_runs()
    {
        var a = await PrepareTenantAsync("a");
        var b = await PrepareTenantAsync("b");
        var platform = await database.CreateDatabaseAsync("worker_platform", CancellationToken);
        await SeedPlatformAsync(platform, a, b);
        await GrantPlatformAsync(platform);
        var logs = new LogCollector();
        await using var app = BuildWorker(platform, logs, ("Tenancy:RefreshInterval", "00:00:00.2"));
        await app.StartAsync(CancellationToken);
        await WaitUntilAsync(() => Task.FromResult(logs.Entries.Contains("Information: Loaded 2 active tenant(s) from the tenant directory.")), "The first read did not work.");

        await SetStatusAsync(platform, "a", "suspended");
        await WaitForRefreshesAsync(logs, RefreshCount(logs) + 2);
        await EnqueueAsync(a);
        var itemB = await EnqueueAsync(b);
        await WaitUntilAsync(async () => await RunsAsync(b) is { Count: 1 }, "The work of the active tenant did not run.");
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);

        Assert.Equal(itemB, (await RunsAsync(b)).Single().ItemId);
        Assert.Empty(await RunsAsync(a));
        Assert.Equal(1L, await AdminCountAsync(a.SchemaConnectionString, "axis.process_work_items WHERE claim_token IS NULL"));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tenant_added_while_the_worker_runs_has_its_due_work_run_after_a_refresh()
    {
        var a = await PrepareTenantAsync("a");
        var b = await PrepareTenantAsync("b");
        var itemB = await EnqueueAsync(b);
        var platform = await database.CreateDatabaseAsync("worker_platform", CancellationToken);
        await SeedPlatformAsync(platform, a);
        await GrantPlatformAsync(platform);
        var logs = new LogCollector();
        await using var app = BuildWorker(platform, logs, ("Tenancy:RefreshInterval", "00:00:00.2"));
        await app.StartAsync(CancellationToken);
        await WaitUntilAsync(() => Task.FromResult(logs.Entries.Contains("Information: Loaded 1 active tenant(s) from the tenant directory.")), "The first read did not work.");
        Assert.Empty(await RunsAsync(b));

        await SeedPlatformAsync(platform, b);

        await WaitUntilAsync(async () => await RunsAsync(b) is { Count: 1 }, "The work of the new tenant did not run.");
        Assert.Equal([new Run(itemB, "b", b.DatabaseName, b.Role.Name)], await RunsAsync(b));
        Assert.Contains(logs.Entries, entry => entry.Contains("Tenant b is ready for work.", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Entries, entry => entry.StartsWith("Error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_refresh_makes_the_worker_Degraded_and_a_working_one_makes_it_Healthy_again()
    {
        var a = await PrepareTenantAsync("a");
        var platform = await database.CreateDatabaseAsync("worker_platform", CancellationToken);
        var platformName = new NpgsqlConnectionStringBuilder(platform).Database!;
        await SeedPlatformAsync(platform, a);
        await GrantPlatformAsync(platform);
        var logs = new LogCollector();
        await using var app = BuildWorker(platform, logs, ("Tenancy:RefreshInterval", "00:00:00.2"));
        await app.StartAsync(CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        await WaitUntilAsync(async () => await GetAsync(client, "/health/ready") == (HttpStatusCode.OK, "Healthy"), "The worker did not become ready.");

        // Another database holds the admin session, because nobody can connect to the platform database once it refuses.
        await PostgreSqlServer.AdminScalarAsync(a.SchemaConnectionString, $"""ALTER DATABASE "{platformName}" ALLOW_CONNECTIONS false""", CancellationToken);
        await PostgreSqlServer.AdminScalarAsync(
            a.SchemaConnectionString,
            $"SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity WHERE datname = '{platformName}'",
            CancellationToken);

        await WaitUntilAsync(async () => await GetAsync(client, "/health/ready") == (HttpStatusCode.OK, "Degraded"), "The worker did not become Degraded.");
        Assert.Contains(logs.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal) && entry.Contains(RefreshFailedLog, StringComparison.Ordinal));
        Assert.Equal((HttpStatusCode.OK, "Healthy"), await GetAsync(client, "/health/live"));

        // The last good read stays in use.
        var itemA = await EnqueueAsync(a);
        await WaitUntilAsync(async () => await RunsAsync(a) is { Count: 1 }, "The work did not run during the outage.");
        Assert.Equal(itemA, (await RunsAsync(a)).Single().ItemId);

        await PostgreSqlServer.AdminScalarAsync(a.SchemaConnectionString, $"""ALTER DATABASE "{platformName}" ALLOW_CONNECTIONS true""", CancellationToken);

        await WaitUntilAsync(async () => await GetAsync(client, "/health/ready") == (HttpStatusCode.OK, "Healthy"), "The worker did not become Healthy again.");
    }

    [Fact]
    public async Task An_invalid_refresh_interval_stops_the_start_and_names_the_setting()
    {
        var platform = await database.CreateDatabaseAsync("worker_platform", CancellationToken);

        var exception = Assert.Throws<InvalidOperationException>(() => BuildWorker(platform, new LogCollector(), ("Tenancy:RefreshInterval", "0")));

        Assert.Contains("Tenancy:RefreshInterval", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tenant_on_another_schema_version_runs_no_work_and_does_not_block_readiness_until_migrate_has_run()
    {
        var a = (await PrepareTenantAsync("a")) with { SchemaVersion = "20000101000000_Old" };
        var item = await EnqueueAsync(a);
        var platform = await database.CreateDatabaseAsync("worker_platform", CancellationToken);
        await SeedPlatformAsync(platform, a);
        await GrantPlatformAsync(platform);
        var logs = new LogCollector();
        await using var app = BuildWorker(platform, logs);
        await app.StartAsync(CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        await WaitUntilAsync(
            () => Task.FromResult(logs.Entries.Any(entry => entry.Contains("Tenant a waits: its schema version", StringComparison.Ordinal))),
            "The tenant did not wait.");
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);

        Assert.Empty(await RunsAsync(a));
        Assert.Equal((HttpStatusCode.OK, "Healthy"), await GetAsync(client, "/health/ready"));
        Assert.Single(logs.Entries, entry => entry.Contains("Tenant a waits", StringComparison.Ordinal));

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:PlatformSchema"] = platform,
            ["Platform:SecretKey"] = TestSecretKey,
        };
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await AdminCommands.RunAsync(["migrate"], new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), output, error, CancellationToken);
        Assert.True(exit == 0, $"{output}{error}");

        await WaitUntilAsync(async () => await RunsAsync(a) is { Count: 1 }, "The work did not run after migrate.");
        Assert.Equal(item, (await RunsAsync(a)).Single().ItemId);
        Assert.Equal((HttpStatusCode.OK, "Healthy"), await GetAsync(client, "/health/ready"));
    }

    private static WebApplication BuildWorker(string platform, LogCollector logs, params (string Key, string Value)[] extraSettings)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        var settings = new Dictionary<string, string?>
        {
            ["Tenancy:Source"] = "directory",
            ["ConnectionStrings:Platform"] = PostgreSqlServer.RuntimeConnectionString(platform),
            ["Platform:SecretKey"] = TestSecretKey,
            ["Health:Urls"] = "http://127.0.0.1:0",
            ["Worker:PollInterval"] = "00:00:00.05",
        };
        foreach (var (key, value) in extraSettings)
        {
            settings[key] = value;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton<IWorkItemHandler, RecordingHandler>();
        var app = builder.Build();
        app.MapWorkerHealth();
        return app;
    }

    private static Role NewRole() =>
        new($"wdir_{Guid.NewGuid():N}"[..16], Convert.ToHexString(RandomNumberGenerator.GetBytes(12)));

    /// <summary>
    /// Creates a tenant database with the processes migrations and the test table, and a login role with a random
    /// password that has the runtime grants.
    /// </summary>
    private async Task<DirectoryTenant> PrepareTenantAsync(string id)
    {
        var schemaConnection = await database.CreateDatabaseAsync($"worker_dir_{id}", CancellationToken);
        await using (var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(schemaConnection).Options))
        {
            await context.Database.MigrateAsync(CancellationToken);
        }

        var role = NewRole();
        await PostgreSqlServer.AdminScalarAsync(schemaConnection, $"CREATE ROLE {role.Name} LOGIN PASSWORD '{role.Password}'", CancellationToken);
        await using (var connection = new NpgsqlConnection(schemaConnection))
        {
            await connection.OpenAsync(CancellationToken);
            await RuntimeRoleGrants.ApplyAsync(connection, role.Name, CancellationToken);
            await using var command = new NpgsqlCommand(
                $"CREATE TABLE test_runs (item_id uuid, tenant_id text, db text, role text); GRANT SELECT, INSERT ON test_runs TO {role.Name}",
                connection);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        var builder = new NpgsqlConnectionStringBuilder(schemaConnection);
        return new DirectoryTenant(id, builder.Host!, builder.Port, builder.Database!, role, schemaConnection);
    }

    /// <summary>Creates the directory tables and one active tenant row per tenant. The reader role cannot read them yet.</summary>
    private static async Task SeedPlatformAsync(string platform, params DirectoryTenant[] tenants)
    {
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        await context.Database.MigrateAsync(CancellationToken);
        foreach (var tenant in tenants)
        {
            context.Tenants.Add(new TenantRow
            {
                Id = tenant.Id,
                Status = "active",
                SchemaVersion = tenant.SchemaVersion ?? TenantSchemaVersion.Current,
                DatabaseHost = tenant.DatabaseHost,
                DatabasePort = tenant.DatabasePort,
                DatabaseName = tenant.DatabaseName,
                RuntimeUser = tenant.Role.Name,
                SchemaUser = PostgreSqlServer.SchemaRole,
                RuntimePassword = _cipher.Encrypt(tenant.Id, TenantSecretFields.RuntimePassword, tenant.Role.Password),
                SchemaPassword = _cipher.Encrypt(tenant.Id, TenantSecretFields.SchemaPassword, PostgreSqlServer.RolePassword),
                OidcAuthority = "https://127.0.0.1:1/realms/axis",
                OidcClientId = "axis",
                OidcClientSecret = _cipher.Encrypt(tenant.Id, TenantSecretFields.OidcClientSecret, "test"),
            });
            context.TenantHosts.Add(new TenantHostRow { TenantId = tenant.Id, Host = $"{tenant.Id}.example.test" });
        }

        await context.SaveChangesAsync(CancellationToken);
    }

    private static async Task SetStatusAsync(string platform, string id, string status)
    {
        await using var context = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(platform).Options);
        var row = await context.Tenants.SingleAsync(tenant => tenant.Id == id, CancellationToken);
        row.Status = status;
        await context.SaveChangesAsync(CancellationToken);
    }

    private static int RefreshCount(LogCollector logs) =>
        logs.Entries.Count(entry => entry.Contains(RefreshedLog, StringComparison.Ordinal));

    private static Task WaitForRefreshesAsync(LogCollector logs, int count) =>
        WaitUntilAsync(() => Task.FromResult(RefreshCount(logs) >= count), "The worker did not refresh the tenant directory.");

    /// <summary>Lets the runtime role read the platform schema, in one step, so a read sees all the rows or none.</summary>
    private static async Task GrantPlatformAsync(string platform)
    {
        await using var connection = new NpgsqlConnection(platform);
        await connection.OpenAsync(CancellationToken);
        await PlatformRoleGrants.ApplyAsync(connection, PostgreSqlServer.RuntimeRole, CancellationToken);
    }

    private static async Task<Guid> EnqueueAsync(DirectoryTenant tenant)
    {
        await using var connection = new NpgsqlConnection(tenant.SchemaConnectionString);
        await connection.OpenAsync(CancellationToken);

        // Due a minute ago, so a small clock difference with the database does not delay the claim.
        return await WorkItemQueue.EnqueueAsync(connection, null, tenant.Id, RecordKind, DateTimeOffset.UtcNow.AddMinutes(-1), CancellationToken);
    }

    private static async Task<List<Run>> RunsAsync(DirectoryTenant tenant)
    {
        await using var connection = new NpgsqlConnection(tenant.SchemaConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT item_id, tenant_id, db, role FROM test_runs ORDER BY item_id", connection);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var runs = new List<Run>();
        while (await reader.ReadAsync(CancellationToken))
        {
            runs.Add(new Run(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return runs;
    }

    private static async Task<long> AdminCountAsync(string connectionString, string table) =>
        (long)(await PostgreSqlServer.AdminScalarAsync(connectionString, $"SELECT count(*) FROM {table}", CancellationToken))!;

    private static async Task<(HttpStatusCode Status, string? Health)> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        return (response.StatusCode, body.RootElement.GetProperty("status").GetString());
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string failure)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken);
        }
    }

    private sealed record Role(string Name, string Password);

    private sealed record DirectoryTenant(string Id, string DatabaseHost, int DatabasePort, string DatabaseName, Role Role, string SchemaConnectionString)
    {
        /// <summary>The schema version the directory records for the tenant. Null means the worker's own.</summary>
        public string? SchemaVersion { get; init; }
    }

    private sealed record Run(Guid ItemId, string TenantId, string DatabaseName, string RoleName);

    /// <summary>Records each run in <c>test_runs</c> with the current tenant, database and role, in the work transaction.</summary>
    private sealed class RecordingHandler(ITenantContextAccessor accessor) : IWorkItemHandler
    {
        public string Kind => RecordKind;

        public async Task HandleAsync(WorkItemContext context, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(
                "INSERT INTO test_runs (item_id, tenant_id, db, role) VALUES (@item, @tenant, current_database(), current_user)",
                context.Connection,
                context.Transaction);
            command.Parameters.AddWithValue("item", context.Item.Id);
            command.Parameters.AddWithValue("tenant", accessor.Current!.TenantId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public Task OnFailedAsync(WorkItemContext context, Exception exception, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
