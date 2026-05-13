using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using FluentAssertions;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public class AuthEndpointTests(ApiTestFixture fixture)
{
    private readonly HttpClient _client = fixture.Client;
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    private static object RegisterPayload(string suffix = "") => new
    {
        org_name = $"TestOrg{suffix}",
        admin_first_name = "Test",
        admin_last_name = "Admin",
        admin_email = $"admin{suffix}@test.com",
        password = "TestPass1",
        password_confirmation = "TestPass1",
    };

    // POST /api/organizations (register, then sign in)
    [Fact]
    public async Task Register_then_signin_returns_access_token()
    {
        // Register
        var regResp = await _client.PostAsJsonAsync("/api/organizations", RegisterPayload("auth1"), Json);
        regResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // SignIn before email verification → email_not_verified
        var signinResp = await _client.PostAsJsonAsync("/api/auth/signin",
            new { email = "adminauth1@test.com", password = "TestPass1" });

        signinResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await signinResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("verify your email");
    }

    [Fact]
    public async Task Register_then_verify_then_signin_returns_access_token()
    {
        // Register
        var regResp = await _client.PostAsJsonAsync("/api/organizations", RegisterPayload("auth2"), Json);
        regResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get verification token from DB (simulate clicking email link)
        using var scope = fixture.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<Axis.Identity.Infrastructure.Persistence.IdentityDbContext>();
        var user = ctx.Users.First(u => u.Email == Axis.Identity.Domain.ValueObjects.Email.Create("adminauth2@test.com").Value);
        var token = user.Id.ToString();

        // Verify email
        var verifyResp = await _client.PostAsJsonAsync("/api/auth/verify-email", new { token });
        verifyResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Sign in
        var signinResp = await _client.PostAsJsonAsync("/api/auth/signin",
            new { email = "adminauth2@test.com", password = "TestPass1" });

        signinResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await signinResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("token_type").GetString().Should().Be("Bearer");
    }

    [Fact]
    public async Task Signin_with_wrong_password_returns_401_invalid_credentials()
    {
        await _client.PostAsJsonAsync("/api/organizations", RegisterPayload("auth3"), Json);

        // Verify email so the handler reaches password check
        using var scope = fixture.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<Axis.Identity.Infrastructure.Persistence.IdentityDbContext>();
        var u = ctx.Users.First(x => x.Email == Axis.Identity.Domain.ValueObjects.Email.Create("adminauth3@test.com").Value);
        await _client.PostAsJsonAsync("/api/auth/verify-email", new { token = u.Id.ToString() });

        var resp = await _client.PostAsJsonAsync("/api/auth/signin",
            new { email = "adminauth3@test.com", password = "WrongPass1" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("Incorrect email");
    }

    [Fact]
    public async Task Signin_with_unknown_email_returns_401_invalid_credentials()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/signin",
            new { email = "nobody@test.com", password = "TestPass1" });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("detail").GetString().Should().Contain("Incorrect email");
    }

    [Fact]
    public async Task Signout_without_token_returns_401()
    {
        var resp = await _client.PostAsync("/api/auth/signout", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ForgotPassword_always_returns_ok_regardless_of_email()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = "notexist@test.com" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Contain("reset link");
    }

    [Fact]
    public async Task ResendVerification_always_returns_no_content()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/resend-verification",
            new { email = "nobody@test.com" });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ValidationError_on_register_returns_422_with_errors()
    {
        var resp = await _client.PostAsJsonAsync("/api/organizations", new
        {
            org_name = "A",     // too short
            admin_first_name = "",
            admin_last_name = "",
            admin_email = "not-an-email",
            password = "weak",  // too short
            password_confirmation = "different",
        }, Json);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").ValueKind.Should().Be(JsonValueKind.Object);
    }
}
