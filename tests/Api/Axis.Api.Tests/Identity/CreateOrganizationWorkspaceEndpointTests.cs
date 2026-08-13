using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Application;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Legal;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public sealed class CreateOrganizationWorkspaceEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;
    private const string Password = "maple river sunrise";

    [Fact]
    public async Task CreateOrganizationWorkspace_WhenAuthenticated_CreatesCanonicalGraph()
    {
        string email = UniqueEmail();
        await CreateVerifiedBrowserSessionAsync(email);
        string idempotencyKey = Guid.NewGuid().ToString("N");

        HttpResponseMessage first = await CreateAsync("  Acme Operations  ", idempotencyKey);
        HttpResponseMessage retry = await CreateAsync("Acme Operations", idempotencyKey);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        retry.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement firstBody = await ReadJsonAsync(first);
        JsonElement retryBody = await ReadJsonAsync(retry);
        firstBody.GetProperty("organizationName").GetString().Should().Be("Acme Operations");
        firstBody.GetProperty("workspaceName").GetString().Should().Be("Acme Operations");
        retryBody.GetProperty("organizationId").GetGuid()
            .Should().Be(firstBody.GetProperty("organizationId").GetGuid());
        retryBody.GetProperty("workspaceId").GetGuid()
            .Should().Be(firstBody.GetProperty("workspaceId").GetGuid());

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        User user = await UserAsync(db, email);
        Guid organizationId = firstBody.GetProperty("organizationId").GetGuid();
        Guid workspaceId = firstBody.GetProperty("workspaceId").GetGuid();
        (await db.Organizations.CountAsync(
            organization => organization.Id == organizationId,
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await db.OrganizationMemberships.CountAsync(
            membership => membership.OrganizationId == organizationId
                && membership.UserId == user.Id
                && membership.Role == OrganizationMembershipRole.Owner,
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await db.Workspaces.CountAsync(
            workspace => workspace.Id == workspaceId
                && workspace.OrganizationId == organizationId,
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await db.WorkspaceMemberships.CountAsync(
            membership => membership.WorkspaceId == workspaceId
                && membership.UserId == user.Id
                && membership.Role == WorkspaceMembershipRole.Administrator
                && membership.IsProductBuilder,
            TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task CreateOrganizationWorkspace_WhenNameIsInvalid_ReturnsFieldLocalProblemWithoutMutation()
    {
        string email = UniqueEmail();
        await CreateVerifiedBrowserSessionAsync(email);
        using IServiceScope beforeScope = fixture.CreateScope();
        IdentityDbContext beforeDb = beforeScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        int organizationCountBefore = await beforeDb.Organizations.CountAsync(
            TestContext.Current.CancellationToken);

        HttpResponseMessage response = await CreateAsync(" ", Guid.NewGuid().ToString("N"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("errors").TryGetProperty("name", out _).Should().BeTrue();
        body.GetProperty("errorCodes").GetProperty("name")
            .EnumerateArray().Select(code => code.GetString())
            .Should().Contain(IdentityProblemCodes.CreateOrganizationNameRequired);
        using IServiceScope afterScope = fixture.CreateScope();
        IdentityDbContext afterDb = afterScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await afterDb.Organizations.CountAsync(TestContext.Current.CancellationToken))
            .Should().Be(organizationCountBefore);
    }

    [Fact]
    public async Task CreateOrganizationWorkspace_WhenIdempotencyKeyIsReusedForDifferentName_ReturnsConflict()
    {
        await CreateVerifiedBrowserSessionAsync(UniqueEmail());
        string idempotencyKey = Guid.NewGuid().ToString("N");
        (await CreateAsync("Acme Operations", idempotencyKey)).StatusCode
            .Should().Be(HttpStatusCode.Created);

        HttpResponseMessage response = await CreateAsync("Acme Finance", idempotencyKey);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateOrganizationWorkspace_WhenIdempotencyKeyIsMissing_ReturnsBadRequest()
    {
        await CreateVerifiedBrowserSessionAsync(UniqueEmail());
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/organizations")
        {
            Content = JsonContent.Create(new { name = "Acme" }, options: Json),
        };

        HttpResponseMessage response = await fixture.SendBrowserMutationAsync(
            request,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("code").GetString().Should().Be("common.invalidInput");
        body.GetProperty("type").GetString().Should().Be("urn:axis:problem:common.invalidInput");
    }

    [Fact]
    public async Task CreateOrganizationWorkspace_WhenUnauthenticated_ReturnsUnauthorized()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/organizations")
        {
            Content = JsonContent.Create(new { name = "Acme" }, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        HttpResponseMessage response = await fixture.CreateAnonymousClient().SendAsync(
            request,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> CreateAsync(string name, string idempotencyKey)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/organizations")
        {
            Content = JsonContent.Create(new { name }, options: Json),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString("N"));
        return await fixture.SendBrowserMutationAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task CreateVerifiedBrowserSessionAsync(string email)
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
        (await fixture.PostBrowserJsonAsync(
            "/api/auth/verify-email",
            new { token },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<User> UserAsync(IdentityDbContext db, string email)
    {
        Email normalized = Email.Create(email).Value;
        return await db.Users.SingleAsync(
            user => user.Email == normalized,
            TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);

    private static string UniqueEmail() => $"organization-{Guid.NewGuid():N}@example.com";
}
