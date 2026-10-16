using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Server.Tests;

public sealed class SignInEndpointTests
{
    // Port 1 on loopback refuses connections immediately, so the database is unreachable.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("evil.example/path")]
    public async Task Sign_in_with_a_return_address_that_is_not_a_local_path_is_a_400_problem(string returnUrl)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        var response = await client.GetAsync(
            new Uri($"/api/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}", UriKind.Relative),
            CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.Location);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        Assert.Contains("The return address must be a local path.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", body, StringComparison.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", UnreachableDatabase);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseTestOidc("default");
        });
}
