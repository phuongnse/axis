using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Legal;
using FluentAssertions;

namespace Axis.Api.Tests.Administration;

internal static class WorkspaceAdministratorApiTestSession
{
    private const string Password = "maple river sunrise";
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    public static async Task<AdministratorContext> CreateAdministratorAsync(
        ApiTestFixture fixture)
    {
        string email = $"workspace-administrator-{Guid.NewGuid():N}@example.com";
        using HttpRequestMessage register = new(HttpMethod.Post, "/api/users/register")
        {
            Content = JsonContent.Create(new
            {
                fullName = "Workspace Administrator",
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

        using HttpRequestMessage createWorkspace = new(HttpMethod.Post, "/api/organizations")
        {
            Content = JsonContent.Create(
                new { name = $"Acceptance {Guid.NewGuid():N}" },
                options: Json),
        };
        createWorkspace.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        HttpResponseMessage workspaceResponse = await fixture.SendBrowserMutationAsync(
            createWorkspace,
            TestContext.Current.CancellationToken);
        workspaceResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement workspaceBody = await workspaceResponse.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        Guid workspaceId = workspaceBody.GetProperty("workspaceId").GetGuid();

        (await fixture.PostBrowserJsonAsync(
            "/api/workspace-context/begin",
            new { targetWorkspaceId = workspaceId },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.PostBrowserAsync(
            "/api/workspace-context/confirm",
            cancellationToken: TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        JsonElement session = await fixture.RefreshBrowserSecurityContextAsync(
            TestContext.Current.CancellationToken);
        JsonElement user = session.GetProperty("user");
        user.GetProperty("workspaceId").GetGuid().Should().Be(workspaceId);
        return new AdministratorContext(
            user.GetProperty("userId").GetGuid(),
            workspaceId);
    }

    internal sealed record AdministratorContext(Guid UserId, Guid WorkspaceId);
}
