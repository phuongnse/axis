using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public sealed class RegisterUserFlowTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task RegisterUser_WhenRequestIsValid_CreatesStandaloneAccountWorkspaceLegalAcceptanceAndVerificationEmail()
    {
        string email = UniqueEmail();

        HttpResponseMessage response = await RegisterAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.EmailCapture.GetVerificationToken(email).Should().NotBeNullOrWhiteSpace();

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Email normalizedEmail = Email.Create(email).Value;

        User user = await db.Users.SingleAsync(u => u.Email == normalizedEmail);
        user.FirstName.Should().Be("Alice");
        user.LastName.Should().Be("Smith");
        user.PasswordHash.Should().NotBeNullOrWhiteSpace();
        user.IsEmailVerified.Should().BeFalse();
        user.AcceptedTermsVersion.Should().Be(WellKnownLegalDocuments.TermsVersion);
        user.AcceptedPrivacyVersion.Should().Be(WellKnownLegalDocuments.PrivacyVersion);
        user.LegalAcceptedAt.Should().NotBeNull();

        Workspace personalWorkspace = await db.Workspaces.SingleAsync(w =>
            w.OwnerUserId == user.Id && w.Type == WorkspaceType.Personal);
        personalWorkspace.Name.Should().Be("Alice Smith");
        personalWorkspace.OwnerEmail.Should().Be(normalizedEmail);
        personalWorkspace.Status.Should().Be(WorkspaceStatus.PendingVerification);
        personalWorkspace.AcceptedTermsVersion.Should().Be(WellKnownLegalDocuments.TermsVersion);
        personalWorkspace.AcceptedPrivacyVersion.Should().Be(WellKnownLegalDocuments.PrivacyVersion);
        personalWorkspace.LegalAcceptedAt.Should().NotBeNull();

        int activeTokenCount = await db.EmailVerificationTokens.CountAsync(t =>
            t.UserId == user.Id && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow);
        activeTokenCount.Should().Be(1);
    }

    [Fact]
    public async Task VerifyEmail_WhenLinkIsInvalidExpiredOrAlreadyUsed_ReturnsClearProblem()
    {
        HttpResponseMessage invalidResponse = await VerifyEmailAsync("not-a-real-token");

        invalidResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemDetailAsync(invalidResponse)).Should().Be("Invalid verification link.");

        string expiredEmail = UniqueEmail();
        await RegisterAsync(expiredEmail);
        string expiredToken = CapturedToken(expiredEmail);
        await ExpireTokenAsync(expiredToken);

        HttpResponseMessage expiredResponse = await VerifyEmailAsync(expiredToken);

        expiredResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemDetailAsync(expiredResponse))
            .Should().Be("This verification link has expired. Please request a new verification email.");

        string reusedEmail = UniqueEmail();
        await RegisterAsync(reusedEmail);
        string reusedToken = CapturedToken(reusedEmail);
        HttpResponseMessage firstUseResponse = await VerifyEmailAsync(reusedToken);

        firstUseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await firstUseResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("sessionEstablished").GetBoolean().Should().BeTrue();
        body.GetProperty("nextStep").GetString().Should().Be(nameof(VerifyEmailNextStep.Dashboard));

        HttpResponseMessage reusedResponse = await VerifyEmailAsync(reusedToken);

        reusedResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemDetailAsync(reusedResponse))
            .Should().Be("This link has already been used. Please sign in.");
    }

    [Fact]
    public async Task ResendVerification_WhenRequestExceedsHourlyCap_ReturnsRateLimitedProblem()
    {
        string email = UniqueEmail();
        await RegisterAsync(email);

        for (int index = 0; index < 3; index++)
        {
            HttpResponseMessage allowedResponse = await ResendVerificationAsync(email);
            allowedResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        HttpResponseMessage limitedResponse = await ResendVerificationAsync(email);

        limitedResponse.StatusCode.Should().Be((HttpStatusCode)429);
        (await ReadProblemDetailAsync(limitedResponse))
            .Should().Be("Please wait before requesting another email.");
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

    private async Task<HttpResponseMessage> ResendVerificationAsync(string email) =>
        await fixture.Client.PostAsJsonAsync("/api/auth/resend-verification", new { email }, Json);

    private async Task ExpireTokenAsync(string rawToken)
    {
        string tokenHash = OpaqueTokenGenerator.Hash(rawToken);
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        EmailVerificationToken token =
            await db.EmailVerificationTokens.SingleAsync(t => t.TokenHash == tokenHash);
        token.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }

    private string CapturedToken(string email) =>
        fixture.EmailCapture.GetVerificationToken(email)
        ?? throw new InvalidOperationException($"No verification token was captured for {email}.");

    private static async Task<string?> ReadProblemDetailAsync(HttpResponseMessage response)
    {
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("detail").GetString();
    }

    private static object ValidRegisterRequest(string email) => new
    {
        FirstName = "Alice",
        LastName = "Smith",
        Email = email,
        Password = "maple river sunrise",
        PasswordConfirmation = "maple river sunrise",
        AcceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
        AcceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
    };

    private static string UniqueEmail() => $"alice-{Guid.NewGuid():N}@example.com";
}
