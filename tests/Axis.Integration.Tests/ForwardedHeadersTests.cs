using System.Net;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Axis.Integration.Tests;

/// <summary>
/// Sign-in behind a proxy that ends TLS. The proxy at 10.0.0.5 is listed, so its forwarded scheme and host
/// decide the tenant and the redirect address that goes to Keycloak. Tenant a is served at localhost and tenant
/// b at b.example.test. The same forwarded scheme decides <c>Strict-Transport-Security</c>, and only the listed
/// proxy can turn it on.
/// </summary>
public sealed class ForwardedHeadersTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    // Port 1 on loopback refuses connections immediately. No test here touches a database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string HostB = "b.example.test";

    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task Sign_in_through_a_listed_proxy_redirects_to_the_forwarded_https_address()
    {
        var context = await Factory.Server.SendAsync(
            request =>
            {
                request.Request.Method = HttpMethods.Get;
                request.Request.Path = "/api/auth/sign-in";
                request.Request.Host = new HostString("server.internal");
                request.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
                request.Request.Headers["X-Forwarded-Host"] = HostB;
                request.Request.Headers["X-Forwarded-Proto"] = "https";
            },
            CancellationToken);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        var location = context.Response.Headers.Location.ToString();
        Assert.Contains(
            $"redirect_uri={Uri.EscapeDataString($"https://{HostB}/api/auth/callback")}",
            location,
            StringComparison.Ordinal);
        var cookies = context.Response.Headers.SetCookie.ToArray();
        Assert.Equal(2, cookies.Length);
        Assert.All(cookies, cookie => Assert.Contains("secure", cookie!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_listed_proxy_that_forwards_https_gets_strict_transport_security()
    {
        var context = await GetMeAsync("10.0.0.5", forwardedHost: HostB);

        Assert.Equal("max-age=31536000", context.Response.Headers.StrictTransportSecurity.ToString());
    }

    [Fact]
    public async Task An_unlisted_address_that_forwards_https_gets_no_strict_transport_security()
    {
        var context = await GetMeAsync("10.0.0.9", host: HostB);

        Assert.False(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    public async ValueTask InitializeAsync()
    {
        var authority = (await keycloak.StartAsync()).ToString();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(UnreachableDatabase);
            builder.WithoutPlatformMigration();
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:a:SchemaConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:b:Hosts:0", HostB);
            builder.UseSetting("Tenants:b:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:b:SchemaConnectionString", UnreachableDatabase);
            builder.UseSetting("ForwardedHeaders:KnownProxies:0", "10.0.0.5");
            UseKeycloak(builder, "a", authority);
            UseKeycloak(builder, "b", authority);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    private Task<HttpContext> GetMeAsync(string remoteAddress, string host = "server.internal", string? forwardedHost = null) =>
        Factory.Server.SendAsync(
            request =>
            {
                request.Request.Method = HttpMethods.Get;
                request.Request.Path = "/api/me";
                request.Request.Host = new HostString(host);
                request.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
                request.Request.Headers["X-Forwarded-Proto"] = "https";
                if (forwardedHost is not null)
                {
                    request.Request.Headers["X-Forwarded-Host"] = forwardedHost;
                }
            },
            CancellationToken);

    private static void UseKeycloak(IWebHostBuilder builder, string tenant, string authority)
    {
        builder.UseSetting($"Tenants:{tenant}:Oidc:Authority", authority);
        builder.UseSetting($"Tenants:{tenant}:Oidc:ClientId", KeycloakFixture.ClientId);
        builder.UseSetting($"Tenants:{tenant}:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
    }
}
