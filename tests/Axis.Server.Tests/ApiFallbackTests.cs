using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

/// <summary>
/// An <c>/api</c> path that matches no route answers a <c>404</c> problem to a signed-in caller and a <c>401</c> problem to
/// an anonymous one, even when the host serves the SPA's <c>index.html</c>. A page path still gets the page. The host
/// serves a fake web root from a temporary content root.
/// </summary>
public sealed class ApiFallbackTests : IAsyncLifetime
{
    // Port 1 on loopback refuses connections immediately. No test here touches a database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"axis-api-fallback-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/no-such-route")]
    [InlineData("/api/studio/b/app-b/resources")]
    public async Task A_signed_in_caller_gets_a_404_problem_from_an_unknown_api_path(string path)
    {
        using var client = Factory().CreateClient().SignedInAs("author");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.IfMatch.Add(new EntityTagHeaderValue("\"x\""));

        var response = await client.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task An_anonymous_caller_gets_a_401_problem_from_an_unknown_api_path()
    {
        using var client = Factory().CreateClient();

        var response = await client.GetAsync(new Uri("/api/no-such-route", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_page_path_still_gets_the_page()
    {
        using var client = Factory().CreateClient();

        var response = await client.GetAsync(new Uri("/some/page", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_contentRoot, "wwwroot"));
        File.WriteAllText(Path.Combine(_contentRoot, "wwwroot", "index.html"), "<!doctype html><title>Axis</title>");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(_contentRoot);
            builder.UseTestPlatform(UnreachableDatabase);
            builder.WithoutPlatformMigration();
            builder.UseSetting("Tenants:a:Hosts:0", "localhost");
            builder.UseSetting("Tenants:a:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:a:SchemaConnectionString", UnreachableDatabase);
            builder.UseTestOidc("a");
            builder.UseTestAuthentication(new TestIdentity("author", "Author User", "author"));
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    private WebApplicationFactory<Program> Factory() =>
        _factory ?? throw new InvalidOperationException("The test host is not started.");
}
