using System.Net;
using Axis.Server.Authentication;
using Axis.Server.Tenancy;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// The session ends after 1 hour without requests, or 8 hours after sign-in, and its row in
/// <c>axis.sessions</c> is deleted. A test-only path creates the session, as the OIDC callback does, and a
/// test clock replaces the server's clock. Tenant a is served at localhost and tenant b at b.example.test,
/// each with its own migrated database.
/// </summary>
public sealed class SessionLifetimeTests(PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private const string HostB = "b.example.test";

    private readonly TestClock _clock = new();
    private readonly Dictionary<string, string> _databases = [];
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task The_session_ends_after_one_hour_without_requests_and_its_row_is_deleted()
    {
        using var client = CreateClient(handleCookies: true);
        await SignInAsync(client);
        Assert.Equal(1L, await CountSessionsAsync("a"));

        _clock.Advance(TimeSpan.FromMinutes(59));
        var beforeIdleLimit = await GetMeAsync(client, "localhost");
        _clock.Advance(TimeSpan.FromMinutes(61));
        var afterIdleLimit = await GetMeAsync(client, "localhost");

        Assert.Equal(HttpStatusCode.OK, beforeIdleLimit);
        Assert.Equal(HttpStatusCode.Unauthorized, afterIdleLimit);
        Assert.Equal(0L, await CountSessionsAsync("a"));
    }

    [Fact]
    public async Task The_session_ends_eight_hours_after_sign_in_even_with_regular_requests_and_its_row_is_deleted()
    {
        using var client = CreateClient(handleCookies: true);
        await SignInAsync(client);

        // Every 30 minutes up to 7 hours 30 minutes after sign-in.
        for (var request = 1; request <= 15; request++)
        {
            _clock.Advance(TimeSpan.FromMinutes(30));
            Assert.Equal(HttpStatusCode.OK, await GetMeAsync(client, "localhost"));
        }

        Assert.Equal(1L, await CountSessionsAsync("a"));
        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeAsync(client, "localhost"));
        Assert.Equal(0L, await CountSessionsAsync("a"));
    }

    [Fact]
    public async Task A_cookie_taken_to_another_tenants_host_signs_nobody_in_and_leaves_the_session_alone()
    {
        using var client = CreateClient(handleCookies: false);
        var cookie = await SignInAsync(client);

        var sameTenant = await GetMeAsync(client, "localhost", cookie);
        var otherTenant = await GetMeAsync(client, HostB, cookie);

        Assert.Equal(HttpStatusCode.OK, sameTenant);
        Assert.Equal(HttpStatusCode.Unauthorized, otherTenant);
        Assert.Equal(1L, await CountSessionsAsync("a"));
        Assert.Equal(0L, await CountSessionsAsync("b"));
    }

    public async ValueTask InitializeAsync()
    {
        foreach (var tenant in new[] { "a", "b" })
        {
            var connectionString = await database.CreateDatabaseAsync($"lifetime_{tenant}", CancellationToken);
            _databases[tenant] = connectionString;
            await DataDatabaseFixture.MigrateTenantAsync(connectionString, CancellationToken);
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(_databases["a"]);
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_databases["a"]));
            builder.UseSetting("Tenants:a:SchemaConnectionString", _databases["a"]);
            builder.UseSetting("Tenants:b:Hosts:0", HostB);
            builder.UseSetting("Tenants:b:ConnectionString", PostgreSqlServer.RuntimeConnectionString(_databases["b"]));
            builder.UseSetting("Tenants:b:SchemaConnectionString", _databases["b"]);
            builder.UseTestOidc("a", "b");
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

    private HttpClient CreateClient(bool handleCookies) =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = handleCookies,
        });

    /// <summary>Signs anna in to tenant a and returns the value of the session cookie.</summary>
    private static async Task<string> SignInAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/test/session", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var prefix = $"{Session.Scheme}=";
        var setCookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(prefix, StringComparison.Ordinal));
        return setCookie[prefix.Length..].Split(';')[0];
    }

    /// <summary>Gets <c>/api/me</c> on <paramref name="host"/>. A client that handles cookies sends its own, otherwise pass <paramref name="cookie"/>.</summary>
    private static async Task<HttpStatusCode> GetMeAsync(HttpClient client, string host, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/me", UriKind.Relative));
        request.Headers.Host = host;
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{Session.Scheme}={cookie}");
        }

        return (await client.SendAsync(request, CancellationToken)).StatusCode;
    }

    private async Task<long> CountSessionsAsync(string tenant)
    {
        await using var connection = new NpgsqlConnection(_databases[tenant]);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM axis.sessions", connection);
        return (long)(await command.ExecuteScalarAsync(CancellationToken))!;
    }

    /// <summary>Answers <c>GET /test/session</c> by signing anna in to tenant a.</summary>
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
                        await context.SignInAsync(Session.Scheme, Session.CreatePrincipal("a", "anna", "Anna Employee", ["employee"]));
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
