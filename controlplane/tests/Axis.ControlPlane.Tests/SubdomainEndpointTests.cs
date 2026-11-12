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
        var settings = new Dictionary<string, string?> { ["ControlPlane:SignUp:ChallengeKey"] = Convert.ToBase64String(new byte[32]), ["ControlPlane:SignUp:LicencesUrl"] = "https://example.com/licences" };
        foreach (var document in new[] { "Terms", "Dpa", "Privacy" })
        {
            settings[$"ControlPlane:SignUp:{document}:Version"] = "2026-01";
            settings[$"ControlPlane:SignUp:{document}:Urls:en"] = $"https://example.com/{document}/en";
            settings[$"ControlPlane:SignUp:{document}:Urls:vi"] = $"https://example.com/{document}/vi";
        }

        using var factory = ControlPlaneFactory.Create(settings: settings);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sign-up", new { fullName = "An" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task The_sign_up_route_answers_503_without_the_challenge_key()
    {
        using var factory = ControlPlaneFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sign-up", new { fullName = "An" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
