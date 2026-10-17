using System.Security.Cryptography;
using Axis.Configuration.Tests;
using Axis.Data.Storage;
using Axis.Server.Tests;
using Axis.Worker;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class RuntimeRoleCheckTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private const string TenantHost = "a.example.test";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_stops_when_the_runtime_database_role_is_a_superuser()
    {
        using var folder = ApplicationFolder();
        var (schemaConnection, role) = await PrepareAsync(folder, "ALTER ROLE {0} SUPERUSER");

        var serverLogs = new LogCollector();
        await using var server = CreateServer(schemaConnection, role, folder.Path, serverLogs);
        var exception = Assert.ThrowsAny<Exception>(() => server.CreateClient());
        var workerLogs = await RunWorkerUntilItStopsAsync(schemaConnection, role);

        AssertNames(exception.ToString(), "tenant 'a'", "superuser");
        Assert.Contains(workerLogs.Entries, entry => entry.Contains("tenant 'a'", StringComparison.Ordinal) && entry.Contains("superuser", StringComparison.Ordinal));
        AssertNoSecrets(role, exception.ToString(), serverLogs, workerLogs);
    }

    [Fact]
    public async Task Start_stops_when_the_runtime_database_role_owns_the_entities_schema()
    {
        using var folder = ApplicationFolder();
        var (schemaConnection, role) = await PrepareAsync(folder, "ALTER SCHEMA entities OWNER TO {0}");

        // The server starts without a folder: activation's grant step cannot run once the schema has another owner.
        var serverLogs = new LogCollector();
        await using var server = CreateServer(schemaConnection, role, folder: null, serverLogs);
        var exception = Assert.ThrowsAny<Exception>(() => server.CreateClient());
        var workerLogs = await RunWorkerUntilItStopsAsync(schemaConnection, role);

        AssertNames(exception.ToString(), "tenant 'a'", "'entities' schema");
        Assert.Contains(workerLogs.Entries, entry => entry.Contains("'entities' schema", StringComparison.Ordinal));
        AssertNoSecrets(role, exception.ToString(), serverLogs, workerLogs);
    }

    [Fact]
    public async Task Start_stops_when_the_runtime_database_role_may_create_objects_in_the_axis_schema()
    {
        using var folder = ApplicationFolder();
        var (schemaConnection, role) = await PrepareAsync(folder, "GRANT CREATE ON SCHEMA axis TO {0}");

        // With a folder, the failure comes from activation's check, which runs after its grant step.
        var serverLogs = new LogCollector();
        await using var server = CreateServer(schemaConnection, role, folder.Path, serverLogs);
        var exception = Assert.ThrowsAny<Exception>(() => server.CreateClient());
        var workerLogs = await RunWorkerUntilItStopsAsync(schemaConnection, role);

        AssertNames(exception.ToString(), "tenant 'a'", "create objects in the 'axis' schema");
        Assert.Contains(workerLogs.Entries, entry => entry.Contains("create objects in the 'axis' schema", StringComparison.Ordinal));
        AssertNoSecrets(role, exception.ToString(), serverLogs, workerLogs);
    }

    [Fact]
    public async Task Both_hosts_start_with_the_runtime_database_role_the_fixtures_create()
    {
        using var folder = ApplicationFolder();
        var schemaConnection = await database.CreateDatabaseAsync("rolecheck", CancellationToken);
        var role = new Role(PostgreSqlServer.RuntimeRole, PostgreSqlServer.RolePassword);

        var serverLogs = new LogCollector();
        await using (var server = CreateServer(schemaConnection, role, folder.Path, serverLogs))
        {
            server.CreateClient().Dispose();
        }

        var workerLogs = new LogCollector();
        using var worker = BuildWorker(schemaConnection, role, workerLogs);
        await worker.StartAsync(CancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!workerLogs.Entries.Any(entry => entry.Contains("Tenant a is ready for work.", StringComparison.Ordinal)) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50, CancellationToken);
        }

        await worker.StopAsync(CancellationToken);
        Assert.Contains(workerLogs.Entries, entry => entry.Contains("Tenant a is ready for work.", StringComparison.Ordinal));
    }

    private static void AssertNames(string text, params string[] parts)
    {
        foreach (var part in parts)
        {
            Assert.Contains(part, text, StringComparison.Ordinal);
        }
    }

    private static void AssertNoSecrets(Role role, string exception, params LogCollector[] logs)
    {
        var texts = logs.SelectMany(log => log.Entries).Append(exception).ToList();
        Assert.NotEmpty(texts);
        Assert.All(texts, text =>
        {
            Assert.DoesNotContain(role.Password, text, StringComparison.Ordinal);
            Assert.DoesNotContain("Host=", text, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Creates a database, starts a good server once so the schemas exist and are migrated, then creates a
    /// login role with the normal runtime grants and applies <paramref name="badRight"/> to it as the
    /// container's superuser. <c>{0}</c> in the statement is the role.
    /// </summary>
    private async Task<(string SchemaConnection, Role Role)> PrepareAsync(TemporaryFolder folder, string badRight)
    {
        var schemaConnection = await database.CreateDatabaseAsync("rolecheck", CancellationToken);
        await using (var server = CreateServer(schemaConnection, new Role(PostgreSqlServer.RuntimeRole, PostgreSqlServer.RolePassword), folder.Path, new LogCollector()))
        {
            server.CreateClient().Dispose();
        }

        var role = new Role($"bad_{Guid.NewGuid():N}"[..12], Convert.ToHexString(RandomNumberGenerator.GetBytes(12)));
        await PostgreSqlServer.AdminScalarAsync(schemaConnection, $"CREATE ROLE {role.Name} LOGIN PASSWORD '{role.Password}'", CancellationToken);
        await using (var connection = new NpgsqlConnection(schemaConnection))
        {
            await connection.OpenAsync(CancellationToken);
            await RuntimeRoleGrants.ApplyAsync(connection, role.Name, CancellationToken);
        }

        await PostgreSqlServer.AdminScalarAsync(schemaConnection, string.Format(badRight, role.Name), CancellationToken);
        return (schemaConnection, role);
    }

    /// <summary>Runs the worker host until it stops on its own. It fails when that takes more than 30 seconds.</summary>
    private static async Task<LogCollector> RunWorkerUntilItStopsAsync(string schemaConnection, Role role)
    {
        var logs = new LogCollector();
        using var worker = BuildWorker(schemaConnection, role, logs);
        var stopping = new TaskCompletionSource();
        using var registration = worker.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopping.TrySetResult());

        await worker.StartAsync(CancellationToken);
        Assert.Same(stopping.Task, await Task.WhenAny(stopping.Task, Task.Delay(TimeSpan.FromSeconds(30), CancellationToken)));
        await worker.StopAsync(CancellationToken);
        return logs;
    }

    private static TemporaryFolder ApplicationFolder() =>
        new TemporaryFolder().With(
            "application.json",
            $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "application", "name": "Role{{Guid.NewGuid():N}}", "formatVersion": 1 }""");

    private static string RuntimeConnectionString(string schemaConnection, Role role) =>
        new NpgsqlConnectionStringBuilder(schemaConnection) { Username = role.Name, Password = role.Password }.ConnectionString;

    private static WebApplicationFactory<Program> CreateServer(string schemaConnection, Role role, string? folder, LogCollector logs) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", RuntimeConnectionString(schemaConnection, role));
            builder.UseSetting("Tenants:a:Hosts:0", TenantHost);
            builder.UseSetting("Tenants:a:ConnectionString", RuntimeConnectionString(schemaConnection, role));
            builder.UseSetting("Tenants:a:SchemaConnectionString", schemaConnection);
            builder.UseTestOidc("a");
            if (folder is not null)
            {
                builder.UseSetting("ActivateOnStartup:0", folder);
            }

            builder.ConfigureLogging(logging => logging.AddProvider(logs));
        });

    private static IHost BuildWorker(string schemaConnection, Role role, LogCollector logs)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:a:Hosts:0"] = TenantHost,
            ["Tenants:a:ConnectionString"] = RuntimeConnectionString(schemaConnection, role),
            ["Worker:PollInterval"] = "00:00:00.05",
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
        return builder.Build();
    }

    private sealed record Role(string Name, string Password);
}
