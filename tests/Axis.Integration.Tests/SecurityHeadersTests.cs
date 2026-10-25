using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Axis.Integration.Tests;

/// <summary>
/// The security headers of D85 on the SPA page, a hashed static file, an API answer, a <c>401</c> and the tenant
/// <c>404</c>, and <c>Strict-Transport-Security</c> only over <c>https</c> outside <c>Development</c>. The host
/// serves a fake web root from a temporary content root, because the real one loads the Development settings.
/// </summary>
public sealed class SecurityHeadersTests : IAsyncLifetime
{
    // Port 1 on loopback refuses connections immediately. No test here touches a database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string Asset = "/assets/index-abc123.js";

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"axis-security-headers-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program>? _testing;
    private WebApplicationFactory<Program>? _development;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/", false)]
    [InlineData(Asset, false)]
    [InlineData("/api/me", true)]
    public async Task Every_answer_carries_the_headers_and_only_the_api_is_not_stored(string path, bool api)
    {
        var context = await GetAsync(_testing, path);

        AssertSecurityHeaders(context);
        Assert.False(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
        var cacheControl = context.Response.Headers.CacheControl.ToString();
        if (api)
        {
            Assert.Equal("no-store", cacheControl);
        }
        else
        {
            Assert.DoesNotContain("no-store", cacheControl, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_hashed_file_is_cached_for_a_year_and_the_page_is_not()
    {
        var asset = await GetAsync(_testing, Asset);
        var page = await GetAsync(_testing, "/");

        Assert.Equal("public, max-age=31536000, immutable", asset.Response.Headers.CacheControl.ToString());
        Assert.Equal(StatusCodes.Status200OK, page.Response.StatusCode);
        Assert.False(page.Response.Headers.ContainsKey("Cache-Control"));
    }

    [Fact]
    public async Task An_https_request_gets_strict_transport_security()
    {
        var context = await GetAsync(_testing, "/", "https");

        Assert.Equal("max-age=31536000", context.Response.Headers.StrictTransportSecurity.ToString());
    }

    [Fact]
    public async Task An_https_request_in_development_gets_no_strict_transport_security()
    {
        var context = await GetAsync(_development, "/", "https");

        AssertSecurityHeaders(context);
        Assert.False(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public async Task A_401_carries_the_headers()
    {
        var context = await GetAsync(_testing, "/api/me");

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        AssertSecurityHeaders(context);
    }

    [Fact]
    public async Task The_answer_to_an_unknown_host_carries_the_headers()
    {
        var context = await GetAsync(_testing, "/", host: "unknown.example.test");

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        AssertSecurityHeaders(context);
    }

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_contentRoot, "wwwroot", "assets"));
        File.WriteAllText(Path.Combine(_contentRoot, "wwwroot", "index.html"), "<!doctype html><title>Axis</title>");
        File.WriteAllText(Path.Combine(_contentRoot, "wwwroot", "assets", "index-abc123.js"), "export {};");
        _testing = CreateFactory("Testing");
        _development = CreateFactory("Development");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_testing is not null)
        {
            await _testing.DisposeAsync();
        }

        if (_development is not null)
        {
            await _development.DisposeAsync();
        }

        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(string environment) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseContentRoot(_contentRoot);
            builder.UseTestPlatform(UnreachableDatabase);
            builder.WithoutPlatformMigration();
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:a:SchemaConnectionString", UnreachableDatabase);
            builder.UseTestOidc("a");
        });

    private static Task<HttpContext> GetAsync(
        WebApplicationFactory<Program>? factory,
        string path,
        string scheme = "http",
        string host = "localhost")
    {
        var server = (factory ?? throw new InvalidOperationException("The test host is not started.")).Server;
        return server.SendAsync(
            context =>
            {
                context.Request.Method = HttpMethods.Get;
                context.Request.Path = path;
                context.Request.Scheme = scheme;
                context.Request.Host = new HostString(host);
            },
            CancellationToken);
    }

    private static void AssertSecurityHeaders(HttpContext context)
    {
        var headers = context.Response.Headers;
        Assert.Equal(
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; "
            + "font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'",
            headers.ContentSecurityPolicy.ToString());
        Assert.Equal("nosniff", headers.XContentTypeOptions.ToString());
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"].ToString());
        Assert.Equal("same-origin", headers["Cross-Origin-Opener-Policy"].ToString());
        Assert.Equal("camera=(), microphone=(), geolocation=(), payment=(), usb=()", headers["Permissions-Policy"].ToString());
    }
}
