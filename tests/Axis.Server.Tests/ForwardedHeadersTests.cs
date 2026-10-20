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
/// Behind a proxy that ends TLS, the server applies the forwarded scheme and host only from the proxies
/// named in the settings. Tenant b is served at b.example.test, and the tests send each request through the
/// test server, which lets a test set the client address.
/// </summary>
public sealed class ForwardedHeadersTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string HostB = "b.example.test";
    private const string ProxyAddress = "10.0.0.5";
    private const string OtherAddress = "10.0.0.9";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> InvalidEntries => new()
    {
        { "ForwardedHeaders:KnownProxies:0", "not-an-ip" },
        { "ForwardedHeaders:KnownProxies:0", "" },
        { "ForwardedHeaders:KnownNetworks:0", "10.0.0.0/33" },
        { "ForwardedHeaders:KnownNetworks:0", "10.0.0.5" },
    };

    [Fact]
    public async Task A_listed_proxy_makes_the_forwarded_host_the_tenant_and_the_forwarded_scheme_secures_the_cookie()
    {
        await using var factory = CreateFactory(ProxyAddress);
        var session = await SignInAsync(factory);

        var response = await GetMeAsync(factory, ProxyAddress, "server.internal", HostB, "https", session);

        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);
        Assert.Contains("secure", RenewedSessionCookie(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_network_range_names_its_proxies_too()
    {
        await using var factory = CreateFactory(proxies: null, networks: "10.0.0.0/24");
        var session = await SignInAsync(factory);

        var response = await GetMeAsync(factory, ProxyAddress, "server.internal", HostB, "https", session);

        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);
        Assert.Contains("secure", RenewedSessionCookie(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_address_that_is_not_listed_cannot_fake_its_host()
    {
        await using var factory = CreateFactory(ProxyAddress);
        var session = await SignInAsync(factory);

        var response = await GetMeAsync(factory, OtherAddress, "server.internal", HostB, "https", session);

        Assert.Equal(StatusCodes.Status404NotFound, response.Response.StatusCode);
    }

    [Fact]
    public async Task An_address_that_is_not_listed_cannot_fake_its_scheme()
    {
        await using var factory = CreateFactory(ProxyAddress);
        var session = await SignInAsync(factory);

        var response = await GetMeAsync(factory, OtherAddress, HostB, forwardedHost: null, "https", session);

        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);
        Assert.DoesNotContain("secure", RenewedSessionCookie(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Loopback_is_not_trusted_unless_it_is_listed()
    {
        await using var factory = CreateFactory(ProxyAddress);
        var session = await SignInAsync(factory);

        var response = await GetMeAsync(factory, "127.0.0.1", "server.internal", HostB, "https", session);

        Assert.Equal(StatusCodes.Status404NotFound, response.Response.StatusCode);
    }

    [Fact]
    public async Task A_request_without_a_client_address_reads_no_forwarded_header()
    {
        await using var factory = CreateFactory(ProxyAddress);
        var session = await SignInAsync(factory);

        var response = await GetMeAsync(factory, remoteAddress: null, "server.internal", HostB, "https", session);

        Assert.Equal(StatusCodes.Status404NotFound, response.Response.StatusCode);
    }

    [Fact]
    public async Task With_no_setting_no_forwarded_header_changes_the_request()
    {
        await using var factory = CreateFactory(proxies: null);
        var session = await SignInAsync(factory);

        var forwarded = await GetMeAsync(factory, ProxyAddress, "server.internal", HostB, "https", session);
        var own = await GetMeAsync(factory, ProxyAddress, HostB, forwardedHost: null, "https", session);

        Assert.Equal(StatusCodes.Status404NotFound, forwarded.Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, own.Response.StatusCode);
        Assert.DoesNotContain("secure", RenewedSessionCookie(own), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(InvalidEntries))]
    public async Task Startup_fails_with_a_message_naming_the_setting_with_the_bad_entry(string setting, string value)
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ConfigureTenants(builder);
            builder.UseSetting(setting, value);
        });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(setting[..setting.LastIndexOf(':')], exception.Message, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(string? proxies, string? networks = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ConfigureTenants(builder);
            if (proxies is not null)
            {
                builder.UseSetting("ForwardedHeaders:KnownProxies:0", proxies);
            }

            if (networks is not null)
            {
                builder.UseSetting("ForwardedHeaders:KnownNetworks:0", networks);
            }
        });

    private static void ConfigureTenants(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseTestPlatform();
        builder.UseSetting("Tenants:default:Hosts:0", "localhost");
        builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
        builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
        builder.UseSetting("Tenants:b:Hosts:0", HostB);
        builder.UseSetting("Tenants:b:ConnectionString", UnreachableDatabase);
        builder.UseSetting("Tenants:b:SchemaConnectionString", UnreachableDatabase);
        builder.UseTestOidc("default", "b");
        builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, SessionStartupFilter>());
    }

    /// <summary>Signs anna in to tenant b on its own host and returns the session cookie.</summary>
    private static async Task<string> SignInAsync(WebApplicationFactory<Program> factory)
    {
        _ = factory.Server;
        var context = await factory.Server.SendAsync(
            request =>
            {
                request.Request.Method = HttpMethods.Get;
                request.Request.Path = "/test/session";
                request.Request.Host = new HostString(HostB);
            },
            CancellationToken);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        return SessionCookie(context);
    }

    private static Task<HttpContext> GetMeAsync(
        WebApplicationFactory<Program> factory,
        string? remoteAddress,
        string host,
        string? forwardedHost,
        string forwardedProto,
        string session) =>
        factory.Server.SendAsync(
            request =>
            {
                request.Request.Method = HttpMethods.Get;
                request.Request.Path = "/api/me";
                request.Request.Host = new HostString(host);
                request.Connection.RemoteIpAddress = remoteAddress is null ? null : IPAddress.Parse(remoteAddress);
                request.Request.Headers.Cookie = $"{Session.Scheme}={session}";
                request.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
                request.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
                if (forwardedHost is not null)
                {
                    request.Request.Headers["X-Forwarded-Host"] = forwardedHost;
                }
            },
            CancellationToken);

    private static string SessionCookie(HttpContext context) =>
        RenewedSessionCookie(context)[(Session.Scheme.Length + 1)..].Split(';')[0];

    private static string RenewedSessionCookie(HttpContext context) =>
        context.Response.Headers.SetCookie.Single(cookie => cookie!.StartsWith($"{Session.Scheme}=", StringComparison.Ordinal))!;

    /// <summary>Answers <c>GET /test/session</c> by signing anna in to tenant b.</summary>
    private sealed class SessionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/test/session")
                {
                    await context.SignInAsync(Session.Scheme, Session.CreatePrincipal("b", "anna", "Anna Employee", ["employee"]));
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                }

                await nextMiddleware(context);
            });
            next(app);
        };
    }
}
