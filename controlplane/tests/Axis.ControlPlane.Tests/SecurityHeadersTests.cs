using Axis.Server.Http;

namespace Axis.ControlPlane.Tests;

public sealed class SecurityHeadersTests
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/nothing-here")]
    public async Task Every_response_carries_the_platforms_security_headers(string path)
    {
        using var factory = ControlPlaneFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, Single(response, "Content-Security-Policy"));
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Single(response, "Referrer-Policy"));
        Assert.Equal("same-origin", Single(response, "Cross-Origin-Opener-Policy"));
        Assert.Equal(SecurityHeaders.PermissionsPolicy, Single(response, "Permissions-Policy"));
    }

    private static string Single(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.GetValues(name));
}
