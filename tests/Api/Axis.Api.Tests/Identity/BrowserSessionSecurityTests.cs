using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Legal;
using FluentAssertions;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public sealed class BrowserSessionSecurityTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;
    private const string Password = "maple river sunrise";

    [Fact]
    public async Task BrowserSession_WhenAnonymous_ReturnsGuestContractAndSecureAntiforgeryCookie()
    {
        using HttpClient client = fixture.CreateAnonymousClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/auth/session",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        body.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["authenticated", "csrfToken", "user"]);
        body.GetProperty("authenticated").GetBoolean().Should().BeFalse();
        body.GetProperty("csrfToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("user").ValueKind.Should().Be(JsonValueKind.Null);

        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies).Should().BeTrue();
        cookies!.Should().Contain(cookie =>
            cookie.StartsWith("__Host-axis-antiforgery=", StringComparison.Ordinal)
            && cookie.Contains("; path=/", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("; secure", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("; httponly", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("; samesite=strict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BrowserMutation_WhenAntiforgeryHeaderIsMissing_FailsClosed()
    {
        using HttpClient client = fixture.CreateAnonymousClient();
        await client.GetAsync("/api/auth/session", TestContext.Current.CancellationToken);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { email = UniqueEmail(), password = Password },
            Json,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        body.GetProperty("code").GetString().Should().Be("identity.invalidAntiforgery");
    }

    [Fact]
    public async Task AuthenticatedBrowser_WhenAuthorizationHeaderIsPresent_DoesNotFallBackToCookie()
    {
        using HttpClient client = fixture.CreateAnonymousClient();
        string email = UniqueEmail();
        string csrfToken = await BootstrapBrowserSecurityAsync(client);

        using HttpRequestMessage registerRequest = new(HttpMethod.Post, "/api/users/register")
        {
            Content = JsonContent.Create(ValidRegisterRequest(email), options: Json),
        };
        registerRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        registerRequest.Headers.Add("X-CSRF-TOKEN", csrfToken);
        HttpResponseMessage registerResponse = await client.SendAsync(
            registerRequest,
            TestContext.Current.CancellationToken);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        string token = fixture.EmailCapture.GetVerificationToken(email)
            ?? throw new InvalidOperationException($"No verification token was captured for {email}.");
        csrfToken = await BootstrapBrowserSecurityAsync(client);
        using HttpRequestMessage verifyRequest = new(HttpMethod.Post, "/api/auth/verify-email")
        {
            Content = JsonContent.Create(new { token }, options: Json),
        };
        verifyRequest.Headers.Add("X-CSRF-TOKEN", csrfToken);
        HttpResponseMessage verifyResponse = await client.SendAsync(
            verifyRequest,
            TestContext.Current.CancellationToken);
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage cookieResponse = await client.GetAsync(
            "/api/users/me",
            TestContext.Current.CancellationToken);
        cookieResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpRequestMessage explicitAuthorizationRequest = new(HttpMethod.Get, "/api/users/me");
        explicitAuthorizationRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", "invalid");
        HttpResponseMessage explicitAuthorizationResponse = await client.SendAsync(
            explicitAuthorizationRequest,
            TestContext.Current.CancellationToken);

        explicitAuthorizationResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<string> BootstrapBrowserSecurityAsync(HttpClient client)
    {
        JsonElement session = await client.GetFromJsonAsync<JsonElement>(
            "/api/auth/session",
            Json,
            TestContext.Current.CancellationToken);
        return session.GetProperty("csrfToken").GetString()
            ?? throw new InvalidOperationException("The browser session did not return an antiforgery token.");
    }

    private static object ValidRegisterRequest(string email) => new
    {
        FullName = "Alice Smith",
        Email = email,
        Password,
        PasswordConfirmation = Password,
        AcceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
        AcceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
    };

    private static string UniqueEmail() => $"browser-security-{Guid.NewGuid():N}@example.com";
}
