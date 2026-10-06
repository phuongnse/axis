using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

public sealed class RecordEndpointErrorTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unexpected_error_is_a_500_problem_without_exception_text()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/apps/Any/entities/Any/records", UriKind.Relative), CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
        });
}
