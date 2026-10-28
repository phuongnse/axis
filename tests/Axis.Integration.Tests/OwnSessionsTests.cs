using System.Buffers.Text;
using System.Net;
using System.Text.Json;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A person lists their own sessions and ends them through <c>/api/me/sessions</c>, and only after a login at the identity provider in
/// the last 5 minutes. A test-only path creates the session with the login time that the test asks for, as the OIDC callback does,
/// and a test clock replaces the server's clock. The tenant is served at localhost.
/// </summary>
public sealed class OwnSessionsTests(PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private readonly TestClock _clock = new();
    private string? _connectionString;
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task A_person_lists_only_their_own_sessions_and_the_current_one_is_marked()
    {
        using var client = CreateClient();
        var annaFirst = await SignInAsync(client, "requester", authenticatedMinutesAgo: 0);
        var annaSecond = await SignInAsync(client, "requester", authenticatedMinutesAgo: 0);
        var lan = await SignInAsync(client, "lan", authenticatedMinutesAgo: 0);

        var first = await ListAsync(client, annaFirst);
        var second = await ListAsync(client, annaSecond);
        var other = await ListAsync(client, lan);

        Assert.Equal(2, first.Sessions.Count);
        Assert.Equal(1, first.Sessions.Count(session => session.Current));
        Assert.Equal(1, second.Sessions.Count(session => session.Current));
        Assert.Equal(first.Sessions.Select(session => session.Id).Order(StringComparer.Ordinal), second.Sessions.Select(session => session.Id).Order(StringComparer.Ordinal));
        Assert.NotEqual(first.Sessions.Single(session => session.Current).Id, second.Sessions.Single(session => session.Current).Id);
        Assert.All(first.Sessions, session => Assert.Equal(_clock.GetUtcNow(), session.SignedInAt));
        Assert.True(first.EndAllowed);
        var lanSession = Assert.Single(other.Sessions);
        Assert.True(lanSession.Current);
        Assert.DoesNotContain(first.Sessions, session => session.Id == lanSession.Id);
        Assert.Equal((await HashesAsync("requester")).Order(StringComparer.Ordinal), first.Sessions.Select(session => session.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_session_past_its_idle_limit_is_not_listed_and_cannot_be_ended()
    {
        using var client = CreateClient();
        var idle = await SignInAsync(client, "requester", authenticatedMinutesAgo: 0);
        var idleId = Assert.Single((await ListAsync(client, idle)).Sessions).Id;
        _clock.Advance(TimeSpan.FromMinutes(61));
        var active = await SignInAsync(client, "requester", authenticatedMinutesAgo: 0);

        var listed = await ListAsync(client, active);
        var ended = await SendAsync(client, HttpMethod.Delete, $"/api/me/sessions/{idleId}", active);

        Assert.True(Assert.Single(listed.Sessions).Current);
        Assert.Equal(HttpStatusCode.NotFound, ended.StatusCode);
    }

    [Fact]
    public async Task Ending_the_session_of_another_person_answers_404_and_leaves_their_row()
    {
        using var client = CreateClient();
        var requester = await SignInAsync(client, "requester", authenticatedMinutesAgo: 0);
        await SignInAsync(client, "lan", authenticatedMinutesAgo: 0);
        var lanId = Assert.Single(await HashesAsync("lan"));

        var response = await SendAsync(client, HttpMethod.Delete, $"/api/me/sessions/{lanId}", requester);
        var unknown = await SendAsync(client, HttpMethod.Delete, $"/api/me/sessions/{Base64Url.EncodeToString(new byte[32])}", requester);
        var malformed = await SendAsync(client, HttpMethod.Delete, "/api/me/sessions/not*base64", requester);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        Assert.Equal([lanId], await HashesAsync("lan"));
        Assert.Equal(2L, await CountAsync());
    }

    [Theory]
    [InlineData(6)]
    [InlineData(null)]
    public async Task Both_delete_routes_answer_403_and_delete_nothing_when_the_login_is_too_old_or_unknown(int? authenticatedMinutesAgo)
    {
        using var client = CreateClient();
        var caller = await SignInAsync(client, "requester", authenticatedMinutesAgo);
        await SignInAsync(client, "requester", authenticatedMinutesAgo: 0);
        await SignInAsync(client, "lan", authenticatedMinutesAgo: 0);
        var otherId = (await ListAsync(client, caller)).Sessions.Single(session => !session.Current).Id;
        var lanId = Assert.Single(await HashesAsync("lan"));

        var listed = await ListAsync(client, caller);
        var one = await SendAsync(client, HttpMethod.Delete, $"/api/me/sessions/{otherId}", caller);
        var unknown = await SendAsync(client, HttpMethod.Delete, $"/api/me/sessions/{lanId}", caller);
        var others = await SendAsync(client, HttpMethod.Delete, "/api/me/sessions", caller);

        Assert.False(listed.EndAllowed);
        Assert.Equal(HttpStatusCode.Forbidden, one.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, others.StatusCode);
        Assert.Equal(3L, await CountAsync());
    }

    [Fact]
    public async Task A_login_exactly_five_minutes_old_may_end_sessions_and_one_second_more_may_not()
    {
        using var client = CreateClient();
        var caller = await SignInAsync(client, "requester", authenticatedMinutesAgo: 5);
        await SignInAsync(client, "requester", authenticatedMinutesAgo: 0);

        var allowed = await ListAsync(client, caller);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var late = await ListAsync(client, caller);
        var ended = await SendAsync(client, HttpMethod.Delete, "/api/me/sessions", caller);

        Assert.True(allowed.EndAllowed);
        Assert.False(late.EndAllowed);
        Assert.Equal(HttpStatusCode.Forbidden, ended.StatusCode);
        Assert.Equal(2L, await CountAsync());
    }

    [Fact]
    public async Task A_recent_caller_ends_one_session_then_all_the_others_and_the_current_session_stays()
    {
        using var client = CreateClient();
        var current = await SignInAsync(client, "requester", authenticatedMinutesAgo: 1);
        var second = await SignInAsync(client, "requester", authenticatedMinutesAgo: 0);
        var third = await SignInAsync(client, "requester", authenticatedMinutesAgo: 0);
        await SignInAsync(client, "lan", authenticatedMinutesAgo: 0);
        var secondId = (await ListAsync(client, second)).Sessions.Single(session => session.Current).Id;

        var endOne = await SendAsync(client, HttpMethod.Delete, $"/api/me/sessions/{secondId}", current);

        Assert.Equal(HttpStatusCode.NoContent, endOne.StatusCode);
        Assert.Equal(3L, await CountAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/me", second)).StatusCode);
        Assert.Equal(2, (await ListAsync(client, current)).Sessions.Count);

        var endOthers = await SendAsync(client, HttpMethod.Delete, "/api/me/sessions", current);

        Assert.Equal(HttpStatusCode.NoContent, endOthers.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(client, HttpMethod.Get, "/api/me", current)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/me", third)).StatusCode);
        var remaining = Assert.Single((await ListAsync(client, current)).Sessions);
        Assert.True(remaining.Current);
        Assert.Single(await HashesAsync("lan"));
        Assert.Equal(2L, await CountAsync());

        var endCurrent = await SendAsync(client, HttpMethod.Delete, $"/api/me/sessions/{remaining.Id}", current);

        Assert.Equal(HttpStatusCode.NoContent, endCurrent.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(client, HttpMethod.Get, "/api/me", current)).StatusCode);
        Assert.Equal(1L, await CountAsync());
    }

    [Fact]
    public async Task Without_a_session_all_three_routes_answer_401_and_a_delete_needs_the_csrf_header()
    {
        using var client = CreateClient();
        var requester = await SignInAsync(client, "requester", authenticatedMinutesAgo: 0);
        var id = Assert.Single(await HashesAsync("requester"));

        var list = await SendAsync(client, HttpMethod.Get, "/api/me/sessions", cookie: null);
        var one = await SendAsync(client, HttpMethod.Delete, $"/api/me/sessions/{id}", cookie: null);
        var others = await SendAsync(client, HttpMethod.Delete, "/api/me/sessions", cookie: null);
        var noCsrf = await SendAsync(client, HttpMethod.Delete, "/api/me/sessions", requester, csrf: false);

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, one.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, others.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Equal(1L, await CountAsync());
    }

    public async ValueTask InitializeAsync()
    {
        _connectionString = await database.CreateDatabaseAsync("own_sessions", CancellationToken);
        await DataDatabaseFixture.MigrateTenantAsync(_connectionString, CancellationToken);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_connectionString));
            builder.UseSetting("Tenants:a:SchemaConnectionString", _connectionString);
            builder.UseTestOidc("a");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(_clock);
                services.AddSingleton<IStartupFilter, SessionStartupFilter>();
            });
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private HttpClient CreateClient() =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });

    /// <summary>Signs the user in with a last login <paramref name="authenticatedMinutesAgo"/> minutes ago, or with none, and returns the session cookie's value.</summary>
    private static async Task<string> SignInAsync(HttpClient client, string user, int? authenticatedMinutesAgo)
    {
        var query = authenticatedMinutesAgo is { } minutes ? $"&authenticatedMinutesAgo={minutes}" : string.Empty;
        var response = await client.GetAsync(new Uri($"/test/session?user={user}{query}", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var prefix = $"{Session.Scheme}=";
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return setCookie[prefix.Length..].Split(';')[0];
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? cookie, bool csrf = true)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        }

        if (csrf && method != HttpMethod.Get)
        {
            request.Headers.Add("X-Axis-Csrf", "1");
        }

        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<OwnSessions> ListAsync(HttpClient client, string cookie)
    {
        using var response = await SendAsync(client, HttpMethod.Get, "/api/me/sessions", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var root = body.RootElement;
        return new OwnSessions(
            root.GetProperty("endAllowed").GetBoolean(),
            [.. root.GetProperty("sessions").EnumerateArray().Select(session => new OwnSession(
                session.GetProperty("id").GetString()!,
                session.GetProperty("signedInAt").GetDateTimeOffset(),
                session.GetProperty("lastRequestAt").GetDateTimeOffset(),
                session.GetProperty("current").GetBoolean()))]);
    }

    private async Task<List<string>> HashesAsync(string user)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT id_hash FROM axis.sessions WHERE user_id = @user", connection);
        command.Parameters.AddWithValue("user", user);
        var hashes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        while (await reader.ReadAsync(CancellationToken))
        {
            hashes.Add(Base64Url.EncodeToString(reader.GetFieldValue<byte[]>(0)));
        }

        return hashes;
    }

    private async Task<long> CountAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM axis.sessions", connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    private sealed record OwnSession(string Id, DateTimeOffset SignedInAt, DateTimeOffset LastRequestAt, bool Current);

    private sealed record OwnSessions(bool EndAllowed, IReadOnlyList<OwnSession> Sessions);

    /// <summary>
    /// Answers <c>GET /test/session?user=&lt;user&gt;&amp;authenticatedMinutesAgo=&lt;n&gt;</c> by signing the user in with a last login
    /// at the identity provider <c>n</c> minutes before the test clock, or with none when <c>n</c> is missing.
    /// </summary>
    private sealed class SessionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // The session store reads the tenant, which the real pipeline resolves only after this filter.
            app.UseWhen(
                context => HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/test/session",
                branch =>
                {
                    branch.UseTenantResolution();
                    branch.Run(async context =>
                    {
                        var tenantId = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current!.TenantId;
                        var clock = context.RequestServices.GetRequiredService<TimeProvider>();
                        var user = context.Request.Query["user"].ToString();
                        DateTimeOffset? authenticatedAt = int.TryParse(context.Request.Query["authenticatedMinutesAgo"], out var minutes)
                            ? clock.GetUtcNow() - TimeSpan.FromMinutes(minutes)
                            : null;
                        await context.SignInAsync(Session.Scheme, Session.CreatePrincipal(tenantId, user, user, ["employee"], authenticatedAt));
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                    });
                });
            next(app);
        };
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan time) => _utcNow += time;
    }
}
