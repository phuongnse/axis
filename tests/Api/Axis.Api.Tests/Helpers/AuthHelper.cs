using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Axis.Api.Tests.Helpers;

public static class AuthHelper
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    /// <summary>
    /// Registers an org with the given suffix, verifies email, signs in,
    /// and returns a new HttpClient pre-configured with the admin's Bearer token.
    /// </summary>
    public static async Task<HttpClient> CreateAdminClientAsync(ApiTestFixture fixture, string suffix)
    {
        var email = $"admin{suffix}@test.com";

        // 1. Register
        var regResp = await fixture.Client.PostAsJsonAsync("/api/organizations", new
        {
            org_name = $"TestOrg{suffix}",
            admin_first_name = "Test",
            admin_last_name = "Admin",
            admin_email = email,
            password = "TestPass1",
            password_confirmation = "TestPass1",
        }, Json);

        if (!regResp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Registration failed: {regResp.StatusCode}");

        // 2. Verify email — token is user.Id.ToString() per RegisterOrganizationHandler
        using var scope = fixture.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<Axis.Identity.Infrastructure.Persistence.IdentityDbContext>();
        var user = ctx.Users.First(u => u.Email ==
            Axis.Identity.Domain.ValueObjects.Email.Create(email).Value);
        var token = user.Id.ToString();

        var verifyResp = await fixture.Client.PostAsJsonAsync("/api/auth/verify-email", new { token }, Json);
        if (verifyResp.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"Email verification failed: {verifyResp.StatusCode}");

        // 3. Sign in on a fresh client (so cookie jar is independent)
        var client = fixture.CreateNewClient();
        var signinResp = await client.PostAsJsonAsync("/api/auth/signin",
            new { email, password = "TestPass1" }, Json);

        if (!signinResp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Sign-in failed: {signinResp.StatusCode}");

        var body = await signinResp.Content.ReadFromJsonAsync<JsonElement>(Json);
        var accessToken = body.GetProperty("access_token").GetString()!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
