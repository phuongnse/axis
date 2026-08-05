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
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

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
        cookies!.Should().Contain(cookie =>
            cookie.StartsWith("__Host-axis-session=", StringComparison.Ordinal)
            && cookie.Contains("; path=/", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("; secure", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("; httponly", StringComparison.OrdinalIgnoreCase)
            && cookie.Contains("; samesite=lax", StringComparison.OrdinalIgnoreCase));
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
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
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
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
            await fixture.PostBrowserAsync("/api/auth/sign-out", cancellationToken: TestContext.Current.CancellationToken);

        HttpResponseMessage cachedResponse = await AuthorizeAsync(prompt: "none", state);
        cachedResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri cachedLocation = ResolveLocation(cachedResponse);
        cachedLocation.AbsolutePath.Should().Be("/connect/authorize");
        string requestUri = ReadRequestUri(cachedLocation);

        HttpResponseMessage response = await fixture.Client.GetAsync(
            cachedLocation.PathAndQuery,
            TestContext.Current.CancellationToken);

        signOutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri location = ResolveLocation(response);
        location.AbsolutePath.Should().Be("/callback");
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query =
            QueryHelpers.ParseQuery(location.Query);
        query["error"].ToString().Should().Be("login_required");
        query["state"].ToString().Should().Be(state);
        requestUri.Should().NotContain("client_id");
    }

    [Fact]
    public async Task Authorize_WhenInteractiveBrowserSessionIsAbsent_RedirectsToSpaWithOpaqueRequestHandle()
    {
        HttpResponseMessage signOutResponse =
            await fixture.PostBrowserAsync("/api/auth/sign-out", cancellationToken: TestContext.Current.CancellationToken);

        HttpResponseMessage cachedResponse = await AuthorizeAsync();

        signOutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        cachedResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri cachedLocation = ResolveLocation(cachedResponse);
        cachedLocation.AbsolutePath.Should().Be("/connect/authorize");
        string requestUri = ReadRequestUri(cachedLocation);

        HttpResponseMessage response = await fixture.Client.GetAsync(
            cachedLocation.PathAndQuery,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri location = ResolveLocation(response);
        location.AbsoluteUri.Should().StartWith("https://localhost:3000/sign-in?");
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query =
            QueryHelpers.ParseQuery(location.Query);
        query.Keys.Should().BeEquivalentTo(["authorization_request"]);
        query["authorization_request"].ToString().Should().Be(requestUri);
    }

    [Fact]
    public async Task Authorize_WhenInteractiveMcpRequestResumesThroughSpaProxy_UsesRegisteredLoopbackCallback()
    {
        string email = UniqueEmail();
        await RegisterAsync(email);
        await VerifyEmailAsync(CapturedToken(email));
        HttpResponseMessage signOutResponse =
            await fixture.PostBrowserAsync("/api/auth/sign-out", cancellationToken: TestContext.Current.CancellationToken);

        string state = Guid.NewGuid().ToString("N");
        HttpResponseMessage cachedResponse = await AuthorizeAsync(
            state: state,
            clientId: "axis_mcp",
            redirectUri: "http://127.0.0.1:48123/callback",
            host: "localhost:5281");

        signOutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        cachedResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri cachedLocation = ResolveLocation(cachedResponse);
        string requestUri = ReadRequestUri(cachedLocation);
        requestUri.Should().NotBeNullOrWhiteSpace();

        HttpResponseMessage signInRedirect = await GetWithHostAsync(
            cachedLocation.PathAndQuery,
            "localhost:5281");

        signInRedirect.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri signInLocation = ResolveLocation(signInRedirect);
        signInLocation.AbsoluteUri.Should().StartWith("https://localhost:3000/sign-in?");
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> signInQuery =
            QueryHelpers.ParseQuery(signInLocation.Query);
        signInQuery.Keys.Should().BeEquivalentTo(["authorization_request"]);
        signInQuery["authorization_request"].ToString().Should().Be(requestUri);

        HttpResponseMessage signInResponse = await SignInAsync(email, Password);
        signInResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage callbackResponse = await GetWithHostAsync(
            cachedLocation.PathAndQuery,
            "web:3000");

        callbackResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri callbackLocation = ResolveLocation(callbackResponse);
        callbackLocation.AbsoluteUri.Should().StartWith("http://127.0.0.1:48123/callback?");
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query =
            QueryHelpers.ParseQuery(callbackLocation.Query);
        query["code"].ToString().Should().NotBeNullOrWhiteSpace();
        query["state"].ToString().Should().Be(state);

        HttpResponseMessage replayResponse = await GetWithHostAsync(
            cachedLocation.PathAndQuery,
            "web:3000");

        replayResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        replayResponse.Headers.Location.Should().BeNull();
    }

    [Fact]
    public async Task Authorize_WhenAuthorizationHandleIsMissingOrTampered_FailsClosedWithoutRedirect()
    {
        foreach (string path in new[]
        {
            "/connect/authorize",
            "/connect/authorize?request_uri=urn%3Aietf%3Aparams%3Aoauth%3Arequest_uri%3Atampered",
        })
        {
            HttpResponseMessage response = await fixture.Client.GetAsync(
                path,
                TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            response.Headers.Location.Should().BeNull();
        }
    }

    [Fact]
    public async Task Authorize_WhenRequestTokenExpires_FailsClosedWithoutCallbackCode()
    {
        HttpResponseMessage cachedResponse = await AuthorizeAsync();
        cachedResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri cachedLocation = ResolveLocation(cachedResponse);
        string requestUri = ReadRequestUri(cachedLocation);

        using IServiceScope scope = fixture.CreateScope();
        IOpenIddictTokenManager manager = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
        string referenceId = requestUri[RequestUris.Prefixes.Generic.Length..];
        object requestToken = await manager.FindByReferenceIdAsync(
            referenceId,
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException($"No request token found for `{requestUri}`.");
        DateTimeOffset creationDate = (await manager.GetCreationDateAsync(
            requestToken,
            TestContext.Current.CancellationToken))!.Value;
        DateTimeOffset expirationDate = (await manager.GetExpirationDateAsync(
            requestToken,
            TestContext.Current.CancellationToken))!.Value;
        (expirationDate - creationDate).Should().Be(TimeSpan.FromMinutes(5));
        OpenIddictTokenDescriptor descriptor = new();
        await manager.PopulateAsync(
            descriptor,
            requestToken,
            TestContext.Current.CancellationToken);
        descriptor.ExpirationDate = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1);
        await manager.UpdateAsync(
            requestToken,
            descriptor,
            TestContext.Current.CancellationToken);

        HttpResponseMessage response = await fixture.Client.GetAsync(
            cachedLocation.PathAndQuery,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Headers.Location.Should().BeNull();
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
        string authorizeState = Guid.NewGuid().ToString("N");
        HttpResponseMessage authorizeBeforeSignOut = await AuthorizeAsync(state: authorizeState);
        authorizeBeforeSignOut.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri cachedBeforeSignOut = ResolveLocation(authorizeBeforeSignOut);
        cachedBeforeSignOut.AbsolutePath.Should().Be("/connect/authorize");
        ReadRequestUri(cachedBeforeSignOut).Should().NotBeNullOrWhiteSpace();
        HttpResponseMessage authorizeCallback = await fixture.Client.GetAsync(
            cachedBeforeSignOut.PathAndQuery,
            TestContext.Current.CancellationToken);
        authorizeCallback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri callbackLocation = ResolveLocation(authorizeCallback);
        callbackLocation.AbsolutePath.Should().Be("/callback");
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> callbackQuery =
            QueryHelpers.ParseQuery(callbackLocation.Query);
        callbackQuery["code"].ToString().Should().NotBeNullOrWhiteSpace();
        callbackQuery["state"].ToString().Should().Be(authorizeState);

        HttpResponseMessage signOutResponse = await fixture.PostBrowserAsync("/api/auth/sign-out", cancellationToken: TestContext.Current.CancellationToken);

        signOutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        signOutResponse.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies).Should().BeTrue();
        cookies!.Should().Contain(cookie =>
            cookie.Contains("__Host-axis-session=;", StringComparison.Ordinal)
            && cookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
        HttpResponseMessage authorizeAfterSignOut = await AuthorizeAsync();
        authorizeAfterSignOut.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri cachedLocation = ResolveLocation(authorizeAfterSignOut);
        cachedLocation.AbsolutePath.Should().Be("/connect/authorize");

        HttpResponseMessage signInRedirect = await fixture.Client.GetAsync(
            cachedLocation.PathAndQuery,
            TestContext.Current.CancellationToken);
        signInRedirect.StatusCode.Should().Be(HttpStatusCode.Redirect);
        ResolveLocation(signInRedirect).AbsolutePath.Should().Be("/sign-in");

        HttpResponseMessage absentSessionResponse = await fixture.PostBrowserAsync("/api/auth/sign-out", cancellationToken: TestContext.Current.CancellationToken);
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

        return await fixture.SendBrowserMutationAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> VerifyEmailAsync(string token) =>
        await fixture.PostBrowserJsonAsync(
            "/api/auth/verify-email",
            new { token },
            TestContext.Current.CancellationToken);

    private async Task<HttpResponseMessage> SignInAsync(string email, string password) =>
        await fixture.PostBrowserJsonAsync(
            "/api/auth/sign-in",
            new { email, password },
            TestContext.Current.CancellationToken);

    private async Task<HttpResponseMessage> AuthorizeAsync(
        string? prompt = null,
        string? state = null,
        string clientId = "axis_mcp",
        string? redirectUri = null,
        string? host = null)
    {
        string verifier = CreateCodeVerifier();
        Dictionary<string, string?> authorizeQuery = new()
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri ?? "http://127.0.0.1:48123/callback",
            ["code_challenge"] = CreateCodeChallenge(verifier),
            ["code_challenge_method"] = "S256",
            ["scope"] = "openid email profile",
            ["state"] = state ?? Guid.NewGuid().ToString("N"),
            ["prompt"] = prompt,
        };

        string authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", authorizeQuery);
        if (host is null)
        {
            return await fixture.Client.GetAsync(
                authorizeUrl,
                TestContext.Current.CancellationToken);
        }

        return await GetWithHostAsync(authorizeUrl, host);
    }

    private async Task<HttpResponseMessage> GetWithHostAsync(string path, string host)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        request.Headers.Host = host;
        return await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Uri ResolveLocation(HttpResponseMessage response)
    {
        Uri location = response.Headers.Location!;
        return location.IsAbsoluteUri
            ? location
            : new Uri(new Uri("https://localhost"), location);
    }

    private static string ReadRequestUri(Uri location)
    {
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query =
            QueryHelpers.ParseQuery(location.Query);
        query.TryGetValue("request_uri", out Microsoft.Extensions.Primitives.StringValues requestUriValue)
            .Should().BeTrue($"authorization request-token location was `{location}`");
        string requestUri = requestUriValue.ToString();
        requestUri.Should().StartWith(RequestUris.Prefixes.Generic);
        return requestUri;
    }

    private async Task<(int UserCount, int WorkspaceCount, int TokenCount)> CountRegistrationArtifactsAsync()
    {
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return (
            await db.Users.CountAsync(TestContext.Current.CancellationToken),
            await db.Workspaces.CountAsync(TestContext.Current.CancellationToken),
            await db.EmailVerificationTokens.CountAsync(TestContext.Current.CancellationToken));
    }

    private async Task SetUserStatusAsync(string email, UserStatus status)
    {
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Email normalizedEmail = Email.Create(email).Value;
        User user = await db.Users.SingleAsync(
            u => u.Email == normalizedEmail,
            TestContext.Current.CancellationToken);
        db.Entry(user).Property(nameof(User.Status)).CurrentValue = status;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task MarkUserEmailVerifiedWithoutActivatingWorkspaceAsync(string email)
    {
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Email normalizedEmail = Email.Create(email).Value;
        User user = await db.Users.SingleAsync(
            u => u.Email == normalizedEmail,
            TestContext.Current.CancellationToken);
        user.VerifyEmail();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private string CapturedToken(string email) =>
        fixture.EmailCapture.GetVerificationToken(email)
        ?? throw new InvalidOperationException($"No verification token was captured for {email}.");

    private static async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage response)
    {
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
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
