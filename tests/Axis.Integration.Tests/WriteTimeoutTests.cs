using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Http;
using Axis.Server.Tests;
using Axis.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class WriteTimeoutTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Title = "The write took too long. Try again.";
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string SecretName = "Secret desk";

    private static readonly TimeSpan _requestLimit = TimeSpan.FromSeconds(10);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_slow_create_update_and_delete_answer_503_write_nothing_and_are_logged_and_counted()
    {
        await fixture.ResetAsync();
        var id = await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = "Desk" });
        var rows = (await fixture.TableShapeAsync(TenantA, "Item")).RowCount;
        var audits = await fixture.AuditRecordCountAsync(TenantA);
        using var metrics = new MetricCollector();
        await using var host = CreateHost();
        using var client = host.CreateClient().SignedInAs("requester");

        await using (await LockAsync(ItemTable()))
        {
            foreach (var request in new[]
            {
                Request(HttpMethod.Post, Items, HostA, $$"""{ "values": { "name": "{{SecretName}}" } }"""),
                Request(HttpMethod.Patch, $"{Items}/{id}", HostA, $$"""{ "version": 1, "values": { "name": "{{SecretName}}" } }"""),
                Request(HttpMethod.Delete, $"{Items}/{id}", HostA),
            })
            {
                using (request)
                {
                    using var response = await SendAsync(client, request);

                    using var problem = await ReadProblemAsync(response, HttpStatusCode.ServiceUnavailable);
                    Assert.Equal(Title, problem.RootElement.GetProperty("title").GetString());
                }
            }
        }

        Assert.Equal(rows, (await fixture.TableShapeAsync(TenantA, "Item")).RowCount);
        Assert.Equal(audits, await fixture.AuditRecordCountAsync(TenantA));
        using var stored = await GetJsonAsync($"{Items}/{id}");
        Assert.Equal("\"Desk\"", stored.RootElement.GetProperty("values").GetProperty("name").GetRawText());
        Assert.Equal(1, stored.RootElement.GetProperty("version").GetInt64());
        Assert.Contains(fixture.Logs.Entries, IsTimeoutWarning);
        Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains(SecretName, StringComparison.Ordinal));
        Assert.True(metrics.HasPoint("axis.queries.timeouts", ("axis.tenant", TenantA), ("axis.application", "RecordsApp")));
    }

    [Fact]
    public async Task The_next_user_of_the_pooled_connection_of_a_timed_out_write_has_no_limit()
    {
        await fixture.ResetAsync();
        await using var host = CreateHost();
        using var client = host.CreateClient().SignedInAs("requester");

        await using (await LockAsync(ItemTable()))
        {
            using var request = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Desk" } }""");
            using var response = await SendAsync(client, request);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        // The pool holds one connection, which No Reset On Close would hand on with a plain SET still in force.
        await using var connection = await host.Services.GetRequiredService<TenantConnectionFactory>().OpenConnectionAsync(TenantA, CancellationToken);
        await using var command = new NpgsqlCommand("SHOW statement_timeout", connection);
        Assert.Equal("0", (string?)await command.ExecuteScalarAsync(CancellationToken));
    }

    [Fact]
    public void Every_write_route_in_the_endpoint_map_runs_under_the_write_timeout()
    {
        var routes = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => (Key: $"{method} {endpoint.RoutePattern.RawText?.TrimEnd('/')}", Endpoint: endpoint)))
            .Where(route => _writes.Contains(route.Key))
            .ToList();

        Assert.Equal(_writes.Order(StringComparer.Ordinal), routes.Select(route => route.Key).Order(StringComparer.Ordinal));
        foreach (var (key, endpoint) in routes)
        {
            Assert.True(
                endpoint.Metadata.GetOrderedMetadata<QueryTimeoutMetadata>().Any(metadata => metadata.Setting == "Data:WriteTimeout"),
                $"{key} has no write timeout.");
        }
    }

    private static readonly HashSet<string> _writes =
    [
        "POST /api/apps/{app}/entities/{entity}/records",
        "POST /api/apps/{app}/entities/{module}/{entity}/records",
        "PATCH /api/apps/{app}/entities/{entity}/records/{id}",
        "PATCH /api/apps/{app}/entities/{module}/{entity}/records/{id}",
        "DELETE /api/apps/{app}/entities/{entity}/records/{id}",
        "DELETE /api/apps/{app}/entities/{module}/{entity}/records/{id}",
        "POST /api/apps/{app}/processes/{process}/instances",
        "POST /api/apps/{app}/processes/{module}/{process}/instances",
        "POST /api/apps/{app}/tasks/{id}/complete",
        "POST /api/apps/{app}/endpoints/{name}",
    ];

    private static bool IsTimeoutWarning(string entry) =>
        entry.StartsWith("Warning: A write of ", StringComparison.Ordinal)
        && entry.Contains("A write of Item ran past the write timeout after ", StringComparison.Ordinal)
        && entry.EndsWith(" ms.", StringComparison.Ordinal);

    // One connection in the pool, so a limit that stayed on it would reach the next request. The limit is far below a second.
    private WebApplicationFactory<Program> CreateHost() =>
        fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Data:WriteTimeout", "00:00:00.200");
            builder.UseSetting(
                "Tenants:a:ConnectionString",
                PostgreSqlServer.RuntimeConnectionString(fixture.ConnectionString(TenantA)) + ";Maximum Pool Size=1;No Reset On Close=true");
        });

    // A request that no timeout stops fails here instead of hanging the test.
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        limit.CancelAfter(_requestLimit);
        return await client.SendAsync(request, limit.Token);
    }

    private async Task<JsonDocument> GetJsonAsync(string path)
    {
        using var request = Request(path, HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private string ItemTable()
    {
        Assert.True(fixture.Model.TryGetEntity("Item", out var item));
        return EntityNaming.QualifiedTable(EntityNaming.Table(item.Id));
    }

    // Holds an EXCLUSIVE lock in an open transaction, so every write to the table waits until it is disposed.
    // Plain reads still run.
    private async Task<TableLock> LockAsync(string table)
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        var transaction = await connection.BeginTransactionAsync(CancellationToken);
        await using var command = new NpgsqlCommand($"LOCK TABLE {table} IN EXCLUSIVE MODE", connection, transaction);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return new TableLock(connection, transaction);
    }

    private sealed class TableLock(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
