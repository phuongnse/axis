using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public sealed class UserLanguagePreferenceEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;
    private const string Password = "maple river sunrise";

    [Fact]
    public async Task UpdateLanguagePreference_WhenAuthenticated_PersistsAndReturnsLanguageOnProfile()
    {
        string email = UniqueEmail();
        string accessToken = await CreateVerifiedSessionTokenAsync(email);

        HttpResponseMessage updateResponse = await SendWithBearerAsync(
            HttpMethod.Put,
            "/api/users/me/preferences/language",
            accessToken,
            new { language = "vi" });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement updateBody = await updateResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        updateBody.GetProperty("language").GetString().Should().Be("vi");

        HttpResponseMessage profileResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/users/me",
            accessToken);

        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement profileBody = await profileResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        profileBody.GetProperty("language").GetString().Should().Be("vi");

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Email normalizedEmail = Email.Create(email).Value;
        User user = await db.Users.SingleAsync(user => user.Email == normalizedEmail);
        user.LanguagePreference!.Value.Should().Be("vi");
    }

    [Fact]
    public async Task UpdateThemePreference_WhenAuthenticated_PersistsAndReturnsThemeOnProfile()
    {
        string email = UniqueEmail();
        string accessToken = await CreateVerifiedSessionTokenAsync(email);

        HttpResponseMessage updateResponse = await SendWithBearerAsync(
            HttpMethod.Put,
            "/api/users/me/preferences/theme",
            accessToken,
            new { theme = "dark" });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement updateBody = await updateResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        updateBody.GetProperty("theme").GetString().Should().Be("dark");

        HttpResponseMessage profileResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/users/me",
            accessToken);

        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement profileBody = await profileResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        profileBody.GetProperty("theme").GetString().Should().Be("dark");

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Email normalizedEmail = Email.Create(email).Value;
        User user = await db.Users.SingleAsync(user => user.Email == normalizedEmail);
        user.ThemePreference!.Value.Should().Be("dark");
    }

    [Fact]
    public async Task UpdateLanguagePreference_WhenLanguageIsUnsupported_ReturnsValidationProblem()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Put,
            "/api/users/me/preferences/language",
            accessToken,
            new { language = "fr" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("errors").EnumerateObject()
            .Select(error => error.Name)
            .Should().Contain("language");
    }

    [Fact]
    public async Task UpdateThemePreference_WhenThemeIsUnsupported_ReturnsValidationProblem()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Put,
            "/api/users/me/preferences/theme",
            accessToken,
            new { theme = "contrast" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("errors").EnumerateObject()
            .Select(error => error.Name)
            .Should().Contain("theme");
    }

    [Fact]
    public async Task VerifyEmail_WhenAccountSessionIsCreated_DoesNotCreateLanguagePreference()
    {
        string email = UniqueEmail();
        await RegisterAsync(email);

        HttpResponseMessage response = await VerifyEmailAsync(CapturedToken(email));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Email normalizedEmail = Email.Create(email).Value;
        User user = await db.Users.SingleAsync(user => user.Email == normalizedEmail);
        user.LanguagePreference.Should().BeNull();
        user.ThemePreference.Should().BeNull();
    }

    private async Task<string> CreateVerifiedSessionTokenAsync(string email)
    {
        await RegisterAsync(email);
        HttpResponseMessage verifyResponse = await VerifyEmailAsync(CapturedToken(email));
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        string verifier = CreateCodeVerifier();
        string state = Guid.NewGuid().ToString("N");
        Dictionary<string, string?> authorizeQuery = new()
        {
            ["response_type"] = "code",
            ["client_id"] = "axis_spa",
            ["redirect_uri"] = "https://localhost/callback",
            ["code_challenge"] = CreateCodeChallenge(verifier),
            ["code_challenge_method"] = "S256",
            ["scope"] = "openid email profile",
            ["state"] = state,
        };

        string authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", authorizeQuery);
        HttpResponseMessage authorizeResponse = await fixture.Client.GetAsync(authorizeUrl);
        authorizeResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        Uri redirect = authorizeResponse.Headers.Location
            ?? throw new InvalidOperationException("Authorization response did not include a redirect.");
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> callbackQuery =
            QueryHelpers.ParseQuery(redirect.Query);
        callbackQuery["state"].ToString().Should().Be(state);
        string code = callbackQuery["code"].ToString();
        code.Should().NotBeNullOrWhiteSpace();

        using FormUrlEncodedContent tokenRequest = new(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "axis_spa",
            ["redirect_uri"] = "https://localhost/callback",
            ["code"] = code,
            ["code_verifier"] = verifier,
        });

        HttpResponseMessage tokenResponse = await fixture.Client.PostAsync("/connect/token", tokenRequest);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement tokenBody = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        return tokenBody.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Token response did not include an access token.");
    }

    private async Task<HttpResponseMessage> SendWithBearerAsync(
        HttpMethod method,
        string url,
        string accessToken,
        object? body = null)
    {
        using HttpRequestMessage request = new(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);

        return await fixture.Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> RegisterAsync(string email)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/users/register")
        {
            Content = JsonContent.Create(ValidRegisterRequest(email), options: Json),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await fixture.Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> VerifyEmailAsync(string token) =>
        await fixture.Client.PostAsJsonAsync("/api/auth/verify-email", new { token }, Json);

    private string CapturedToken(string email) =>
        fixture.EmailCapture.GetVerificationToken(email)
        ?? throw new InvalidOperationException($"No verification token was captured for {email}.");

    private static object ValidRegisterRequest(string email) => new
    {
        FullName = "Alice Smith",
        Email = email,
        Password,
        PasswordConfirmation = Password,
        AcceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
        AcceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
    };

    private static string CreateCodeVerifier() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeChallenge(string verifier)
    {
        byte[] bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string UniqueEmail() => $"language-{Guid.NewGuid():N}@example.com";
}
