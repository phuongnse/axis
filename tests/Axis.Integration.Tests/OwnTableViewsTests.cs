using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A person keeps their columns and saved views of a table through <c>/api/me/tables/{key}</c>, see
/// <c>docs/reference/frontend.md#table-views-and-columns</c>. The tests go through the server on PostgreSQL with two test people, and
/// check that one person never reads or changes the other's rows, that a request without a session or the CSRF header is refused, and
/// each limit. The tenant is served at localhost.
/// </summary>
public sealed class OwnTableViewsTests(PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string Key = "site.purchasing.requests";

    private static readonly string[] _shown = ["title", "state"];
    private static readonly string[] _empty = ["a", ""];
    private static readonly string[] _repeated = ["a", "a"];

    private string? _connectionString;
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task A_person_saves_lists_renames_and_deletes_views_and_sets_and_resets_columns_and_another_person_sees_none_of_it()
    {
        using var anna = CreateClient("anna");
        using var ben = CreateClient("ben");

        var empty = await ReadAsync(anna, Key);
        Assert.Null(empty.Columns);
        Assert.Empty(empty.Views);

        var created = await SendAsync(anna, HttpMethod.Post, $"/api/me/tables/{Key}/views", new { name = "  Open  ", search = "pen", filter = "[]", sort = "-title" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var view = (await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken));
        var id = view.GetProperty("id").GetGuid();
        Assert.Equal("Open", view.GetProperty("name").GetString());
        Assert.Equal($"/api/me/tables/{Key}/views/{id}", created.Headers.Location?.OriginalString);
        await SendAsync(anna, HttpMethod.Post, $"/api/me/tables/{Key}/views", new { name = "archived", search = (string?)null, filter = (string?)null, sort = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(anna, HttpMethod.Put, $"/api/me/tables/{Key}/columns", new { columns = _shown })).StatusCode);

        var listed = await ReadAsync(anna, Key);
        Assert.Equal(["title", "state"], listed.Columns);
        Assert.Equal(["archived", "Open"], listed.Views.Select(item => item.GetProperty("name").GetString()));
        Assert.Equal("pen", listed.Views[1].GetProperty("search").GetString());
        Assert.Equal(JsonValueKind.Null, listed.Views[0].GetProperty("sort").ValueKind);

        var other = await ReadAsync(ben, Key);
        Assert.Null(other.Columns);
        Assert.Empty(other.Views);
        var replace = new { name = "Taken", search = "x", filter = (string?)null, sort = (string?)null };
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(ben, HttpMethod.Put, $"/api/me/tables/{Key}/views/{id}", replace)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(ben, HttpMethod.Delete, $"/api/me/tables/{Key}/views/{id}")).StatusCode);
        Assert.Equal("Open", (await ReadAsync(anna, Key)).Views[1].GetProperty("name").GetString());

        // The same id under another key is not the person's view of that key.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(anna, HttpMethod.Delete, $"/api/me/tables/site.purchasing.other/views/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(anna, HttpMethod.Delete, $"/api/me/tables/{Key}/views/not-an-id")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(anna, HttpMethod.Put, $"/api/me/tables/{Key}/views/not-an-id", replace)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(anna, HttpMethod.Put, $"/api/me/tables/{Key}/views/{id}", new { name = "Renamed", search = (string?)null, filter = "f", sort = (string?)null })).StatusCode);
        var renamed = await ReadAsync(anna, Key);
        Assert.Equal(["archived", "Renamed"], renamed.Views.Select(item => item.GetProperty("name").GetString()));
        Assert.Equal("f", renamed.Views[1].GetProperty("filter").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(anna, HttpMethod.Delete, $"/api/me/tables/{Key}/views/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(anna, HttpMethod.Delete, $"/api/me/tables/{Key}/views/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(anna, HttpMethod.Delete, $"/api/me/tables/{Key}/columns")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(anna, HttpMethod.Delete, $"/api/me/tables/{Key}/columns")).StatusCode);
        var after = await ReadAsync(anna, Key);
        Assert.Null(after.Columns);
        Assert.Equal(["archived"], after.Views.Select(item => item.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Every_route_answers_401_without_a_session_and_a_write_needs_the_csrf_header()
    {
        using var nobody = CreateClient(null);
        using var anna = CreateClient("anna");
        var id = Guid.NewGuid();
        var body = new { name = "A", search = (string?)null, filter = (string?)null, sort = (string?)null };
        var routes = new (HttpMethod Method, string Path, object? Body)[]
        {
            (HttpMethod.Get, $"/api/me/tables/{Key}", null),
            (HttpMethod.Put, $"/api/me/tables/{Key}/columns", new { columns = new[] { "a" } }),
            (HttpMethod.Delete, $"/api/me/tables/{Key}/columns", null),
            (HttpMethod.Post, $"/api/me/tables/{Key}/views", body),
            (HttpMethod.Put, $"/api/me/tables/{Key}/views/{id}", body),
            (HttpMethod.Delete, $"/api/me/tables/{Key}/views/{id}", null),
        };

        foreach (var (method, path, content) in routes)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(nobody, method, path, content)).StatusCode);
            if (method != HttpMethod.Get)
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(anna, method, path, content, csrf: false)).StatusCode);
            }
        }

        Assert.Empty((await ReadAsync(anna, Key)).Views);
    }

    [Fact]
    public async Task A_request_past_a_limit_answers_400_or_409_and_stores_nothing()
    {
        using var anna = CreateClient("anna");
        var views = $"/api/me/tables/{Key}/views";
        Task<HttpResponseMessage> Save(string name, string? search = null, string? filter = null, string? sort = null) =>
            SendAsync(anna, HttpMethod.Post, views, new { name, search, filter, sort });

        Assert.Equal(HttpStatusCode.BadRequest, (await Save(new string('n', 81))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Save("   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Save("big", new string('s', 4000), new string('f', 4000), new string('o', 193))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Save("big", new string('é', 4097))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Save(new string('n', 80), new string('s', 4000), new string('f', 4000), new string('o', 192))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendRawAsync(anna, HttpMethod.Post, views, "{ not json")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendRawAsync(anna, HttpMethod.Post, views, null)).StatusCode);

        var columns = $"/api/me/tables/{Key}/columns";
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(anna, HttpMethod.Put, columns, new { columns = Enumerable.Range(0, 101).Select(i => $"c{i}") })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(anna, HttpMethod.Put, columns, new { columns = _empty })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(anna, HttpMethod.Put, columns, new { columns = _repeated })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(anna, HttpMethod.Put, columns, new { columns = (string[]?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(anna, HttpMethod.Put, columns, new { columns = Enumerable.Range(0, 100).Select(i => $"c{i}") })).StatusCode);

        foreach (var key in new[] { "a..b", ".a", "a.", "a b", "a/b", new string('k', 201) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(anna, HttpMethod.Get, $"/api/me/tables/{Uri.EscapeDataString(key)}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(anna, HttpMethod.Get, $"/api/me/tables/{new string('k', 200)}")).StatusCode);

        // A name is taken ignoring letter case, by a save and by a replace.
        Assert.Equal(HttpStatusCode.Created, (await Save("Open")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Save("OPEN")).StatusCode);
        var created = await Save("Closed");
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await SendAsync(anna, HttpMethod.Put, $"{views}/{id}", new { name = " open ", search = (string?)null, filter = (string?)null, sort = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(anna, HttpMethod.Put, $"{views}/{id}", new { name = "CLOSED", search = (string?)null, filter = (string?)null, sort = (string?)null })).StatusCode);

        // Three views exist, and 47 more fill the table. The 51st answers 409, and another table has room.
        for (var i = 0; i < 47; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await Save($"view {i}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Conflict, (await Save("one too many")).StatusCode);
        Assert.Equal(50, (await ReadAsync(anna, Key)).Views.Count);
        Assert.Equal(50, await CountAsync());
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(anna, HttpMethod.Post, $"/api/me/tables/site.purchasing.other/views", new { name = "one", search = (string?)null, filter = (string?)null, sort = (string?)null })).StatusCode);
    }

    public async ValueTask InitializeAsync()
    {
        _connectionString = await database.CreateDatabaseAsync("own_table_views", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(_connectionString, CancellationToken);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_connectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", _connectionString);
            builder.UseTestOidc("a");
            builder.UseTestAuthentication(new TestIdentity("anna", "Anna"), new TestIdentity("ben", "Ben"));
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private HttpClient CreateClient(string? user)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        return user is null ? client : client.SignedInAs(user);
    }

    private static async Task<(IReadOnlyList<string>? Columns, IReadOnlyList<JsonElement> Views)> ReadAsync(HttpClient client, string key)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/me/tables/{key}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var columns = body.GetProperty("columns");
        return (
            columns.ValueKind == JsonValueKind.Null ? null : [.. columns.EnumerateArray().Select(column => column.GetString()!)],
            [.. body.GetProperty("views").EnumerateArray()]);
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null, bool csrf = true)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (csrf && method != HttpMethod.Get)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        return client.SendAsync(request, CancellationToken);
    }

    private static Task<HttpResponseMessage> SendRawAsync(HttpClient client, HttpMethod method, string path, string? json)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (json is not null)
        {
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        request.Headers.Add("X-Axis-Csrf", "1");
        return client.SendAsync(request, CancellationToken);
    }

    private async Task<long> CountAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM axis.table_views WHERE table_key = @key", connection);
        command.Parameters.AddWithValue("key", Key);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }
}
