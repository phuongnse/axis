using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Legal;
using FluentAssertions;

namespace Axis.Api.Tests.Administration;

internal static class PersonalWorkspaceOwnerApiTestSession
{
    private const string Password = "maple river sunrise";
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    public static async Task<OwnerContext> CreateAsync(ApiTestFixture fixture)
    {
        string email = $"personal-owner-{Guid.NewGuid():N}@example.com";
        using HttpRequestMessage register = new(HttpMethod.Post, "/api/users/register")
        {
            Content = JsonContent.Create(new
            {
                fullName = "Personal Owner",
                email,
                password = Password,
                passwordConfirmation = Password,
                acceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
                acceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
            }, options: Json),
        };
        register.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        (await fixture.SendBrowserMutationAsync(
            register,
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        string verificationToken = fixture.EmailCapture.GetVerificationToken(email)
            ?? throw new InvalidOperationException("Verification token was not captured.");
        (await fixture.PostBrowserJsonAsync(
            "/api/auth/verify-email",
            new { token = verificationToken },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement session = await fixture.RefreshBrowserSecurityContextAsync(
            TestContext.Current.CancellationToken);
        JsonElement user = session.GetProperty("user");
        return new OwnerContext(
            user.GetProperty("userId").GetGuid(),
            user.GetProperty("workspaceId").GetGuid());
    }

    internal sealed record OwnerContext(Guid UserId, Guid WorkspaceId);
}
