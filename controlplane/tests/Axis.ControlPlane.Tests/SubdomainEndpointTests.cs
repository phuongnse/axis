using System.Net;
using System.Net.Http.Json;

namespace Axis.ControlPlane.Tests;

public sealed class SubdomainEndpointTests
{
    [Fact]
    public async Task The_route_answers_503_without_the_directory()
    {
        using var factory = ControlPlaneFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/sign-up/subdomains/acme", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task The_route_answers_503_for_a_malformed_name_too()
    {
        using var factory = ControlPlaneFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/sign-up/subdomains/A_b", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task The_sign_up_route_answers_503_without_the_directory()
    {
        using var factory = ControlPlaneFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sign-up", new { fullName = "An" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
