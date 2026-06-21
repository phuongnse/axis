using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Identity.Domain.Aggregates;

namespace Axis.Api.Tests.Helpers;

/// <summary>
/// Shared test helper that registers a workspace, verifies email, runs the full
/// Authorization Code + PKCE flow, and returns an authenticated HttpClient.
/// </summary>
public static class AuthHelper
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    // Test redirect URI - must match a registered RedirectUri in the SPA client seed
    private const string RedirectUri = "https://localhost/callback";
    private const string ClientId = "axis_spa";

    /// <summary>
    /// Registers a workspace with the given suffix, verifies email, completes the
    /// Authorization Code + PKCE flow, and returns a pre-configured Bearer client.
    /// </summary>
    public static async Task<HttpClient> CreateAdminClientAsync(ApiTestFixture fixture, string suffix)
    {
        string email = await RegisterAndVerifyAdminAsync(fixture, suffix);

        await fixture.EnsureWorkspaceProvisionedAsync(email);

        string accessToken = await CompletePkceFlowWithSessionAsync(fixture.Client);

        HttpClient authedClient = fixture.CreateNewClient();
        authedClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        return authedClient;
    }

    public static async Task<string> RegisterAndVerifyAdminAsync(
        ApiTestFixture fixture,
        string suffix)
    {
        string email = await RegisterFirstAdminWithoutUserVerificationAsync(fixture, suffix);

        string userVerifyToken = fixture.EmailCapture.GetVerificationToken(email)
            ?? throw new InvalidOperationException(
                $"No verification token captured for {email}.");

        HttpResponseMessage userVerifyResp = await fixture.Client.PostAsJsonAsync(
            "/api/auth/verify-email", new { token = userVerifyToken }, Json);
        if (userVerifyResp.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"User email verification failed: {userVerifyResp.StatusCode}");

        return email;
    }

    public static async Task<string> RegisterFirstAdminWithoutUserVerificationAsync(
        ApiTestFixture fixture,
        string suffix)
    {
        string setupToken = await RegisterAndVerifyWorkspaceAsync(fixture, suffix);
        string email = TestRegistrationPayload.AdminEmail(suffix);

        HttpResponseMessage userRegResp = await fixture.Client.PostAsJsonAsync(
            "/api/users/register", TestRegistrationPayload.CreateUser(suffix, setupToken), Json);
        if (!userRegResp.IsSuccessStatusCode)
            throw new InvalidOperationException($"User registration failed: {userRegResp.StatusCode}");

        return email;
    }

    public static async Task<string> RegisterAndVerifyWorkspaceAsync(
        ApiTestFixture fixture,
        string suffix)
    {
        string contactEmail = TestRegistrationPayload.WorkspaceContactEmail(suffix);

        HttpResponseMessage regResp = await fixture.Client.PostAsJsonAsync(
            "/api/workspaces", TestRegistrationPayload.Create(suffix), Json);

        if (!regResp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Registration failed: {regResp.StatusCode}");

        string verifyToken = fixture.EmailCapture.GetVerificationToken(contactEmail)
            ?? throw new InvalidOperationException(
                $"No verification token captured for {contactEmail}.");

        HttpResponseMessage verifyResp = await fixture.Client.PostAsJsonAsync(
            "/api/auth/verify-email", new { token = verifyToken }, Json);
        if (verifyResp.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Workspace verification failed: {verifyResp.StatusCode}");

        JsonElement verifyBody = await verifyResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        string? setupToken = verifyBody.GetProperty("workspaceSetupToken").GetString();

        return setupToken
            ?? throw new InvalidOperationException("No WorkspaceSetupToken in verification response.");
    }

    /// <summary>
    /// Executes PKCE after the session cookie was established (e.g. by verify-email).
    /// </summary>
    public static async Task<string> CompletePkceFlowWithSessionAsync(HttpClient client)
    {
        string codeVerifier = GenerateCodeVerifier();
        string codeChallenge = ComputeCodeChallenge(codeVerifier);
        string state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        string authorizeQuery = string.Concat(
            "/connect/authorize",
            "?response_type=code",
            "&client_id=", Uri.EscapeDataString(ClientId),
            "&redirect_uri=", Uri.EscapeDataString(RedirectUri),
            "&code_challenge=", Uri.EscapeDataString(codeChallenge),
            "&code_challenge_method=S256",
            "&scope=", Uri.EscapeDataString("openid email profile offline_access permissions"),
            "&state=", Uri.EscapeDataString(state));

        HttpResponseMessage authResp = await client.GetAsync(authorizeQuery);
        if (authResp.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException(
                $"Expected 302 from /connect/authorize with session, got {authResp.StatusCode}. " +
                $"Body: {await authResp.Content.ReadAsStringAsync()}");
        }

        Uri callbackUri = authResp.Headers.Location
            ?? throw new InvalidOperationException("No Location header in authorize response.");

        string? code = ExtractQueryParam(callbackUri, "code");
        if (string.IsNullOrEmpty(code))
        {
            throw new InvalidOperationException(
                $"Authorization code not found in redirect: {callbackUri}");
        }

        HttpResponseMessage tokenResp = await client.PostAsync("/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["code_verifier"] = codeVerifier,
            }));

        if (!tokenResp.IsSuccessStatusCode)
        {
            string body = await tokenResp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Token exchange failed ({tokenResp.StatusCode}): {body}");
        }

        JsonElement tokenBody = await tokenResp.Content.ReadFromJsonAsync<JsonElement>();
        string? accessToken = tokenBody.GetProperty("access_token").GetString();

        return accessToken
            ?? throw new InvalidOperationException("No access_token in token response.");
    }

    /// <summary>
    /// Executes the full Authorization Code + PKCE flow and returns the access token.
    /// </summary>
    public static async Task<string> CompletePkceFlowAsync(
        HttpClient client, string email, string password)
    {
        // Generate PKCE values
        string codeVerifier = GenerateCodeVerifier();
        string codeChallenge = ComputeCodeChallenge(codeVerifier);
        string state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        string authorizeQuery = string.Concat(
            "/connect/authorize",
            "?response_type=code",
            "&client_id=", Uri.EscapeDataString(ClientId),
            "&redirect_uri=", Uri.EscapeDataString(RedirectUri),
            "&code_challenge=", Uri.EscapeDataString(codeChallenge),
            "&code_challenge_method=S256",
            "&scope=", Uri.EscapeDataString("openid email profile offline_access permissions"),
            "&state=", Uri.EscapeDataString(state));

        // Step A: GET /connect/authorize -> 302 to /connect/login
        HttpResponseMessage authResp = await client.GetAsync(authorizeQuery);
        if (authResp.StatusCode != HttpStatusCode.Redirect)
            throw new InvalidOperationException(
                $"Expected 302 from /connect/authorize, got {authResp.StatusCode}");

        // Step B: POST /connect/login -> 302 back to /connect/authorize (with session cookie set)
        string loginUrl = authResp.Headers.Location?.ToString()
            ?? "/connect/login?return_url=" + Uri.EscapeDataString(authorizeQuery);

        // Ensure return_url is set correctly
        if (!loginUrl.Contains("return_url"))
            loginUrl += (loginUrl.Contains('?') ? "&" : "?") +
                        "return_url=" + Uri.EscapeDataString(authorizeQuery);

        HttpResponseMessage loginResp = await client.PostAsync(loginUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = email,
                ["password"] = password,
                ["return_url"] = authorizeQuery,
            }));

        if (loginResp.StatusCode != HttpStatusCode.Redirect)
            throw new InvalidOperationException(
                $"Login failed ({loginResp.StatusCode}). " +
                $"Body: {await loginResp.Content.ReadAsStringAsync()}");

        // Step C: GET /connect/authorize again (cookie is now set) -> 302 to redirect_uri?code=...
        HttpResponseMessage authResp2 = await client.GetAsync(authorizeQuery);
        if (authResp2.StatusCode != HttpStatusCode.Redirect)
            throw new InvalidOperationException(
                $"Expected 302 from /connect/authorize (with cookie), got {authResp2.StatusCode}. " +
                $"Body: {await authResp2.Content.ReadAsStringAsync()}");

        Uri callbackUri = authResp2.Headers.Location
            ?? throw new InvalidOperationException("No Location header in authorize response.");

        string? code = ExtractQueryParam(callbackUri, "code");
        if (string.IsNullOrEmpty(code))
            throw new InvalidOperationException(
                $"Authorization code not found in redirect: {callbackUri}");

        // Step D: POST /connect/token - exchange code for tokens
        HttpResponseMessage tokenResp = await client.PostAsync("/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["code_verifier"] = codeVerifier,
            }));

        if (!tokenResp.IsSuccessStatusCode)
        {
            string body = await tokenResp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Token exchange failed ({tokenResp.StatusCode}): {body}");
        }

        JsonElement tokenBody = await tokenResp.Content.ReadFromJsonAsync<JsonElement>();
        string? accessToken = tokenBody.GetProperty("access_token").GetString();

        return accessToken
            ?? throw new InvalidOperationException("No access_token in token response.");
    }

    // -- PKCE helpers ----------------------------------------------------------

    private static string GenerateCodeVerifier()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        // Base64url encode (no padding)
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ComputeCodeChallenge(string codeVerifier)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string? ExtractQueryParam(Uri uri, string name)
    {
        string query = uri.Query.TrimStart('?');
        foreach (string part in query.Split('&'))
        {
            string[] kv = part.Split('=', 2);
            if (kv.Length == 2 && Uri.UnescapeDataString(kv[0]) == name)
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }
}
