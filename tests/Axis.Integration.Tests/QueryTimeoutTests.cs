using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Server.Http;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class QueryTimeoutTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Title = "The query took too long. Try again or narrow it.";
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";
    private const string ItemNamesRows = "/api/apps/RecordsApp/data-sources/ItemNames/rows";
    private const string SecretName = "Secret desk";

    private static readonly TimeSpan _requestLimit = TimeSpan.FromSeconds(10);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_slow_read_answers_503_and_is_logged_and_counted()
    {
        await fixture.ResetAsync();
        var id = await InsertItemAsync(SecretName);
        using var metrics = new MetricCollector();
        await using var host = CreateHost();
        using var client = host.CreateClient().SignedInAs("requester");

        await using (await LockAsync(ItemTable()))
        {
            foreach (var path in new[] { Items, $"{Items}/{id}", $"{Items}/{id}/history", ItemNamesRows })
            {
                using var response = await GetAsync(client, path);

                using var problem = await ReadProblemAsync(response, HttpStatusCode.ServiceUnavailable);
                Assert.Equal(Title, problem.RootElement.GetProperty("title").GetString());
            }
        }

        Assert.Contains(fixture.Logs.Entries, entry => IsTimeoutWarning(entry, "Item"));
        Assert.Contains(fixture.Logs.Entries, entry => IsTimeoutWarning(entry, "ItemNames"));
        Assert.DoesNotContain(fixture.Logs.Entries, entry => entry.Contains(SecretName, StringComparison.Ordinal));
        Assert.True(metrics.HasPoint("axis.queries.timeouts", ("axis.tenant", TenantA), ("axis.application", "RecordsApp")));
    }

    [Fact]
    public async Task Every_read_route_in_the_endpoint_map_runs_under_its_timeout()
    {
        await fixture.ResetAsync();
        var id = await InsertItemAsync(SecretName);
        var routes = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Where(endpoint => _prefixes.Any(prefix => endpoint.RoutePattern.RawText!.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
        Assert.NotEmpty(routes);

        // An export route is a read route too. It runs under its own limit, and it needs a format.
        var exports = routes.Where(IsExport).Select(route => route.RoutePattern.RawText).ToList();
        Assert.Equal(
            [
                "/api/apps/{app}/data-sources/{dataSource}/export",
                "/api/apps/{app}/data-sources/{module}/{dataSource}/export",
                "/api/apps/{app}/entities/{entity}/records/export",
                "/api/apps/{app}/entities/{module}/{entity}/records/export",
            ],
            exports.Order(StringComparer.Ordinal));

        // Each route resolves the application or the site first, so locking the release tables stalls every one of them.
        await using var host = CreateHost();
        using var client = host.CreateClient().SignedInAs("requester");

        await using (await LockAsync("axis.active_releases", "axis.releases", "axis.active_sites"))
        {
            foreach (var route in routes)
            {
                var pattern = route.RoutePattern;
                var setting = IsExport(route) ? "Data:ExportQueryTimeout" : "Data:QueryTimeout";
                Assert.Contains(route.Metadata.GetOrderedMetadata<QueryTimeoutMetadata>(), metadata => metadata.Setting == setting);
                var path = pattern.RawText!;
                foreach (var parameter in pattern.Parameters)
                {
                    path = path.Replace($"{{{parameter.Name}}}", Fill(parameter.Name, id), StringComparison.Ordinal);
                }

                using var response = await GetAsync(client, IsExport(route) ? $"{path}?format=csv" : path);

                Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, $"{pattern.RawText} answered {(int)response.StatusCode}.");
            }
        }
    }

    [Fact]
    public async Task A_count_that_alone_is_slow_gives_the_items_and_a_null_total()
    {
        await fixture.ResetAsync();
        await InsertItemAsync("Desk");
        await InsertItemAsync("Chair");
        var table = ItemTable();
        await using var host = CreateHost();
        using var client = host.CreateClient().SignedInAs("requester");

        // The policy makes only the statements that start with the count slow, as the read filters of the user would.
        await AdminAsync(
            """
            CREATE FUNCTION public.axis_test_slow_count() RETURNS boolean LANGUAGE plpgsql VOLATILE AS $$
            BEGIN
                IF current_query() LIKE 'SELECT count(*)%' THEN PERFORM pg_sleep(1); END IF;
                RETURN true;
            END $$
            """);
        try
        {
            await AdminAsync($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY");
            await AdminAsync($"CREATE POLICY axis_test_slow ON {table} FOR SELECT TO {PostgreSqlServer.RuntimeRole} USING (public.axis_test_slow_count())");

            foreach (var path in new[] { Items, ItemNamesRows })
            {
                using var response = await GetAsync(client, path);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
                Assert.Equal(2, page.RootElement.GetProperty("items").GetArrayLength());
                Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("totalCount").ValueKind);
            }
        }
        finally
        {
            await AdminAsync($"DROP POLICY IF EXISTS axis_test_slow ON {table}");
            await AdminAsync($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY");
            await AdminAsync("DROP FUNCTION IF EXISTS public.axis_test_slow_count()");
        }
    }

    [Fact]
    public async Task A_write_that_waits_longer_than_the_limit_on_the_connection_of_a_timed_out_read_is_not_cancelled()
    {
        await fixture.ResetAsync();
        var id = await InsertItemAsync("Desk");
        await using var host = CreateHost();
        using var client = host.CreateClient().SignedInAs("requester");

        await using (await LockAsync(ItemTable()))
        {
            using var response = await GetAsync(client, $"{Items}/{id}");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        // The pool holds one connection, which No Reset On Close would hand on with a plain SET still in force.
        await using var admin = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await admin.OpenAsync(CancellationToken);
        await using var rowLock = await admin.BeginTransactionAsync(CancellationToken);
        await using (var command = new NpgsqlCommand($"SELECT 1 FROM {ItemTable()} WHERE {EntityNaming.Quote(EntityNaming.IdColumn)} = @id FOR UPDATE", admin, rowLock))
        {
            command.Parameters.AddWithValue("id", id);
            await command.ExecuteScalarAsync(CancellationToken);
        }

        using var request = Request(HttpMethod.Patch, $"{Items}/{id}", HostA, """{ "version": 1, "values": { "name": "Renamed" } }""");
        var patch = client.SendAsync(request, CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken);
        await rowLock.RollbackAsync(CancellationToken);

        using var patched = await patch;
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
    }

    private static readonly string[] _prefixes =
    [
        "/api/apps/{app}/entities/",
        "/api/apps/{app}/data-sources/",
        "/api/sites/{path}/pages/",
    ];

    private static bool IsExport(RouteEndpoint route) => route.RoutePattern.RawText!.EndsWith("/export", StringComparison.Ordinal);

    private static string Fill(string parameter, Guid id) => parameter switch
    {
        "app" => "RecordsApp",
        "entity" => "Item",
        "module" => "m",
        "id" => id.ToString("D"),
        "dataSource" => "ItemNames",
        "path" => "records",
        "page" => "Items",
        _ => throw new InvalidOperationException($"The test has no value for the route parameter '{parameter}'."),
    };

    private static bool IsTimeoutWarning(string entry, string subject) =>
        entry.StartsWith("Warning: A read of ", StringComparison.Ordinal)
        && entry.Contains($"A read of {subject} ran past the query timeout after ", StringComparison.Ordinal)
        && entry.EndsWith(" ms.", StringComparison.Ordinal);

    // One connection in the pool, so a limit that stayed on it would reach the next request. The limit is far below a second.
    private WebApplicationFactory<Program> CreateHost() =>
        fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Data:QueryTimeout", "00:00:00.200");
            builder.UseSetting("Data:ExportQueryTimeout", "00:00:00.200");
            builder.UseSetting(
                "Tenants:a:ConnectionString",
                PostgreSqlServer.RuntimeConnectionString(fixture.ConnectionString(TenantA)) + ";Maximum Pool Size=1;No Reset On Close=true");
        });

    // A request that no timeout stops fails here instead of hanging the test.
    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        limit.CancelAfter(_requestLimit);
        using var request = Request(path, HostA);
        return await client.SendAsync(request, limit.Token);
    }

    private async Task<Guid> InsertItemAsync(string name) =>
        await fixture.InsertAsync(TenantA, "Item", new Dictionary<string, string?> { ["name"] = name });

    private string ItemTable()
    {
        Assert.True(fixture.Model.TryGetEntity("Item", out var item));
        return EntityNaming.QualifiedTable(EntityNaming.Table(item.Id));
    }

    private async Task AdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(CancellationToken);
    }

    // Holds ACCESS EXCLUSIVE locks in an open transaction, so every read of the tables waits until it is disposed.
    private async Task<TableLock> LockAsync(params string[] tables)
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString(TenantA));
        await connection.OpenAsync(CancellationToken);
        var transaction = await connection.BeginTransactionAsync(CancellationToken);
        foreach (var table in tables)
        {
            await using var command = new NpgsqlCommand($"LOCK TABLE {table} IN ACCESS EXCLUSIVE MODE", connection, transaction);
            await command.ExecuteNonQueryAsync(CancellationToken);
        }

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
