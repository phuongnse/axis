using System.Net;
using System.Text.Json;
using Axis.Server.Http;
using Microsoft.AspNetCore.Hosting;

namespace Axis.ControlPlane.Tests;

public sealed class SignUpPageTests : IDisposable
{
    private const string Page = "<!doctype html><title>Sign-up stub</title>";

    private static readonly HashSet<string> _allowedSources = ["'self'", "'none'", "'unsafe-inline'", "data:"];

    private readonly string _webRoot = Directory.CreateTempSubdirectory("axis-sign-up-").FullName;

    public SignUpPageTests() => File.WriteAllText(Path.Combine(_webRoot, "sign-up.html"), Page);

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    [Fact]
    public async Task The_page_is_served_at_sign_up_with_the_platforms_content_security_policy()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sign-up", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Page, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        AssertPlatformPolicy(response);
    }

    [Fact]
    public async Task The_applications_are_served_with_their_titles_and_the_platforms_content_security_policy()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/sign-up/applications", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var application = Assert.Single(body.RootElement.GetProperty("applications").EnumerateArray());
        Assert.Equal("approvals", application.GetProperty("id").GetString());
        Assert.Equal("Approvals", application.GetProperty("titles").GetProperty("en").GetString());
        Assert.Equal("Phê duyệt", application.GetProperty("titles").GetProperty("vi").GetString());
        AssertPlatformPolicy(response);
    }

    [Fact]
    public async Task With_no_application_set_the_list_is_empty()
    {
        using var factory = ControlPlaneFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/sign-up/applications", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Empty(body.RootElement.GetProperty("applications").EnumerateArray());
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory() =>
        ControlPlaneFactory.Create(settings: new Dictionary<string, string?>
        {
            ["ControlPlane:SignUp:Applications:0:Id"] = "approvals",
            ["ControlPlane:SignUp:Applications:0:Titles:en"] = "Approvals",
            ["ControlPlane:SignUp:Applications:0:Titles:vi"] = "Phê duyệt",
            [WebHostDefaults.WebRootKey] = _webRoot,
        });

    private static void AssertPlatformPolicy(HttpResponseMessage response)
    {
        var policy = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, policy);
        foreach (var directive in policy.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var source in directive.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                Assert.Contains(source, _allowedSources);
            }
        }
    }
}
