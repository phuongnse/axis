using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public class AuthEndpointTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    private static object RegisterPayload(string suffix) => new
    {
        org_name = $"TestOrg{suffix}",
        admin_first_name = "Test",
        admin_last_name = "Admin",
        admin_email = $"admin{suffix}@test.com",
        password = "TestPass1",
        password_confirmation = "TestPass1",
    };

    // ── Registration ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_WhenPayloadIsValid_Returns200()
    {
        HttpResponseMessage resp = await _client.PostAsJsonAsync(
            "/api/organizations", RegisterPayload("auth_reg1"), Json);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_WhenPayloadIsInvalid_Returns400WithErrors()
    {
        HttpResponseMessage resp = await _client.PostAsJsonAsync("/api/organizations", new
        {
            org_name = "A",          // too short
            admin_first_name = "",
            admin_last_name = "",
            admin_email = "not-an-email",
            password = "weak",       // too short
            password_confirmation = "different",
        }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Object);
    }

    // ── PKCE Flow ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Authorize_WhenEmailIsVerified_ReturnsAccessToken()
    {
        // Register
        await _client.PostAsJsonAsync("/api/organizations", RegisterPayload("auth_pkce1"), Json);

        // Verify email
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext ctx =
            scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        User user = ctx.Users
            .First(u => u.Email == Email.Create("adminauth_pkce1@test.com").Value);

        HttpResponseMessage verifyResp = await _client.PostAsJsonAsync(
            "/api/auth/verify-email", new { token = user.Id.ToString() }, Json);
        verifyResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Full PKCE flow on independent client (isolated cookie jar)
        HttpClient pkceClient = fixture.CreateNewClient();
        string accessToken = await AuthHelper.CompletePkceFlowAsync(
            pkceClient, "adminauth_pkce1@test.com", "TestPass1");

        accessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Authorize_WhenEmailIsNotVerified_Returns401()
    {
        // Register but do NOT verify email
        await _client.PostAsJsonAsync("/api/organizations", RegisterPayload("auth_noverify1"), Json);

        // POST /connect/login directly — should fail because email is not verified
        HttpClient pkceClient = fixture.CreateNewClient();
        HttpResponseMessage loginResp = await pkceClient.PostAsync("/connect/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = "adminauth_noverify1@test.com",
                ["password"] = "TestPass1",
                ["return_url"] = "/connect/authorize",
            }));

        loginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        JsonElement body = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("verify your email");
    }

    [Fact]
    public async Task Login_WhenPasswordIsWrong_Returns401()
    {
        // Register + verify
        await _client.PostAsJsonAsync("/api/organizations", RegisterPayload("auth_badpwd1"), Json);

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext ctx =
            scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        User user = ctx.Users
            .First(u => u.Email == Email.Create("adminauth_badpwd1@test.com").Value);
        await _client.PostAsJsonAsync("/api/auth/verify-email", new { token = user.Id.ToString() }, Json);

        // Attempt login with wrong password
        HttpClient pkceClient = fixture.CreateNewClient();
        HttpResponseMessage loginResp = await pkceClient.PostAsync("/connect/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = "adminauth_badpwd1@test.com",
                ["password"] = "WrongPassword1",
                ["return_url"] = "/connect/authorize",
            }));

        loginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        JsonElement body = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("Incorrect email");
    }

    [Fact]
    public async Task Login_WhenEmailIsUnknown_Returns401()
    {
        HttpClient pkceClient = fixture.CreateNewClient();
        HttpResponseMessage loginResp = await pkceClient.PostAsync("/connect/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["email"] = "nobody@test.com",
                ["password"] = "TestPass1",
                ["return_url"] = "/connect/authorize",
            }));

        loginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        JsonElement body = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("Incorrect email");
    }

    // ── Sign Out ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Signout_WhenNoToken_Returns401()
    {
        HttpResponseMessage resp = await _client.PostAsync("/api/auth/signout", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signout_WhenTokenIsValid_Returns204()
    {
        HttpClient authedClient = await AuthHelper.CreateAdminClientAsync(fixture, "auth_signout1");
        HttpResponseMessage resp = await authedClient.PostAsync("/api/auth/signout", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Password Reset ────────────────────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_WhenEmailIsAny_AlwaysReturnsOk()
    {
        HttpResponseMessage resp = await _client.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = "notexist@test.com" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Contain("reset link");
    }

    [Fact]
    public async Task ResendVerification_WhenEmailIsAny_AlwaysReturnsNoContent()
    {
        HttpResponseMessage resp = await _client.PostAsJsonAsync("/api/auth/resend-verification",
            new { email = "nobody@test.com" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── Token Refresh ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_WhenCookieIsPresent_ReturnsNewAccessToken()
    {
        // Complete PKCE flow — refresh token is set as httpOnly cookie on the client
        HttpClient pkceClient = fixture.CreateNewClient();

        await _client.PostAsJsonAsync("/api/organizations", RegisterPayload("auth_refresh1"), Json);

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext ctx =
            scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        User user = ctx.Users
            .First(u => u.Email == Email.Create("adminauth_refresh1@test.com").Value);
        await _client.PostAsJsonAsync("/api/auth/verify-email", new { token = user.Id.ToString() }, Json);

        // PKCE flow sets refresh_token cookie on pkceClient
        string firstAccessToken = await AuthHelper.CompletePkceFlowAsync(
            pkceClient, "adminauth_refresh1@test.com", "TestPass1");
        firstAccessToken.Should().NotBeNullOrEmpty();

        // Refresh — the cookie is automatically sent by pkceClient
        HttpResponseMessage refreshResp = await pkceClient.PostAsync("/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = "axis_spa",
                // refresh_token is in the httpOnly cookie — not in the body
            }));

        refreshResp.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement tokenBody = await refreshResp.Content.ReadFromJsonAsync<JsonElement>();
        string? newAccessToken = tokenBody.GetProperty("access_token").GetString();
        newAccessToken.Should().NotBeNullOrEmpty();
        // Tokens are different (rotation)
        newAccessToken.Should().NotBe(firstAccessToken);
    }
}
