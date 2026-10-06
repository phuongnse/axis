using System.Net;
using System.Net.Http.Json;
using Axis.Server.Tenancy;
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
    public async Task Ready_returns_200_for_an_unknown_host()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", database.ConnectionString);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", database.ConnectionString);
        });
        using var client = factory.CreateClient();
        using var request = Request(HttpMethod.Get, "/health/ready", "unknown.example");

        using var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async ValueTask InitializeAsync()
    {
        var tenantA = await CreateDatabaseAsync("tenant_a");
        var tenantB = await CreateDatabaseAsync("tenant_b");

        // A test host, not Program: the row endpoints exist only here.
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tenants:a:Hosts:0"] = "a.example.test",
            ["Tenants:a:ConnectionString"] = tenantA,
            ["Tenants:b:Hosts:0"] = "b.example.test",
            ["Tenants:b:ConnectionString"] = tenantB,
        });
        builder.Services.AddTenancy(builder.Configuration);
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

    private async Task<string> CreateDatabaseAsync(string name)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        // CREATE DATABASE cannot run in a transaction block, so each statement is its own command.
        foreach (var sql in new[] { $"""DROP DATABASE IF EXISTS "{name}" WITH (FORCE)""", $"""CREATE DATABASE "{name}" """ })
        {
            await using var command = dataSource.CreateCommand(sql);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(database.ConnectionString) { Database = name }.ConnectionString;
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

    private static HttpRequestMessage Request(HttpMethod method, string path, string host)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Host = host;
        return request;
    }
}
