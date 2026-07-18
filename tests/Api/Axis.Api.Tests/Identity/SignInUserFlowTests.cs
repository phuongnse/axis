using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Application;
using Axis.Identity.Application.Commands.SignInUser;
using Axis.Identity.Application.Commands.VerifyEmail;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public sealed class SignInUserFlowTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;
    private const string Password = "maple river sunrise";

    [Fact]
    public async Task SignInUser_WhenVerifiedAccountIsValid_EstablishesBrowserSessionAndDoesNotCreateRegistrationSideEffects()
    {
        string email = UniqueEmail();
        await RegisterAsync(email);
        await VerifyEmailAsync(CapturedToken(email));
        (int userCountBefore, int workspaceCountBefore, int tokenCountBefore) = await CountRegistrationArtifactsAsync();

        HttpResponseMessage response = await SignInAsync(email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies).Should().BeTrue();
        cookies!.Should().Contain(cookie => cookie.Contains(".AspNetCore.Cookies", StringComparison.Ordinal));
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("sessionEstablished").GetBoolean().Should().BeTrue();
        body.GetProperty("nextStep").GetString().Should().Be(nameof(SignInNextStep.Dashboard));

        (int userCountAfter, int workspaceCountAfter, int tokenCountAfter) = await CountRegistrationArtifactsAsync();
        userCountAfter.Should().Be(userCountBefore);
        workspaceCountAfter.Should().Be(workspaceCountBefore);
        tokenCountAfter.Should().Be(tokenCountBefore);
    }

    [Fact]
    public async Task SignInUser_WhenFieldsAreMissing_ReturnsValidationErrors()
    {
        HttpResponseMessage response = await SignInAsync("", "");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        JsonElement errors = body.GetProperty("errors");
        errors.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo(["email", "password"]);
        JsonElement errorCodes = body.GetProperty("errorCodes");
        ReadCodes(errorCodes, "email").Should().Contain(IdentityProblemCodes.SignInEmailRequired);
        ReadCodes(errorCodes, "password").Should().Contain(IdentityProblemCodes.SignInPasswordRequired);
    }

    [Fact]
    public async Task SignInUser_WhenEmailUnknownPasswordWrongOrAccountInactive_ReturnsSameGenericProblem()
    {
        string wrongPasswordEmail = UniqueEmail();
        await RegisterAsync(wrongPasswordEmail);
        await VerifyEmailAsync(CapturedToken(wrongPasswordEmail));

        string inactiveEmail = UniqueEmail();
        await RegisterAsync(inactiveEmail);
        await VerifyEmailAsync(CapturedToken(inactiveEmail));
        await SetUserStatusAsync(inactiveEmail, UserStatus.Inactive);

        HttpResponseMessage unknownResponse = await SignInAsync(UniqueEmail(), Password);
        HttpResponseMessage wrongPasswordResponse = await SignInAsync(wrongPasswordEmail, "incorrect password");
        HttpResponseMessage inactiveResponse = await SignInAsync(inactiveEmail, Password);

        foreach (HttpResponseMessage response in new[] { unknownResponse, wrongPasswordResponse, inactiveResponse })
        {
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            ApiProblem problem = await ReadProblemAsync(response);
            problem.Detail.Should().Be(SignInUserHandler.GenericCredentialError);
            problem.Code.Should().Be(IdentityProblemCodes.SignInInvalidCredentials);
            problem.Type.Should().Be(ProblemType(IdentityProblemCodes.SignInInvalidCredentials));
            response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task SignInUser_WhenAccountIsUnverified_ReturnsVerificationRequiredWithoutSession()
    {
        string email = UniqueEmail();
        await RegisterAsync(email);

        HttpResponseMessage response = await SignInAsync(email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ApiProblem problem = await ReadProblemAsync(response);
        problem.Detail.Should().Be(SignInUserHandler.VerificationRequiredError);
        problem.Code.Should().Be(IdentityProblemCodes.SignInVerificationRequired);
        problem.Type.Should().Be(ProblemType(IdentityProblemCodes.SignInVerificationRequired));
        response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse();
    }

    [Fact]
    public async Task SignInUser_WhenPersonalWorkspaceIsUnavailable_ReturnsAccountUnavailableWithoutSession()
    {
        string email = UniqueEmail();
        await RegisterAsync(email);
        await MarkUserEmailVerifiedWithoutActivatingWorkspaceAsync(email);

        HttpResponseMessage response = await SignInAsync(email, Password);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        ApiProblem problem = await ReadProblemAsync(response);
        problem.Detail.Should().Be(SignInUserHandler.AccountUnavailableError);
        problem.Code.Should().Be(IdentityProblemCodes.SignInAccountUnavailable);
        problem.Type.Should().Be(ProblemType(IdentityProblemCodes.SignInAccountUnavailable));
        response.Headers.TryGetValues("Set-Cookie", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Authorize_WhenSilentBrowserSessionIsAbsent_RedirectsWithLoginRequired()
    {
        string state = Guid.NewGuid().ToString("N");
        HttpResponseMessage signOutResponse =
            await fixture.Client.PostAsync("/api/auth/sign-out", content: null);

        HttpResponseMessage response = await AuthorizeAsync(prompt: "none", state);

        signOutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri location = response.Headers.Location!;
        location.AbsolutePath.Should().Be("/callback");
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query =
            QueryHelpers.ParseQuery(location.Query);
        query["error"].ToString().Should().Be("login_required");
        query["state"].ToString().Should().Be(state);
    }

    [Fact]
    public async Task SignOutUser_WhenBrowserSessionExistsOrIsAbsent_ClearsBrowserSessionWithoutIdentitySideEffects()
    {
        string email = UniqueEmail();
        await RegisterAsync(email);
        await VerifyEmailAsync(CapturedToken(email));
        (int userCountBefore, int workspaceCountBefore, int tokenCountBefore) = await CountRegistrationArtifactsAsync();

        HttpResponseMessage signInResponse = await SignInAsync(email, Password);
        signInResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        HttpResponseMessage authorizeBeforeSignOut = await AuthorizeAsync();
        authorizeBeforeSignOut.StatusCode.Should().Be(HttpStatusCode.Redirect);

        HttpResponseMessage signOutResponse = await fixture.Client.PostAsync("/api/auth/sign-out", content: null);

        signOutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        signOutResponse.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies).Should().BeTrue();
        cookies!.Should().Contain(cookie =>
            cookie.Contains(".AspNetCore.Cookies=;", StringComparison.Ordinal)
            && cookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        HttpResponseMessage authorizeAfterSignOut = await AuthorizeAsync();
        authorizeAfterSignOut.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        HttpResponseMessage absentSessionResponse = await fixture.Client.PostAsync("/api/auth/sign-out", content: null);
        absentSessionResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (int userCountAfter, int workspaceCountAfter, int tokenCountAfter) = await CountRegistrationArtifactsAsync();
        userCountAfter.Should().Be(userCountBefore);
        workspaceCountAfter.Should().Be(workspaceCountBefore);
        tokenCountAfter.Should().Be(tokenCountBefore);
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

    private async Task<HttpResponseMessage> SignInAsync(string email, string password) =>
        await fixture.Client.PostAsJsonAsync("/api/auth/sign-in", new { email, password }, Json);

    private async Task<HttpResponseMessage> AuthorizeAsync(string? prompt = null, string? state = null)
    {
        string verifier = CreateCodeVerifier();
        Dictionary<string, string?> authorizeQuery = new()
        {
            ["response_type"] = "code",
            ["client_id"] = "axis_spa",
            ["redirect_uri"] = "https://localhost/callback",
            ["code_challenge"] = CreateCodeChallenge(verifier),
            ["code_challenge_method"] = "S256",
            ["scope"] = "openid email profile",
            ["state"] = state ?? Guid.NewGuid().ToString("N"),
            ["prompt"] = prompt,
        };

        string authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", authorizeQuery);
        return await fixture.Client.GetAsync(authorizeUrl);
    }

    private async Task<(int UserCount, int WorkspaceCount, int TokenCount)> CountRegistrationArtifactsAsync()
    {
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return (
            await db.Users.CountAsync(),
            await db.Workspaces.CountAsync(),
            await db.EmailVerificationTokens.CountAsync());
    }

    private async Task SetUserStatusAsync(string email, UserStatus status)
    {
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Email normalizedEmail = Email.Create(email).Value;
        User user = await db.Users.SingleAsync(u => u.Email == normalizedEmail);
        db.Entry(user).Property(nameof(User.Status)).CurrentValue = status;
        await db.SaveChangesAsync();
    }

    private async Task MarkUserEmailVerifiedWithoutActivatingWorkspaceAsync(string email)
    {
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Email normalizedEmail = Email.Create(email).Value;
        User user = await db.Users.SingleAsync(u => u.Email == normalizedEmail);
        user.VerifyEmail();
        await db.SaveChangesAsync();
    }

    private string CapturedToken(string email) =>
        fixture.EmailCapture.GetVerificationToken(email)
        ?? throw new InvalidOperationException($"No verification token was captured for {email}.");

    private static async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage response)
    {
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return new ApiProblem(
            body.GetProperty("detail").GetString(),
            body.GetProperty("code").GetString(),
            body.GetProperty("type").GetString());
    }

    private static string ProblemType(string code) => $"urn:axis:problem:{code}";

    private static string[] ReadCodes(JsonElement errorCodes, string field) =>
        errorCodes.GetProperty(field).EnumerateArray().Select(code => code.GetString()!).ToArray();

    private sealed record ApiProblem(string? Detail, string? Code, string? Type);

    private static string CreateCodeVerifier() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeChallenge(string verifier)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static object ValidRegisterRequest(string email) => new
    {
        FullName = "Alice Smith",
        Email = email,
        Password,
        PasswordConfirmation = Password,
        AcceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
        AcceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
    };

    private static string UniqueEmail() => $"alice-{Guid.NewGuid():N}@example.com";
}
