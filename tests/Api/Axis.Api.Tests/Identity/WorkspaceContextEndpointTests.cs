using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Audit.Infrastructure.Persistence;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public sealed class WorkspaceContextEndpointTests(ApiTestFixture fixture)
{
    private const string Password = "maple river sunrise";
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task SwitchWorkspace_WhenConfirmed_RotatesAuthorityInBothDirections()
    {
        await CreateVerifiedBrowserSessionAsync(UniqueEmail());
        JsonElement initialSession = await fixture.RefreshBrowserSecurityContextAsync(
            TestContext.Current.CancellationToken);
        Guid personalWorkspaceId = initialSession.GetProperty("user")
            .GetProperty("workspaceId")
            .GetGuid();
        Guid organizationWorkspaceId = await CreateOrganizationWorkspaceAsync();

        JsonElement eligible = await fixture.Client.GetFromJsonAsync<JsonElement>(
            "/api/workspace-context/eligible",
            Json,
            TestContext.Current.CancellationToken);
        eligible.EnumerateArray().Select(item => item.GetProperty("workspaceId").GetGuid())
            .Should().BeEquivalentTo([personalWorkspaceId, organizationWorkspaceId]);

        HttpResponseMessage begin = await fixture.PostBrowserJsonAsync(
            "/api/workspace-context/begin",
            new { targetWorkspaceId = organizationWorkspaceId },
            TestContext.Current.CancellationToken);
        begin.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement beginBody = await begin.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        beginBody.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            ["transitionId", "status", "expiresAt", "authoritativeWorkspaceId"]);

        using HttpRequestMessage blocked = new(HttpMethod.Post, "/api/organizations")
        {
            Content = JsonContent.Create(new { name = "Blocked During Transition" }, options: Json),
        };
        blocked.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        (await fixture.SendBrowserMutationAsync(blocked, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await fixture.PostBrowserAsync(
            "/api/workspace-context/confirm",
            cancellationToken: TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await CurrentWorkspaceIdAsync()).Should().Be(organizationWorkspaceId);

        (await fixture.PostBrowserJsonAsync(
            "/api/workspace-context/begin",
            new { targetWorkspaceId = personalWorkspaceId },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.PostBrowserAsync(
            "/api/workspace-context/confirm",
            cancellationToken: TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await CurrentWorkspaceIdAsync()).Should().Be(personalWorkspaceId);
        (await WaitForTransitionAuditCountAsync(4)).Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task RecoverWorkspace_WhenTargetWasOnlyStaged_CompensatesAndRotatesSourceSession()
    {
        await CreateVerifiedBrowserSessionAsync(UniqueEmail());
        Guid sourceWorkspaceId = await CurrentWorkspaceIdAsync();
        Guid targetWorkspaceId = await CreateOrganizationWorkspaceAsync();
        (await fixture.PostBrowserJsonAsync(
            "/api/workspace-context/begin",
            new { targetWorkspaceId },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage recovery = await fixture.PostBrowserAsync(
            "/api/workspace-context/recover",
            cancellationToken: TestContext.Current.CancellationToken);

        recovery.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await recovery.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        body.GetProperty("status").GetString().Should().Be("Compensated");
        (await CurrentWorkspaceIdAsync()).Should().Be(sourceWorkspaceId);
        (await fixture.PostBrowserJsonAsync(
            "/api/workspace-context/begin",
            new { targetWorkspaceId },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RecoverWorkspace_WhenCompletionResponseWasLost_RestoresCompletedTargetWithoutSourceAuthority()
    {
        string sourceSessionCookie = await CreateVerifiedBrowserSessionAsync(UniqueEmail());
        Guid targetWorkspaceId = await CreateOrganizationWorkspaceAsync();
        using HttpClient sourceBrowser = fixture.CreateRawClient();
        sourceBrowser.DefaultRequestHeaders.Add("Cookie", sourceSessionCookie);
        HttpResponseMessage sourceSessionResponse = await sourceBrowser.GetAsync(
            "/api/auth/session",
            TestContext.Current.CancellationToken);
        JsonElement sourceSession = await sourceSessionResponse.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        string antiforgeryCookie = ReadCookie(sourceSessionResponse, "__Host-axis-antiforgery");
        string csrf = sourceSession.GetProperty("csrfToken").GetString()!;

        using HttpRequestMessage beginRequest = new(HttpMethod.Post, "/api/workspace-context/begin")
        {
            Content = JsonContent.Create(new { targetWorkspaceId }, options: Json),
        };
        beginRequest.Headers.Add("Cookie", $"{sourceSessionCookie}; {antiforgeryCookie}");
        beginRequest.Headers.Add("X-CSRF-TOKEN", csrf);
        HttpResponseMessage beginResponse = await sourceBrowser.SendAsync(
            beginRequest,
            TestContext.Current.CancellationToken);
        beginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        string transitionCookie = ReadCookie(
            beginResponse,
            "__Host-axis-workspace-transition");
        string preCompletionCookies =
            $"{sourceSessionCookie}; {antiforgeryCookie}; {transitionCookie}";

        using HttpClient completingServer = fixture.CreateRawClient();
        completingServer.DefaultRequestHeaders.Add("Cookie", preCompletionCookies);
        completingServer.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        HttpResponseMessage completionResponse = await completingServer.PostAsync(
            "/api/workspace-context/confirm",
            content: null,
            TestContext.Current.CancellationToken);
        completionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        ReadSessionCookie(completionResponse).Should().NotBe(sourceSessionCookie);

        using HttpClient responseLostBrowser = fixture.CreateRawClient();
        responseLostBrowser.DefaultRequestHeaders.Add("Cookie", preCompletionCookies);
        responseLostBrowser.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf);
        JsonElement staleSession = await responseLostBrowser.GetFromJsonAsync<JsonElement>(
            "/api/auth/session",
            Json,
            TestContext.Current.CancellationToken);
        staleSession.GetProperty("user").ValueKind.Should().Be(JsonValueKind.Null);
        (await responseLostBrowser.GetAsync(
            "/api/rules?page=1&pageSize=20",
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        HttpResponseMessage recovered = await responseLostBrowser.PostAsync(
            "/api/workspace-context/recover",
            content: null,
            TestContext.Current.CancellationToken);

        recovered.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement recovery = await recovered.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        recovery.GetProperty("status").GetString().Should().Be("Completed");
        recovery.GetProperty("authoritativeWorkspaceId").GetGuid().Should().Be(targetWorkspaceId);
        string recoveredSessionCookie = ReadSessionCookie(recovered);
        using HttpClient recoveredBrowser = fixture.CreateRawClient();
        recoveredBrowser.DefaultRequestHeaders.Add("Cookie", recoveredSessionCookie);
        JsonElement session = await recoveredBrowser.GetFromJsonAsync<JsonElement>(
            "/api/auth/session",
            Json,
            TestContext.Current.CancellationToken);
        session.GetProperty("user").GetProperty("workspaceId").GetGuid()
            .Should().Be(targetWorkspaceId);
    }

    [Fact]
    public async Task SwitchWorkspace_WhenTargetIsUnknown_PreservesSourceWithoutStagingAuthority()
    {
        await CreateVerifiedBrowserSessionAsync(UniqueEmail());
        Guid sourceWorkspaceId = await CurrentWorkspaceIdAsync();

        HttpResponseMessage begin = await fixture.PostBrowserJsonAsync(
            "/api/workspace-context/begin",
            new { targetWorkspaceId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);

        begin.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CurrentWorkspaceIdAsync()).Should().Be(sourceWorkspaceId);
        (await fixture.PostBrowserAsync(
            "/api/workspace-context/confirm",
            cancellationToken: TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SwitchWorkspace_WhenCurrentMembershipWasRevoked_DeniesDataAndAllowsEligibleRecovery()
    {
        string email = UniqueEmail();
        await CreateVerifiedBrowserSessionAsync(email);
        Guid personalWorkspaceId = await CurrentWorkspaceIdAsync();
        Guid organizationWorkspaceId = await CreateOrganizationWorkspaceAsync();
        (await fixture.PostBrowserJsonAsync(
            "/api/workspace-context/begin",
            new { targetWorkspaceId = organizationWorkspaceId },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.PostBrowserAsync(
            "/api/workspace-context/confirm",
            cancellationToken: TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.OK);

        using (IServiceScope scope = fixture.CreateScope())
        {
            IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            User user = await UserAsync(db, email);
            WorkspaceMembership membership = await db.WorkspaceMemberships.SingleAsync(
                candidate => candidate.UserId == user.Id
                    && candidate.WorkspaceId == organizationWorkspaceId,
                TestContext.Current.CancellationToken);
            membership.Remove(membership.Revision);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using HttpRequestMessage denied = new(HttpMethod.Post, "/api/organizations")
        {
            Content = JsonContent.Create(new { name = "Denied From Stale Context" }, options: Json),
        };
        denied.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        (await fixture.SendBrowserMutationAsync(denied, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        JsonElement eligible = await fixture.Client.GetFromJsonAsync<JsonElement>(
            "/api/workspace-context/eligible",
            Json,
            TestContext.Current.CancellationToken);
        eligible.EnumerateArray().Select(item => item.GetProperty("workspaceId").GetGuid())
            .Should().Equal(personalWorkspaceId);

        (await fixture.PostBrowserJsonAsync(
            "/api/workspace-context/begin",
            new { targetWorkspaceId = personalWorkspaceId },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.PostBrowserAsync(
            "/api/workspace-context/confirm",
            cancellationToken: TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await CurrentWorkspaceIdAsync()).Should().Be(personalWorkspaceId);
    }

    [Fact]
    public async Task WorkspaceOperation_WhenBearerMembershipWasRevoked_RevalidatesAndDeniesAccess()
    {
        string email = UniqueEmail();
        await CreateVerifiedBrowserSessionAsync(email);
        Guid organizationWorkspaceId = await CreateOrganizationWorkspaceAsync();
        (await fixture.PostBrowserJsonAsync(
            "/api/workspace-context/begin",
            new { targetWorkspaceId = organizationWorkspaceId },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.PostBrowserAsync(
            "/api/workspace-context/confirm",
            cancellationToken: TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.OK);
        string accessToken = await IssueAccessTokenForCurrentBrowserSessionAsync();

        using (IServiceScope scope = fixture.CreateScope())
        {
            IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            User user = await UserAsync(db, email);
            WorkspaceMembership membership = await db.WorkspaceMemberships.SingleAsync(
                candidate => candidate.UserId == user.Id
                    && candidate.WorkspaceId == organizationWorkspaceId,
                TestContext.Current.CancellationToken);
            membership.Remove(membership.Revision);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using HttpRequestMessage request = new(
            HttpMethod.Get,
            "/api/rules?page=1&pageSize=20");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        (await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> CreateOrganizationWorkspaceAsync()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/organizations")
        {
            Content = JsonContent.Create(new { name = $"Organization {Guid.NewGuid():N}" }, options: Json),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        HttpResponseMessage response = await fixture.SendBrowserMutationAsync(
            request,
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        return body.GetProperty("workspaceId").GetGuid();
    }

    private async Task<Guid> CurrentWorkspaceIdAsync()
    {
        JsonElement session = await fixture.RefreshBrowserSecurityContextAsync(
            TestContext.Current.CancellationToken);
        return session.GetProperty("user").GetProperty("workspaceId").GetGuid();
    }

    private async Task<string> IssueAccessTokenForCurrentBrowserSessionAsync()
    {
        string verifier = CreateCodeVerifier();
        string state = Guid.NewGuid().ToString("N");
        string authorizeUrl = QueryHelpers.AddQueryString(
            "/connect/authorize",
            new Dictionary<string, string?>
            {
                ["response_type"] = "code",
                ["client_id"] = "axis_mcp",
                ["redirect_uri"] = "http://127.0.0.1:48123/callback",
                ["code_challenge"] = CreateCodeChallenge(verifier),
                ["code_challenge_method"] = "S256",
                ["scope"] = "openid email profile",
                ["state"] = state,
            });

        HttpResponseMessage authorize = await fixture.Client.GetAsync(
            authorizeUrl,
            TestContext.Current.CancellationToken);
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri redirect = ResolveLocation(authorize);
        if (redirect.AbsolutePath == "/connect/authorize")
        {
            authorize = await fixture.Client.GetAsync(
                redirect.PathAndQuery,
                TestContext.Current.CancellationToken);
            authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
            redirect = ResolveLocation(authorize);
        }

        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> callback =
            QueryHelpers.ParseQuery(redirect.Query);
        callback["state"].ToString().Should().Be(state);
        string code = callback["code"].ToString();
        code.Should().NotBeNullOrWhiteSpace();
        using FormUrlEncodedContent tokenRequest = new(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "axis_mcp",
            ["redirect_uri"] = "http://127.0.0.1:48123/callback",
            ["code"] = code,
            ["code_verifier"] = verifier,
        });
        HttpResponseMessage tokenResponse = await fixture.Client.PostAsync(
            "/connect/token",
            tokenRequest,
            TestContext.Current.CancellationToken);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    private async Task<int> WaitForTransitionAuditCountAsync(int expected)
    {
        int count = 0;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            using IServiceScope scope = fixture.CreateScope();
            AuditDbContext audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            count = await audit.AuditRecords.CountAsync(
                record => record.Action == "workspace.context.transition",
                TestContext.Current.CancellationToken);
            if (count >= expected)
                return count;

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return count;
    }

    private async Task<string> CreateVerifiedBrowserSessionAsync(string email)
    {
        using HttpRequestMessage register = new(HttpMethod.Post, "/api/users/register")
        {
            Content = JsonContent.Create(new
            {
                fullName = "Alice Smith",
                email,
                password = Password,
                passwordConfirmation = Password,
                acceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
                acceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
            }, options: Json),
        };
        register.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        (await fixture.SendBrowserMutationAsync(register, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        string token = fixture.EmailCapture.GetVerificationToken(email)
            ?? throw new InvalidOperationException("Verification token was not captured.");
        HttpResponseMessage verified = await fixture.PostBrowserJsonAsync(
            "/api/auth/verify-email",
            new { token },
            TestContext.Current.CancellationToken);
        verified.StatusCode.Should().Be(HttpStatusCode.OK);
        return ReadSessionCookie(verified);
    }

    private static async Task<User> UserAsync(IdentityDbContext db, string email)
    {
        Email normalized = Email.Create(email).Value;
        return await db.Users.SingleAsync(
            user => user.Email == normalized,
            TestContext.Current.CancellationToken);
    }

    private static string ReadSessionCookie(HttpResponseMessage response)
        => ReadCookie(response, "__Host-axis-session");

    private static string ReadCookie(HttpResponseMessage response, string name)
    {
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies)
            .Should().BeTrue();
        return cookies!
            .Last(cookie => cookie.StartsWith(name + "=", StringComparison.Ordinal))
            .Split(';', 2)[0];
    }

    private static Uri ResolveLocation(HttpResponseMessage response)
    {
        Uri location = response.Headers.Location
            ?? throw new InvalidOperationException("Authorization response did not include a redirect.");
        return location.IsAbsoluteUri
            ? location
            : new Uri(new Uri("https://localhost"), location);
    }

    private static string CreateCodeVerifier() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeChallenge(string verifier) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string UniqueEmail() => $"workspace-switch-{Guid.NewGuid():N}@example.com";
}
