using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Legal;
using Axis.Rules.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;

namespace Axis.Api.Tests.Rules;

[Collection("Api")]
public sealed class FieldRuleDefinitionEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;
    private const string Password = "maple river sunrise";

    [Fact]
    public async Task FieldRuleDefinitionEndpoints_WhenAnonymous_ReturnUnauthorized()
    {
        HttpResponseMessage response = await fixture.Client.GetAsync("/api/rules/field-rule-definitions");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListFieldRuleDefinitions_WhenAuthenticated_ReturnsSystemCatalog()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/rules/field-rule-definitions",
            accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.EnumerateArray()
            .Select(definition => definition.GetProperty("definitionKey").GetString())
            .Should().Contain([
                FieldRuleDefinitionKeys.Required,
                FieldRuleDefinitionKeys.NumericRange,
                FieldRuleDefinitionKeys.SingleSelectOptions,
            ]);

        JsonElement options = body.EnumerateArray()
            .Single(definition =>
                definition.GetProperty("definitionKey").GetString()
                == FieldRuleDefinitionKeys.SingleSelectOptions);
        options.GetProperty("parameters")[0].GetProperty("key").GetString().Should().Be("options");
        options.GetProperty("parameters")[0].GetProperty("allowMultiple").GetBoolean().Should().BeTrue();
    }

    private async Task<string> CreateVerifiedSessionTokenAsync(string email)
    {
        await RegisterAsync(email);
        HttpResponseMessage verifyResponse = await VerifyEmailAsync(CapturedToken(email));
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        string verifier = CreateCodeVerifier();
        string state = Guid.NewGuid().ToString("N");
        string authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = "axis_spa",
            ["redirect_uri"] = "https://localhost/callback",
            ["code_challenge"] = CreateCodeChallenge(verifier),
            ["code_challenge_method"] = "S256",
            ["scope"] = "openid email profile",
            ["state"] = state,
        });

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
        string accessToken)
    {
        using HttpRequestMessage request = new(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
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

    private static string UniqueEmail() => $"rules-{Guid.NewGuid():N}@example.com";
}
