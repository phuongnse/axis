using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Axis.Api.Tests.Administration;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public sealed class ServiceIdentityEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task ServiceIdentityLifecycle_WhenAdministrator_ListsNonSecretCanonicalState()
    {
        await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        string clientId = $"service-{Guid.NewGuid():N}";

        HttpResponseMessage created = await fixture.PostBrowserJsonAsync(
            "/api/service-identities",
            new { clientId },
            TestContext.Current.CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement createdBody = await ReadJsonAsync(created);
        Guid identityId = createdBody.GetProperty("id").GetGuid();
        createdBody.GetProperty("clientId").GetString().Should().Be(clientId);
        createdBody.TryGetProperty("clientSecret", out _).Should().BeFalse();
        createdBody.TryGetProperty("privateKey", out _).Should().BeFalse();
        JsonElement createdMetadata = createdBody.GetProperty("metadata");
        createdMetadata.GetProperty("createdBy").GetProperty("displayName").GetString()
            .Should().Be("Workspace Administrator");
        createdMetadata.GetProperty("modifiedBy").GetProperty("displayName").GetString()
            .Should().Be("Workspace Administrator");

        HttpResponseMessage listed = await fixture.Client.GetAsync(
            "/api/service-identities",
            TestContext.Current.CancellationToken);
        listed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(listed)).EnumerateArray()
            .Should().Contain(value =>
                value.GetProperty("id").GetGuid() == identityId &&
                value.GetProperty("clientId").GetString() == clientId);

        HttpResponseMessage invalidKey = await fixture.PostBrowserJsonAsync(
            $"/api/service-identities/{identityId}/keys",
            new { expectedRevision = 1, publicJwk = "not-a-jwk" },
            TestContext.Current.CancellationToken);
        invalidKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        HttpResponseMessage revoked = await fixture.PostBrowserJsonAsync(
            $"/api/service-identities/{identityId}/revoke",
            new { expectedRevision = 1 },
            TestContext.Current.CancellationToken);
        revoked.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement revokedBody = await ReadJsonAsync(revoked);
        revokedBody.GetProperty("status").GetString().Should().Be("Revoked");
        revokedBody.GetProperty("workspaceGrantStatus").GetString().Should().Be("Revoked");
        revokedBody.GetProperty("metadata").GetProperty("modifiedBy").GetProperty("displayName").GetString()
            .Should().Be("Workspace Administrator");
    }

    [Fact]
    public async Task ServiceIdentityEndpoints_WhenAnonymous_ReturnUnauthorized()
    {
        using HttpClient anonymous = fixture.CreateAnonymousClient();

        (await anonymous.GetAsync(
            "/api/service-identities",
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ServiceIdentityInput_WhenInvalidOrCrossWorkspace_DeniesWithoutDisclosure()
    {
        await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        HttpResponseMessage created = await fixture.PostBrowserJsonAsync(
            "/api/service-identities",
            new { clientId = $"service-{Guid.NewGuid():N}" },
            TestContext.Current.CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement identity = await ReadJsonAsync(created);
        Guid identityId = identity.GetProperty("id").GetGuid();

        using ECDsa first = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        HttpResponseMessage privateKey = await fixture.PostBrowserJsonAsync(
            $"/api/service-identities/{identityId}/keys",
            new { expectedRevision = 1, publicJwk = PublicJwk(first, "release-key", includePrivate: true) },
            TestContext.Current.CancellationToken);
        privateKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using ECDsa nonEs256 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        HttpResponseMessage unsupportedCurve = await fixture.PostBrowserJsonAsync(
            $"/api/service-identities/{identityId}/keys",
            new { expectedRevision = 1, publicJwk = PublicJwk(nonEs256, "release-key") },
            TestContext.Current.CancellationToken);
        unsupportedCurve.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        HttpResponseMessage added = await fixture.PostBrowserJsonAsync(
            $"/api/service-identities/{identityId}/keys",
            new { expectedRevision = 1, publicJwk = PublicJwk(first, "release-key") },
            TestContext.Current.CancellationToken);
        added.StatusCode.Should().Be(HttpStatusCode.OK);

        using ECDsa second = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        HttpResponseMessage duplicateKid = await fixture.PostBrowserJsonAsync(
            $"/api/service-identities/{identityId}/keys",
            new { expectedRevision = 2, publicJwk = PublicJwk(second, "release-key") },
            TestContext.Current.CancellationToken);
        duplicateKid.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        HttpResponseMessage foreign = await fixture.Client.GetAsync(
            $"/api/service-identities/{identityId}",
            TestContext.Current.CancellationToken);
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Contains(identityId.ToString(), StringComparison.OrdinalIgnoreCase).Should().BeFalse();
    }

    [Fact]
    public async Task ServiceIdentityCreate_WhenPersonalOwner_PersistsWorkspaceScopedIdentity()
    {
        PersonalWorkspaceOwnerApiTestSession.OwnerContext owner =
            await PersonalWorkspaceOwnerApiTestSession.CreateAsync(fixture);
        string clientId = $"personal-{Guid.NewGuid():N}";

        HttpResponseMessage created = await fixture.PostBrowserJsonAsync(
            "/api/service-identities",
            new { clientId },
            TestContext.Current.CancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await ReadJsonAsync(created);
        body.GetProperty("workspaceId").GetGuid().Should().Be(owner.WorkspaceId);

        HttpResponseMessage listed = await fixture.Client.GetAsync(
            "/api/service-identities",
            TestContext.Current.CancellationToken);
        listed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(listed)).EnumerateArray()
            .Should().Contain(value => value.GetProperty("clientId").GetString() == clientId);

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await db.ServiceIdentities.AnyAsync(
            value => value.ClientId == clientId && value.WorkspaceId == owner.WorkspaceId,
            TestContext.Current.CancellationToken)).Should().BeTrue();
        (await db.IdentityAuditOutboxRecords.AnyAsync(
            value => value.Action == "service_identity.create_denied"
                && value.ActorId == owner.UserId,
            TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    private static string PublicJwk(ECDsa key, string kid, bool includePrivate = false)
    {
        ECParameters parameters = key.ExportParameters(includePrivate);
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = parameters.Q.X!.Length == 48 ? "P-384" : "P-256",
            ["kid"] = kid,
            ["x"] = Base64Url(parameters.Q.X!),
            ["y"] = Base64Url(parameters.Q.Y!),
            ["d"] = includePrivate ? Base64Url(parameters.D!) : string.Empty,
        }.Where(pair => pair.Value.Length > 0).ToDictionary());
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
}
