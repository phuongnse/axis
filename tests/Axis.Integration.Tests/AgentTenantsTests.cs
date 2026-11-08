using System.Net;
using System.Text.Json;
using Axis.Agent;
using Axis.Hosting;
using Axis.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Axis.Integration.Tests.Polling;

namespace Axis.Integration.Tests;

/// <summary>With <c>Ai:DefaultBinding</c> set, the agent host reads its tenants as the worker does and reads each tenant database as the runtime role.</summary>
public sealed class AgentTenantsTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_directory_tenant_is_read_as_the_runtime_role_and_only_its_requests_that_need_work_are_listed()
    {
        var platform = await CellTestPlatform.CreateAsync(database, new Uri("https://127.0.0.1:1/realms/axis"), CancellationToken);
        await using var app = BuildAgent(
            aiOn: true,
            new Dictionary<string, string?>
            {
                ["Tenancy:Source"] = "directory",
                ["ConnectionStrings:Platform"] = PostgreSqlServer.RuntimeConnectionString(platform),
                ["Platform:SecretKey"] = TestPlatform.SecretKey,
                ["Tenancy:RefreshInterval"] = "00:00:00.2",
            },
            new LogCollector());
        await app.StartAsync(CancellationToken);
        var catalog = app.Services.GetRequiredService<BackgroundTenantCatalog>();
        await WaitUntilAsync(() => catalog.Find("a") is not null, "The agent host did not read tenant a.");
        var connectionString = catalog.Find("a")!.ConnectionString.Reveal();
        var planning = Guid.NewGuid();
        await InsertRequestAsync(connectionString, planning, "planning");
        await InsertRequestAsync(connectionString, Guid.NewGuid(), "accepted");

        var listed = await app.Services.GetRequiredService<AuthoringRequestStore>().ListNeedingWorkAsync("a", CancellationToken);

        Assert.Equal([planning], listed.Select(request => request.Id));
        Assert.Equal(["planning"], listed.Select(request => request.State));
        await using var connection = await app.Services.GetRequiredService<TenantConnectionFactory>().OpenConnectionAsync("a", CancellationToken);
        await using var command = new NpgsqlCommand("SELECT current_user", connection);
        Assert.Equal(PostgreSqlServer.RuntimeRole, await command.ExecuteScalarAsync(CancellationToken));
    }

    [Fact]
    public async Task A_tenant_whose_role_owns_the_schema_is_refused_and_no_row_is_read()
    {
        var owner = await database.CreateDatabaseAsync("agent_tenants", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(owner, CancellationToken);
        var planning = Guid.NewGuid();
        await InsertRequestAsync(owner, planning, "planning");
        await using var app = BuildAgent(
            aiOn: true,
            new Dictionary<string, string?>
            {
                ["Tenants:a:Hosts:0"] = "a.example.test",
                ["Tenants:a:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(owner),
                ["Tenants:owner:Hosts:0"] = "owner.example.test",
                ["Tenants:owner:ConnectionString"] = owner,
            },
            new LogCollector());
        await app.StartAsync(CancellationToken);
        var store = app.Services.GetRequiredService<AuthoringRequestStore>();

        var listed = await store.ListNeedingWorkAsync("a", CancellationToken);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListNeedingWorkAsync("owner", CancellationToken));

        Assert.Equal([planning], listed.Select(request => request.Id));
        Assert.Contains("'owner'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{PostgreSqlServer.SchemaRole}'", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task With_ai_authoring_off_the_agent_host_starts_ready_and_reads_no_tenants()
    {
        var logs = new LogCollector();
        await using var app = BuildAgent(aiOn: false, new Dictionary<string, string?> { ["Tenancy:Source"] = "directory" }, logs);

        await app.StartAsync(CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var response = await client.GetAsync("/health/ready", CancellationToken);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        Assert.Equal((HttpStatusCode.OK, "Healthy"), (response.StatusCode, body.RootElement.GetProperty("status").GetString()));
        Assert.Null(app.Services.GetService<ITenantCatalog>());
        Assert.Null(app.Services.GetService<AuthoringRequestStore>());
        await WaitUntilAsync(
            () => logs.Entries.Any(entry => entry.Contains("AI authoring is off", StringComparison.Ordinal)),
            "The agent host did not say that AI authoring is off.");
    }

    private static WebApplication BuildAgent(bool aiOn, Dictionary<string, string?> settings, LogCollector logs)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        settings["Health:Urls"] = "http://127.0.0.1:0";
        if (aiOn)
        {
            settings["Ai:Bindings:main:Kind"] = "anthropic";
            settings["Ai:Bindings:main:Model"] = "model-1";
            settings["Ai:DefaultBinding"] = "main";
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisAgent(builder.Configuration, builder.Environment);
        var app = builder.Build();
        app.MapHostHealth();
        return app;
    }

    // The rows go in as the superuser of the container, which names the database through the connection string.
    private static async Task InsertRequestAsync(string connectionString, Guid id, string state) =>
        await PostgreSqlServer.AdminScalarAsync(
            connectionString,
            $"INSERT INTO axis.authoring_requests (id, application_id, author, text, state) VALUES ('{id}', '{Guid.NewGuid()}', 'author-1', 'Add a field.', '{state}')",
            CancellationToken);
}
