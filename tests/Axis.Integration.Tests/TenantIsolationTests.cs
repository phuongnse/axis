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
            builder.UseSetting("ConnectionStrings:Platform", database.ConnectionString);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", database.ConnectionString);
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

        // A test host, not Program: the row endpoints exist only here.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = tenantA,
            ["Tenants:b:Hosts:0"] = "b.example.test",
            ["Tenants:b:ConnectionString"] = tenantB,
            ["Tenants:a:Bindings:PurchasingSystemUrl:Kind"] = "url",
            ["Tenants:a:Bindings:PurchasingSystemUrl:Value"] = "https://a.example.test/api",
            ["Tenants:b:Bindings:PurchasingSystemUrl:Kind"] = "url",
            ["Tenants:b:Bindings:PurchasingSystemUrl:Value"] = "https://b.example.test/api",
        });
        builder.Services.AddTenancy(builder.Configuration, builder.Environment);
        _app = builder.Build();
        _app.UseTenantResolution();
        _app.MapPost("/rows", async (string value, ITenantConnectionFactory connections, CancellationToken cancellationToken) =>
        {
            await using var connection = await connections.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "CREATE TABLE IF NOT EXISTS rows (value text NOT NULL); INSERT INTO rows (value) VALUES (@value)",
                connection);
            command.Parameters.AddWithValue("value", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return Results.NoContent();
        });
        _app.MapGet("/rows", async (ITenantConnectionFactory connections, CancellationToken cancellationToken) =>
        {
            await using var connection = await connections.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "CREATE TABLE IF NOT EXISTS rows (value text NOT NULL); SELECT value FROM rows ORDER BY value",
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
}
