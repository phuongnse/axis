using Axis.Server.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Axis.Server.Tests;

/// <summary>
/// The name and attributes of the session cookie. Outside Development and Testing it is
/// <c>__Host-Axis.Session</c>, with Secure, Path=/ and no domain, which a browser enforces. Development
/// and Testing run over plain http, so they keep the name Axis.Session. A test-only path signs anna in with
/// in-memory sessions, so no database is needed. The row of a session is tested on PostgreSQL in the
/// integration tests.
/// </summary>
public sealed class SessionCookieTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public void Only_development_and_testing_allow_plain_http(string environment, bool expected)
    {
        var host = new TestHostEnvironment(environment);

        Assert.Equal(expected, Session.AllowsPlainHttp(host));
    }

    [Fact]
    public async Task Production_sets_the_host_cookie_with_secure_path_root_and_no_domain()
    {
        await using var factory = CreateFactory("Production");

        var cookie = await SignInAsync(factory, "https");

        Assert.StartsWith("__Host-Axis.Session=", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Testing", false)]
    public async Task The_helper_cookies_have_the_secure_prefix_only_outside_development_and_testing(string environment, bool prefixed)
    {
        await using var factory = CreateFactory(environment);

        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(TenantSignIn.SchemeName("default"));

        Assert.Equal(prefixed, options.CorrelationCookie.Name?.StartsWith("__Secure-Axis.Correlation.", StringComparison.Ordinal));
        Assert.Equal(prefixed, options.NonceCookie.Name?.StartsWith("__Secure-Axis.Nonce.", StringComparison.Ordinal));
        var expected = prefixed ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        Assert.Equal(expected, options.CorrelationCookie.SecurePolicy);
        Assert.Equal(expected, options.NonceCookie.SecurePolicy);
    }

    [Fact]
    public async Task Testing_over_http_keeps_the_plain_cookie_name()
    {
        await using var factory = CreateFactory("Testing");

        var cookie = await SignInAsync(factory, "http");

        Assert.StartsWith("Axis.Session=", cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> SignInAsync(WebApplicationFactory<Program> factory, string scheme)
    {
        _ = factory.Server;
        var context = await factory.Server.SendAsync(
            request =>
            {
                request.Request.Method = HttpMethods.Get;
                request.Request.Scheme = scheme;
                request.Request.Path = "/test/session";
                request.Request.Host = new HostString("localhost");
            },
            CancellationToken);
        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        return Assert.Single(context.Response.Headers.SetCookie)!;
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseTestPlatform();
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseTestOidc("default");
            builder.UseInMemorySessions();
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, SessionStartupFilter>());
        });

    private sealed class TestHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Axis.Server.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

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
}
