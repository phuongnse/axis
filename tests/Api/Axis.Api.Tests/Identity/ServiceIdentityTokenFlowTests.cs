using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Authorization.Contracts;
using Axis.Authorization.Infrastructure.Persistence;
using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Contracts;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Infrastructure.Persistence;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using BusinessObjectRecordId = Axis.BusinessObjects.Domain.ValueObjects.BusinessObjectRecordId;

namespace Axis.Api.Tests.Identity;

[Collection("Api")]
public sealed class ServiceIdentityTokenFlowTests(ApiTestFixture fixture)
{
    private const string AssertionType =
        "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
    private const string TokenAudience = "https://localhost:5281/connect/token";

    [Theory]
    [InlineData("key")]
    [InlineData("identity")]
    public async Task ServiceToken_WhenAuthorityRevoked_DeniesImmediately(string authority)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string clientId = $"service-{Guid.NewGuid():N}";
        ServiceIdentity serviceIdentity = await SeedServiceIdentityAsync(
            key,
            clientId,
            cancellationToken);

        using HttpClient client = fixture.CreateRawClient();
        client.BaseAddress = new Uri("https://localhost:5281");
        string accessToken = await IssueServiceTokenAsync(
            client,
            key,
            clientId,
            cancellationToken);

        using HttpRequestMessage baselineRequest = new(
            HttpMethod.Get,
            "/api/business-object-definitions");
        baselineRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage baselineResponse = await client.SendAsync(
            baselineRequest,
            cancellationToken);
        baselineResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using (IServiceScope scope = fixture.CreateScope())
        {
            IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            ServiceIdentity persisted = await context.ServiceIdentities.SingleAsync(
                value => value.Id == serviceIdentity.Id,
                cancellationToken);
            if (authority == "key")
            {
                persisted.RevokeKey(
                    persisted.Keys.Single().Id,
                    persisted.Revision,
                    DateTime.UtcNow);
            }
            else
            {
                persisted.Revoke(persisted.Revision, DateTime.UtcNow);
            }
            await new ServiceIdentityClientProjection(context).StageAsync(persisted, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
        }

        using HttpRequestMessage revokedRequest = new(
            HttpMethod.Get,
            "/api/business-object-definitions");
        revokedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage revokedResponse = await client.SendAsync(
            revokedRequest,
            cancellationToken);
        revokedResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using (IServiceScope scope = fixture.CreateScope())
        {
            IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await context.IdentityAuditOutboxRecords.AnyAsync(
                value => value.TargetId == serviceIdentity.Id
                    && value.Outcome == "token_rejected"
                    && value.MetadataJson == "{}",
                cancellationToken)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task ServiceToken_WithExactCreateGrant_CreatesServiceOwnedRecordOnly()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string clientId = $"service-{Guid.NewGuid():N}";
        ServiceIdentity serviceIdentity = await SeedServiceIdentityAsync(
            key,
            clientId,
            cancellationToken);
        await InstallServiceCreatePolicyAsync(serviceIdentity, cancellationToken);

        using HttpClient client = fixture.CreateRawClient();
        client.BaseAddress = new Uri("https://localhost:5281");
        string accessToken = await IssueServiceTokenAsync(
            client,
            key,
            clientId,
            cancellationToken);
        using HttpRequestMessage create = new(
            HttpMethod.Post,
            "/api/business-object-records/loan_application")
        {
            Content = JsonContent.Create(
                new
                {
                    idempotencyKey = $"service-{Guid.NewGuid():N}",
                    values = new { display_name = new[] { "Service draft" } },
                },
                options: ApiTestFixture.JsonOptions),
        };
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage created = await client.SendAsync(create, cancellationToken);

        string content = await created.Content.ReadAsStringAsync(cancellationToken);
        created.StatusCode.Should().Be(HttpStatusCode.Created, content);
        using JsonDocument payload = JsonDocument.Parse(content);
        Guid recordId = payload.RootElement.GetProperty("id").GetGuid();
        JsonElement actor = payload.RootElement.GetProperty("createdBySubject");
        actor.GetProperty("kind").GetString().Should().Be("Service");
        actor.GetProperty("subjectId").GetGuid().Should().Be(serviceIdentity.Id);

        using (IServiceScope scope = fixture.CreateScope())
        {
            BusinessObjectsDbContext db = scope.ServiceProvider
                .GetRequiredService<BusinessObjectsDbContext>();
            BusinessObjectRecord record = await db.BusinessObjectRecords.SingleAsync(
                value => value.Id == BusinessObjectRecordId.From(recordId),
                cancellationToken);
            record.Owner.Kind.Should().Be(Axis.BusinessObjects.Domain.ValueObjects.SubjectKind.Service);
            record.Owner.Id.Should().Be(serviceIdentity.Id);
        }

        using HttpRequestMessage list = new(
            HttpMethod.Get,
            "/api/business-object-records?page=1&pageSize=20&objectKey=loan_application");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage listResponse = await client.SendAsync(list, cancellationToken);
        listResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using HttpRequestMessage ruleRead = new(
            HttpMethod.Get,
            "/api/rules/expression-language");
        ruleRead.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage ruleReadResponse = await client.SendAsync(
            ruleRead,
            cancellationToken);
        ruleReadResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using HttpRequestMessage ungovernedRuleRoute = new(
            HttpMethod.Post,
            "/api/rules/authoring/complete")
        {
            Content = JsonContent.Create(new { }, options: ApiTestFixture.JsonOptions),
        };
        ungovernedRuleRoute.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken);
        using HttpResponseMessage ungovernedRuleResponse = await client.SendAsync(
            ungovernedRuleRoute,
            cancellationToken);
        ungovernedRuleResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using HttpRequestMessage lifecycle = new(HttpMethod.Get, "/api/solutions/versions");
        lifecycle.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage lifecycleResponse = await client.SendAsync(
            lifecycle,
            cancellationToken);
        lifecycleResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ServiceToken_WhenFallbackCredentialFlowIsAttempted_DeniesWithoutContinuation()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string clientId = $"service-{Guid.NewGuid():N}";
        await SeedServiceIdentityAsync(key, clientId, cancellationToken);
        using HttpClient client = fixture.CreateRawClient();
        client.BaseAddress = new Uri("https://localhost:5281");

        using HttpResponseMessage passwordGrant = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = clientId,
                ["username"] = "service",
                ["password"] = "not-a-service-credential",
            }),
            cancellationToken);
        await AssertMachineDenialAsync(passwordGrant, cancellationToken);

        using HttpResponseMessage sharedSecret = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = "not-supported",
            }),
            cancellationToken);
        await AssertMachineDenialAsync(sharedSecret, cancellationToken);

        using HttpResponseMessage browserGrant = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = clientId,
                ["code"] = "not-a-code",
                ["redirect_uri"] = "https://example.test/callback",
            }),
            cancellationToken);
        await AssertMachineDenialAsync(browserGrant, cancellationToken);
    }

    private async Task<ServiceIdentity> SeedServiceIdentityAsync(
        ECDsa key,
        string clientId,
        CancellationToken cancellationToken)
    {
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        Workspace workspace = Workspace.CreatePersonal(
            "Service Workspace",
            WorkspaceSlug.Create($"service-{Guid.NewGuid():N}").Value);
        workspace.ActivateAfterOwnerVerification();
        ServiceIdentity serviceIdentity = ServiceIdentity.Create(
            workspace.Id,
            clientId,
            DateTime.UtcNow);
        serviceIdentity.AddKey(
            "key-1",
            Base64Url(SHA256.HashData(parameters.Q.X!.Concat(parameters.Q.Y!).ToArray())),
            Base64Url(parameters.Q.X!),
            Base64Url(parameters.Q.Y!),
            serviceIdentity.Revision,
            DateTime.UtcNow);

        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Workspaces.AddAsync(workspace, cancellationToken);
        await context.ServiceIdentities.AddAsync(serviceIdentity, cancellationToken);
        await new ServiceIdentityClientProjection(context).StageAsync(serviceIdentity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return serviceIdentity;
    }

    private async Task InstallServiceCreatePolicyAsync(
        ServiceIdentity serviceIdentity,
        CancellationToken cancellationToken)
    {
        Guid solutionVersionId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        using IServiceScope scope = fixture.CreateScope();
        IBusinessObjectDefinitionSolutionInstaller definitions = scope.ServiceProvider
            .GetRequiredService<IBusinessObjectDefinitionSolutionInstaller>();
        BusinessObjectDefinitionInstallationResult definitionResult = await definitions.InstallAsync(
            serviceIdentity.WorkspaceId,
            new BusinessObjectDefinitionSolutionComponent(
                "loan_application",
                "loan_application",
                "Loan Application",
                [new("display_name", "Display name", 0, BusinessObjectSolutionFieldType.Text, null, [])]),
            new BusinessObjectDefinitionInstallationReceipt(
                solutionVersionId,
                SubjectReference.Service(serviceIdentity.Id),
                hash,
                operationId,
                Guid.NewGuid(),
                1),
            cancellationToken);
        definitionResult.IsSuccess.Should().BeTrue(definitionResult.ProblemCode);

        IProductPolicyInstaller policies = scope.ServiceProvider
            .GetRequiredService<IProductPolicyInstaller>();
        ProductPolicyComponent policy = new(
            "service_product",
            solutionVersionId,
            [new("ServiceCreator", new Dictionary<string, ProductRolePresentation>
            {
                ["en"] = new("Service creator", null),
            })],
            [new(
                "ServiceCreator",
                BusinessObjectProductActions.RecordCreate,
                BusinessObjectProductActions.RecordResourceType,
                "loan_application",
                ProductActionScope.Own)]);
        ProductPolicyInstallResult policyResult = await policies.InstallAsync(
            new InstallProductPolicyRequest(
                serviceIdentity.WorkspaceId,
                policy,
                "1.0.0",
                hash,
                operationId.ToString("D"),
                Guid.NewGuid().ToString("D"),
                2,
                SubjectReference.Service(serviceIdentity.Id),
                "service-policy-seed"),
            cancellationToken);
        policyResult.IsInstalled.Should().BeTrue(policyResult.Error);

        AuthorizationDbContext authorization = scope.ServiceProvider
            .GetRequiredService<AuthorizationDbContext>();
        await authorization.Assignments.AddAsync(new ProductRoleAssignmentRow
        {
            Id = Guid.NewGuid(),
            WorkspaceId = serviceIdentity.WorkspaceId,
            SubjectKind = SubjectKind.Service.ToString(),
            SubjectId = serviceIdentity.Id,
            PolicyVersionId = solutionVersionId,
            RoleKey = "ServiceCreator",
            IsActive = true,
            Revision = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);
        await authorization.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string> IssueServiceTokenAsync(
        HttpClient client,
        ECDsa key,
        string clientId,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage tokenResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_assertion_type"] = AssertionType,
                ["client_assertion"] = CreateAssertion(key, clientId),
            }),
            cancellationToken);
        string content = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK, content);
        using JsonDocument payload = JsonDocument.Parse(content);
        string accessToken = payload.RootElement.GetProperty("access_token").GetString()!;
        accessToken.Should().NotContain(".");
        payload.RootElement.GetProperty("expires_in").GetInt32().Should().BeInRange(1, 300);
        payload.RootElement.TryGetProperty("refresh_token", out _).Should().BeFalse();
        payload.RootElement.TryGetProperty("id_token", out _).Should().BeFalse();
        return accessToken;
    }

    private static async Task AssertMachineDenialAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.Unauthorized);
        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        content.Contains("access_token", StringComparison.Ordinal).Should().BeFalse();
        content.Contains("refresh_token", StringComparison.Ordinal).Should().BeFalse();
        content.Contains("authorization_uri", StringComparison.Ordinal).Should().BeFalse();
        content.Contains("verification_uri", StringComparison.Ordinal).Should().BeFalse();
    }

    private static string CreateAssertion(ECDsa key, string clientId)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            alg = "ES256",
            typ = "client-authentication+jwt",
            kid = "key-1",
        })));
        string payload = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            iss = clientId,
            sub = clientId,
            aud = TokenAudience,
            jti = Guid.NewGuid().ToString("N"),
            iat = now,
            nbf = now,
            exp = now + 300,
        })));
        byte[] signingInput = Encoding.ASCII.GetBytes($"{header}.{payload}");
        byte[] signature = key.SignData(
            signingInput,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
