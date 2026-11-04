using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

/// <summary>
/// <c>GET /api/licences</c> answers a <c>401</c> problem without a session, the server's <c>licences.json</c> with each package's
/// version to a signed-in person, and a <c>404</c> problem when the server has no such file. The host reads a fake file from a
/// temporary content root.
/// </summary>
public sealed class LicenceEndpointTests : IAsyncLifetime
{
    // Port 1 on loopback refuses connections immediately. No test here touches a database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"axis-licences-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_anonymous_caller_gets_a_401_problem()
    {
        using var client = Factory().CreateClient();

        var response = await client.GetAsync(new Uri("/api/licences", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Sign in to read or write this data.", await response.Content.ReadAsStringAsync(CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_signed_in_caller_gets_the_packages_with_their_versions()
    {
        using var client = Factory().CreateClient().SignedInAs("reader");

        var response = await client.GetAsync(new Uri("/api/licences", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("\"version\": \"19.0.0\"", body, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"2.0.0\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_signed_in_caller_gets_a_404_problem_when_the_server_has_no_list()
    {
        File.Delete(Path.Combine(_contentRoot, "licences.json"));
        using var client = Factory().CreateClient().SignedInAs("reader");

        var response = await client.GetAsync(new Uri("/api/licences", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_contentRoot, "wwwroot"));
        File.WriteAllText(Path.Combine(_contentRoot, "wwwroot", "index.html"), "<!doctype html><title>Axis</title>");
        File.WriteAllText(
            Path.Combine(_contentRoot, "licences.json"),
            """
            {
              "npm": [
                { "name": "react", "version": "19.0.0", "licence": "MIT" }
              ],
              "nuget": [
                { "name": "Npgsql", "version": "2.0.0", "licence": "PostgreSQL" }
              ]
            }
            """);
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
            builder.UseTestAuthentication(new TestIdentity("reader", "Reader"));
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
