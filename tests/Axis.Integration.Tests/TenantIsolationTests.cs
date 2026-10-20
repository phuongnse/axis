using System.Net;
using System.Net.Http.Json;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Axis.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class TenantIsolationTests(PostgreSqlFixture database) : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;
    private RecordingTenantCatalog? _catalog;
    private string? _tenantBDatabase;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private HttpClient Client => _client ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task Row_written_for_tenant_a_is_read_only_through_tenant_a()
    {
        await PostAsync("A.Example.TEST:8443", "written-for-a");

        Assert.Contains("written-for-a", await ListAsync("a.example.test"));
        Assert.DoesNotContain("written-for-a", await ListAsync("b.example.test"));
    }

    [Fact]
    public async Task Row_written_for_tenant_b_is_read_only_through_tenant_b()
    {
        await PostAsync("B.EXAMPLE.test:8443", "written-for-b");

        Assert.Contains("written-for-b", await ListAsync("b.example.test"));
        Assert.DoesNotContain("written-for-b", await ListAsync("a.example.test"));
    }

    [Fact]
    public async Task A_request_on_tenant_a_asks_the_catalog_only_for_tenant_a_and_never_opens_tenant_b()
    {
        await PostAsync("a.example.test", "written-for-a");
        Assert.Contains("written-for-a", await ListAsync("a.example.test"));

        Assert.NotEmpty(_catalog!.FoundIds);
        Assert.All(_catalog.FoundIds, id => Assert.Equal("a", id));
        await using var dataSource = NpgsqlDataSource.Create(_tenantBDatabase!);
        await using var count = dataSource.CreateCommand("SELECT count(*) FROM rows");
        Assert.Equal(0L, await count.ExecuteScalarAsync(CancellationToken));
    }

    [Fact]
    public async Task Each_tenant_resolves_its_own_value_of_the_same_binding()
    {
        Assert.Equal("https://a.example.test/api", await GetStringAsync("/bindings/PurchasingSystemUrl", "a.example.test"));
        Assert.Equal("https://b.example.test/api", await GetStringAsync("/bindings/PurchasingSystemUrl", "b.example.test"));
    }

    [Fact]
    public async Task Ready_returns_200_for_an_unknown_host()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(database.ConnectionString);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", PostgreSqlServer.RuntimeConnectionString(database.ConnectionString));
            builder.UseSetting("Tenants:default:SchemaConnectionString", database.ConnectionString);
            builder.UseTestOidc("default");
        });
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Get, "/health/ready", "unknown.example");

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async ValueTask InitializeAsync()
    {
        var tenantA = await database.CreateDatabaseAsync("tenant_a", CancellationToken);
        var tenantB = await database.CreateDatabaseAsync("tenant_b", CancellationToken);
        _tenantBDatabase = tenantB;

        // The runtime role cannot create tables, so the schema role creates the table the handlers use
        // and grants the runtime role row access to it.
        foreach (var tenant in new[] { tenantA, tenantB })
        {
            await using var dataSource = NpgsqlDataSource.Create(tenant);
            await using var create = dataSource.CreateCommand(
                $"CREATE TABLE rows (value text NOT NULL); GRANT SELECT, INSERT, UPDATE, DELETE ON rows TO {PostgreSqlServer.RuntimeRole}");
            await create.ExecuteNonQueryAsync(CancellationToken);
        }

        // A test host, not Program: the row endpoints exist only here.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(tenantA),
            ["Tenants:b:Hosts:0"] = "b.example.test",
            ["Tenants:b:ConnectionString"] = PostgreSqlServer.RuntimeConnectionString(tenantB),
            ["Tenants:a:Bindings:PurchasingSystemUrl:Kind"] = "url",
            ["Tenants:a:Bindings:PurchasingSystemUrl:Value"] = "https://a.example.test/api",
            ["Tenants:b:Bindings:PurchasingSystemUrl:Kind"] = "url",
            ["Tenants:b:Bindings:PurchasingSystemUrl:Value"] = "https://b.example.test/api",
        });

        // The host reads tenants through the catalog contract only, and the catalog records which tenants it is asked for.
        _catalog = new RecordingTenantCatalog(ConfigurationTenantCatalog.Load(builder.Configuration, builder.Environment));
        builder.Services.AddTenancy(_catalog);
        _app = builder.Build();
        _app.UseTenantResolution();
        _app.MapPost("/rows", async (string value, ITenantConnectionFactory connections, CancellationToken cancellationToken) =>
        {
            await using var connection = await connections.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "INSERT INTO rows (value) VALUES (@value)",
                connection);
            command.Parameters.AddWithValue("value", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return Results.NoContent();
        });
        _app.MapGet("/rows", async (ITenantConnectionFactory connections, CancellationToken cancellationToken) =>
        {
            await using var connection = await connections.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT value FROM rows ORDER BY value",
                connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var values = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                values.Add(reader.GetString(0));
            }

            return values;
        });
        _app.MapGet("/bindings/{name}", (string name, IDeploymentBindings bindings) => bindings.Resolve(name).Value);
        await _app.StartAsync(CancellationToken);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private async Task PostAsync(string host, string value)
    {
        using var request = Request(HttpMethod.Post, $"/rows?value={Uri.EscapeDataString(value)}", host);
        using var response = await Client.SendAsync(request, CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<List<string>> ListAsync(string host)
    {
        using var request = Request(HttpMethod.Get, "/rows", host);
        using var response = await Client.SendAsync(request, CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<string>>(CancellationToken) ?? [];
    }

    private async Task<string> GetStringAsync(string path, string host)
    {
        using var request = Request(HttpMethod.Get, path, host);
        using var response = await Client.SendAsync(request, CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(CancellationToken);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string host)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        return request;
    }

    private sealed class RecordingTenantCatalog(ITenantCatalog inner) : ITenantCatalog
    {
        private readonly List<string> _foundIds = [];

        public IReadOnlyList<TenantEntry> Tenants => inner.Tenants;

        public IReadOnlyList<string> FoundIds
        {
            get
            {
                lock (_foundIds)
                {
                    return [.. _foundIds];
                }
            }
        }

        public TenantEntry? Find(string tenantId)
        {
            lock (_foundIds)
            {
                _foundIds.Add(tenantId);
            }

            return inner.Find(tenantId);
        }

        public TenantEntry? FindByHost(string host) => inner.FindByHost(host);

        public bool IsUnavailableHost(string host) => inner.IsUnavailableHost(host);
    }
}
