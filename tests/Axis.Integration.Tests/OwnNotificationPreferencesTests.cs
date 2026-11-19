using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A person reads and replaces their own notification preferences through <c>/api/me/notification-preferences</c>, see
/// <c>docs/reference/notifications.md#notification-preferences</c>. The tests go through the server on PostgreSQL with three test people.
/// They check the defaults, the round trip of all four sources, each refusal with nothing stored, that nobody reads another person's
/// preferences, and that a request with no session or only a bearer token is refused. The tenant is served at localhost.
/// </summary>
public sealed class OwnNotificationPreferencesTests(PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string Path = "/api/me/notification-preferences";

    private string? _connectionString;
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task A_person_reads_the_defaults_then_saves_and_reads_all_four_sources_back()
    {
        using var anna = CreateClient("anna");

        Assert.True(JsonNode.DeepEquals(Body(), await ReadAsync(anna)));
        Assert.Equal(0L, await CountAsync());

        var saved = Body(hour: 17, zone: "Europe/Berlin", authoringRequestEmail: false, taskDigest: true, mentionApplication: false);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(anna, HttpMethod.Put, Path, saved)).StatusCode);

        var read = await ReadAsync(anna);
        Assert.True(JsonNode.DeepEquals(saved, read));
        Assert.False(read["sources"]!["authoringRequest"]!["channels"]!["email"]!.GetValue<bool>());
        Assert.Equal(17, read["digestHour"]!.GetValue<int>());
        Assert.Equal("Europe/Berlin", read["timeZone"]!.GetValue<string>());

        // A second save replaces the first, and the person still has one row.
        var replaced = Body(hour: 0, zone: "Asia/Tokyo");
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(anna, HttpMethod.Put, Path, replaced)).StatusCode);
        Assert.True(JsonNode.DeepEquals(replaced, await ReadAsync(anna)));
        Assert.Equal(1L, await CountAsync());
    }

    [Fact]
    public async Task A_bad_value_answers_400_keyed_by_its_path_and_stores_nothing()
    {
        using var anna = CreateClient("anna");

        await AssertRefusedAsync(anna, Body(hour: 24), "/digestHour");
        await AssertRefusedAsync(anna, Body(hour: -1), "/digestHour");
        await AssertRefusedAsync(anna, Body(zone: "Mars/Base"), "/timeZone");
        await AssertRefusedAsync(anna, Body(zone: "europe/berlin"), "/timeZone");

        var noSource = Body();
        noSource["sources"]!.AsObject().Remove("authoringRequest");
        await AssertRefusedAsync(anna, noSource, "/sources/authoringRequest");

        var unknownSource = Body();
        unknownSource["sources"]!["other"] = Source();
        await AssertRefusedAsync(anna, unknownSource, "/sources/other");

        var noEmail = Body();
        noEmail["sources"]!["mention"]!["channels"]!.AsObject().Remove("email");
        await AssertRefusedAsync(anna, noEmail, "/sources/mention/channels/email");

        var textDigest = Body();
        textDigest["sources"]!["process"]!["digest"] = "yes";
        await AssertRefusedAsync(anna, textDigest, "/sources/process/digest");

        var extra = Body();
        extra["extra"] = 1;
        await AssertRefusedAsync(anna, extra, "/extra");

        var noHour = Body();
        noHour.AsObject().Remove("digestHour");
        await AssertRefusedAsync(anna, noHour, "/digestHour");

        var fractionalHour = Body();
        fractionalHour["digestHour"] = 8.5;
        await AssertRefusedAsync(anna, fractionalHour, "/digestHour");

        var malformed = await SendRawAsync(anna, "{ not json", "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Contains("\"\"", await malformed.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await SendRawAsync(anna, Body().ToJsonString(), "text/plain")).StatusCode);

        Assert.Equal(0L, await CountAsync());
        Assert.True(JsonNode.DeepEquals(Body(), await ReadAsync(anna)));
    }

    [Fact]
    public async Task Two_people_each_read_only_what_they_saved_and_a_third_who_never_saved_reads_the_defaults()
    {
        using var anna = CreateClient("anna");
        using var ben = CreateClient("ben");
        using var carl = CreateClient("carl");

        var annas = Body(hour: 17, zone: "Europe/Berlin", authoringRequestEmail: false);
        var bens = Body(hour: 6, zone: "America/New_York", taskApplication: false, processDigest: true);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(anna, HttpMethod.Put, Path, annas)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(ben, HttpMethod.Put, Path, bens)).StatusCode);

        Assert.True(JsonNode.DeepEquals(annas, await ReadAsync(anna)));
        Assert.True(JsonNode.DeepEquals(bens, await ReadAsync(ben)));
        Assert.True(JsonNode.DeepEquals(Body(), await ReadAsync(carl)));
        Assert.Equal(2L, await CountAsync());
    }

    [Fact]
    public async Task A_request_with_no_session_or_only_a_bearer_token_answers_401_and_a_write_needs_the_csrf_header()
    {
        using var nobody = CreateClient(null);
        using var anna = CreateClient("anna");

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(nobody, HttpMethod.Get, Path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(nobody, HttpMethod.Put, Path, Body())).StatusCode);

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put })
        {
            using var request = new HttpRequestMessage(method, new Uri(Path, UriKind.Relative));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-session");
            if (method == HttpMethod.Put)
            {
                request.Headers.Add("X-Axis-Csrf", "1");
                request.Content = JsonContent.Create(Body());
            }

            using var response = await nobody.SendAsync(request, CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(anna, HttpMethod.Put, Path, Body(), csrf: false)).StatusCode);
        Assert.Equal(0L, await CountAsync());
    }

    public async ValueTask InitializeAsync()
    {
        _connectionString = await database.CreateDatabaseAsync("own_notification_preferences", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(_connectionString, CancellationToken);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_connectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", _connectionString);
            builder.UseTestOidc("a");
            builder.UseTestAuthentication(new TestIdentity("anna", "Anna"), new TestIdentity("ben", "Ben"), new TestIdentity("carl", "Carl"));
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private static JsonObject Source(bool application = true, bool email = true, bool digest = false) =>
        new() { ["channels"] = new JsonObject { ["application"] = application, ["email"] = email }, ["digest"] = digest };

    // The defaults, with the named differences.
    private static JsonObject Body(
        int hour = 8,
        string zone = "UTC",
        bool authoringRequestEmail = true,
        bool taskDigest = false,
        bool mentionApplication = true,
        bool taskApplication = true,
        bool processDigest = false) =>
        new()
        {
            ["sources"] = new JsonObject
            {
                ["task"] = Source(taskApplication, digest: taskDigest),
                ["mention"] = Source(mentionApplication),
                ["process"] = Source(digest: processDigest),
                ["authoringRequest"] = Source(email: authoringRequestEmail),
            },
            ["digestHour"] = hour,
            ["timeZone"] = zone,
        };

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

    private static async Task<JsonNode> ReadAsync(HttpClient client)
    {
        var response = await SendAsync(client, HttpMethod.Get, Path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonNode>(CancellationToken))!;
    }

    private static async Task AssertRefusedAsync(HttpClient client, JsonNode body, string pointer)
    {
        var response = await SendAsync(client, HttpMethod.Put, Path, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        Assert.True(problem.GetProperty("errors").TryGetProperty(pointer, out _), $"No error keyed {pointer}: {problem}");
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, JsonNode? body = null, bool csrf = true)
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

    private static Task<HttpResponseMessage> SendRawAsync(HttpClient client, string json, string contentType)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, new Uri(Path, UriKind.Relative))
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, contentType),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        return client.SendAsync(request, CancellationToken);
    }

    private async Task<long> CountAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM axis.notification_preferences", connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }
}
