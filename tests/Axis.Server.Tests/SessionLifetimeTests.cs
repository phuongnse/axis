using System.Net;
using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>
/// The session ends after 1 hour without requests, or 8 hours after sign-in. A test-only path
/// creates the session, as the OIDC callback does, and a test clock replaces the server's clock.
/// </summary>
public sealed class SessionLifetimeTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private readonly TestClock _clock = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_session_ends_after_one_hour_without_requests()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await SignInAsync(client);

        _clock.Advance(TimeSpan.FromMinutes(59));
        var beforeIdleLimit = await GetMeAsync(client);
        _clock.Advance(TimeSpan.FromMinutes(61));
        var afterIdleLimit = await GetMeAsync(client);

        Assert.Equal(HttpStatusCode.OK, beforeIdleLimit);
        Assert.Equal(HttpStatusCode.Unauthorized, afterIdleLimit);
    }

    [Fact]
    public async Task The_session_ends_eight_hours_after_sign_in_even_with_regular_requests()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();
        await SignInAsync(client);

        // Every 30 minutes up to 7 hours 30 minutes after sign-in.
        for (var request = 1; request <= 15; request++)
        {
            _clock.Advance(TimeSpan.FromMinutes(30));
            Assert.Equal(HttpStatusCode.OK, await GetMeAsync(client));
        }

        _clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetMeAsync(client));
    }

    private static async Task SignInAsync(HttpClient client)
    {
        var response = await client.GetAsync(new Uri("/test/session", UriKind.Relative), CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<HttpStatusCode> GetMeAsync(HttpClient client) =>
        (await client.GetAsync(new Uri("/api/me", UriKind.Relative), CancellationToken)).StatusCode;

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseTestOidc("default");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(_clock);
                services.AddSingleton<IStartupFilter, SessionStartupFilter>();
            });
        });

    /// <summary>Answers <c>GET /test/session</c> by signing anna in to the default tenant.</summary>
    private sealed class SessionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/test/session")
                {
                    await context.SignInAsync(Session.Scheme, Session.CreatePrincipal("default", "anna", "Anna Employee", ["employee"]));
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                }

                await nextMiddleware(context);
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
