using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Server.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

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
    public async Task The_verify_page_is_served_at_sign_up_verify_with_the_platforms_content_security_policy()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/sign-up/verify", TestContext.Current.CancellationToken);

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
        Assert.Equal("Approve requests", application.GetProperty("descriptions").GetProperty("en").GetString());
        Assert.Equal("Duyệt yêu cầu", application.GetProperty("descriptions").GetProperty("vi").GetString());
        AssertPlatformPolicy(response);
    }

    [Fact]
    public async Task The_documents_are_served_with_their_versions_and_addresses_and_the_platforms_content_security_policy()
    {
        using var factory = ControlPlaneFactory.Create(settings: DocumentSettings());
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/sign-up/documents", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = body.RootElement;
        AssertDocument(root.GetProperty("terms"), "2026-01", "https://example.com/terms/en", "https://example.com/terms/vi");
        AssertDocument(root.GetProperty("dpa"), "2026-02", "https://example.com/dpa/en", "https://example.com/dpa/vi");
        AssertDocument(root.GetProperty("privacy"), "2026-03", "https://example.com/privacy/en", "https://example.com/privacy/vi");
        Assert.Equal("https://example.com/licences", root.GetProperty("licencesUrl").GetString());
        AssertPlatformPolicy(response);
    }

    [Fact]
    public async Task With_no_document_set_the_documents_answer_unavailable()
    {
        using var factory = ControlPlaneFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/sign-up/documents", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task The_challenge_is_signed_with_the_key_and_expires_ten_minutes_after_it_is_served()
    {
        var key = Encoding.UTF8.GetBytes("thirty-two-bytes-key-for-testing");
        var served = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        using var factory = ControlPlaneFactory
            .Create(settings: new Dictionary<string, string?> { ["ControlPlane:SignUp:ChallengeKey"] = Convert.ToBase64String(key) })
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FixedClock(served))));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/sign-up/challenge", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = body.RootElement;
        var challenge = root.GetProperty("challenge").GetString()!;
        Assert.Equal(Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(challenge))), root.GetProperty("signature").GetString());
        Assert.Equal("SHA-256", root.GetProperty("algorithm").GetString());
        Assert.Equal(100_000, root.GetProperty("maxnumber").GetInt32());
        Assert.EndsWith($"?expires={served.AddMinutes(10).ToUnixTimeSeconds()}", root.GetProperty("salt").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_no_challenge_key_the_challenge_answers_unavailable()
    {
        using var factory = ControlPlaneFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/sign-up/challenge", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task The_region_answers_its_tenant_domain()
    {
        using var factory = ControlPlaneFactory.Create();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/sign-up/region", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("vn.example.com", body.RootElement.GetProperty("tenantDomain").GetString());
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
            ["ControlPlane:SignUp:Applications:0:Descriptions:en"] = "Approve requests",
            ["ControlPlane:SignUp:Applications:0:Descriptions:vi"] = "Duyệt yêu cầu",
            [WebHostDefaults.WebRootKey] = _webRoot,
        });

    private static Dictionary<string, string?> DocumentSettings() => new()
    {
        ["ControlPlane:SignUp:Terms:Version"] = "2026-01",
        ["ControlPlane:SignUp:Terms:Urls:en"] = "https://example.com/terms/en",
        ["ControlPlane:SignUp:Terms:Urls:vi"] = "https://example.com/terms/vi",
        ["ControlPlane:SignUp:Dpa:Version"] = "2026-02",
        ["ControlPlane:SignUp:Dpa:Urls:en"] = "https://example.com/dpa/en",
        ["ControlPlane:SignUp:Dpa:Urls:vi"] = "https://example.com/dpa/vi",
        ["ControlPlane:SignUp:Privacy:Version"] = "2026-03",
        ["ControlPlane:SignUp:Privacy:Urls:en"] = "https://example.com/privacy/en",
        ["ControlPlane:SignUp:Privacy:Urls:vi"] = "https://example.com/privacy/vi",
        ["ControlPlane:SignUp:LicencesUrl"] = "https://example.com/licences",
    };

    private static void AssertDocument(JsonElement document, string version, string en, string vi)
    {
        Assert.Equal(version, document.GetProperty("version").GetString());
        Assert.Equal(en, document.GetProperty("urls").GetProperty("en").GetString());
        Assert.Equal(vi, document.GetProperty("urls").GetProperty("vi").GetString());
    }

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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
