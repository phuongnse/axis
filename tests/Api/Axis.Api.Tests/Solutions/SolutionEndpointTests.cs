using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Api.Tests.Administration;
using Axis.Api.Tests.Helpers;
using Axis.Shared.Domain.Primitives;
using Axis.Solutions.Application;
using Axis.Solutions.Domain;
using Axis.Solutions.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Swagger;

namespace Axis.Api.Tests.Solutions;

[Collection("Api")]
public sealed class SolutionEndpointTests(ApiTestFixture fixture)
{
    private const int MaximumPackageBytes = 10 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task SolutionEndpoints_WhenAnonymous_ReturnUnauthorized()
    {
        using HttpClient anonymous = fixture.CreateAnonymousClient();

        (await anonymous.GetAsync(
            "/api/solutions/installations",
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync(
            $"/api/solutions/operations/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void SolutionLifecycleSurface_WhenEnumerated_ContainsOnlyThePublishedWorkflowContracts()
    {
        using IServiceScope scope = fixture.CreateScope();
        EndpointDataSource endpoints = scope.ServiceProvider.GetRequiredService<EndpointDataSource>();
        string[] routes = endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/api/solutions",
                StringComparison.Ordinal) is true)
            .SelectMany(endpoint => endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods
                .Select(method => $"{method} {endpoint.RoutePattern.RawText}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedRoutes =
        [
            "GET /api/solutions/installations",
            "GET /api/solutions/operations/{operationId:guid}",
            "GET /api/solutions/versions",
            "GET /api/solutions/versions/{solutionVersionId:guid}",
            "POST /api/solutions/operations/{operationId:guid}/resume",
            "POST /api/solutions/versions",
            "POST /api/solutions/versions/{solutionVersionId:guid}/installations",
        ];
        routes.Should().Equal(expectedRoutes);

        ISwaggerProvider provider = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>();
        string[] openApiRoutes = provider.GetSwagger("v1").Paths
            .Where(path => path.Key.StartsWith("/api/solutions", StringComparison.Ordinal))
            .SelectMany(path => (path.Value.Operations ?? []).Keys.Select(method =>
                $"{method.ToString().ToUpperInvariant()} {path.Key}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expectedOpenApiRoutes =
        [
            "GET /api/solutions/installations",
            "GET /api/solutions/operations/{operationId}",
            "GET /api/solutions/versions",
            "GET /api/solutions/versions/{solutionVersionId}",
            "POST /api/solutions/operations/{operationId}/resume",
            "POST /api/solutions/versions",
            "POST /api/solutions/versions/{solutionVersionId}/installations",
        ];
        openApiRoutes.Should().Equal(expectedOpenApiRoutes);
    }

    [Fact]
    public async Task SolutionReadAndInstallEndpoints_WhenAdministrator_StayWorkspaceScopedAndRequireIdempotency()
    {
        WorkspaceAdministratorApiTestSession.AdministratorContext administrator =
            await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);

        Guid publishedVersionId = await SeedVersionAsync();
        Guid seededOperationId = await SeedBlockedInstallationAsync(
            administrator.WorkspaceId,
            publishedVersionId);
        HttpResponseMessage versions = await fixture.Client.GetAsync(
            "/api/solutions/versions",
            TestContext.Current.CancellationToken);
        versions.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement publishedVersion = (await ReadJsonAsync(versions)).EnumerateArray()
            .Single(value => value.GetProperty("id").GetGuid() == publishedVersionId);
        publishedVersion.GetProperty("components").EnumerateArray()
            .Select(value => value.GetProperty("key").GetString())
            .Should().Equal("policy", "definition");
        HttpResponseMessage version = await fixture.Client.GetAsync(
            $"/api/solutions/versions/{publishedVersionId}",
            TestContext.Current.CancellationToken);
        version.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(version)).GetProperty("trustStatus").GetString()
            .Should().Be("Unknown");

        HttpResponseMessage list = await fixture.Client.GetAsync(
            "/api/solutions/installations",
            TestContext.Current.CancellationToken);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement listBody = await ReadJsonAsync(list);
        listBody.ValueKind.Should().Be(JsonValueKind.Array);
        listBody.EnumerateArray().Should().Contain(value =>
            value.GetProperty("operationId").GetGuid() == seededOperationId &&
            value.GetProperty("operationStatus").GetString() == "Blocked");

        HttpResponseMessage blockedResume = await fixture.PostBrowserAsync(
            $"/api/solutions/operations/{seededOperationId}/resume",
            cancellationToken: TestContext.Current.CancellationToken);
        blockedResume.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonAsync(blockedResume)).GetProperty("code").GetString()
            .Should().Be("solutions.install.operation_not_resumable");

        Guid unavailableVersionId = Guid.NewGuid();
        HttpResponseMessage missingKey = await fixture.PostBrowserAsync(
            $"/api/solutions/versions/{unavailableVersionId}/installations",
            cancellationToken: TestContext.Current.CancellationToken);
        missingKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(missingKey)).GetProperty("code").GetString()
            .Should().Be("solutions.install.invalid_request");

        using HttpRequestMessage install = new(
            HttpMethod.Post,
            $"/api/solutions/versions/{unavailableVersionId}/installations");
        install.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        HttpResponseMessage unavailable = await fixture.SendBrowserMutationAsync(
            install,
            TestContext.Current.CancellationToken);
        unavailable.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonElement unavailableProblem = await ReadJsonAsync(unavailable);
        unavailableProblem.GetProperty("code").GetString()
            .Should().Be("solutions.version.not_found");
        unavailableProblem.GetRawText().Contains(
            unavailableVersionId.ToString(),
            StringComparison.OrdinalIgnoreCase).Should().BeFalse();

        Guid unavailableOperationId = Guid.NewGuid();
        HttpResponseMessage status = await fixture.Client.GetAsync(
            $"/api/solutions/operations/{unavailableOperationId}",
            TestContext.Current.CancellationToken);
        status.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadJsonAsync(status)).GetProperty("code").GetString()
            .Should().Be("solutions.resource.not_found");

        HttpResponseMessage resume = await fixture.PostBrowserAsync(
            $"/api/solutions/operations/{unavailableOperationId}/resume",
            cancellationToken: TestContext.Current.CancellationToken);
        resume.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadJsonAsync(resume)).GetProperty("code").GetString()
            .Should().Be("solutions.resource.not_found");
    }

    [Fact]
    public async Task SolutionVersions_WhenPersonalOwner_ReturnsCurrentLifecycleView()
    {
        await PersonalWorkspaceOwnerApiTestSession.CreateAsync(fixture);

        HttpResponseMessage response = await fixture.Client.GetAsync(
            "/api/solutions/versions",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PublishSolutionVersion_WhenPayloadIsUnsafe_RejectsBeforePackageProcessing()
    {
        await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);

        using HttpRequestMessage wrongTypeRequest = new(HttpMethod.Post, "/api/solutions/versions")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        HttpResponseMessage wrongType = await fixture.SendBrowserMutationAsync(
            wrongTypeRequest,
            TestContext.Current.CancellationToken);
        wrongType.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);

        using HttpRequestMessage emptyRequest = new(HttpMethod.Post, "/api/solutions/versions")
        {
            Content = PackageContent([]),
        };
        HttpResponseMessage empty = await fixture.SendBrowserMutationAsync(
            emptyRequest,
            TestContext.Current.CancellationToken);
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(empty)).GetProperty("code").GetString()
            .Should().Be("solutions.package.empty");

        using HttpRequestMessage oversizedRequest = new(HttpMethod.Post, "/api/solutions/versions")
        {
            Content = PackageContent(new byte[MaximumPackageBytes + 1]),
        };
        HttpResponseMessage oversized = await fixture.SendBrowserMutationAsync(
            oversizedRequest,
            TestContext.Current.CancellationToken);
        oversized.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        JsonElement oversizedProblem = await ReadJsonAsync(oversized);
        oversizedProblem.GetProperty("code").GetString()
            .Should().Be("solutions.package.too_large");
        oversizedProblem.GetRawText().Contains(
            "package bytes",
            StringComparison.OrdinalIgnoreCase).Should().BeFalse();

        using HttpRequestMessage malformedPackage = new(HttpMethod.Post, "/api/solutions/versions")
        {
            Content = PackageContent(Encoding.UTF8.GetBytes("{}")),
        };
        HttpResponseMessage malformed = await fixture.SendBrowserMutationAsync(
            malformedPackage,
            TestContext.Current.CancellationToken);
        malformed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadJsonAsync(malformed)).GetProperty("code").GetString()
            .Should().Be("solutions.package.envelope_invalid");
    }

    [Fact]
    public async Task OperationStatus_WhenOperationBelongsToAnotherWorkspace_ReturnsNonDisclosingNotFound()
    {
        await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        Guid operationId = await SeedForeignOperationAsync();

        HttpResponseMessage response = await fixture.Client.GetAsync(
            $"/api/solutions/operations/{operationId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonElement problem = await ReadJsonAsync(response);
        problem.GetProperty("code").GetString()
            .Should().Be("solutions.resource.not_found");
        problem.GetRawText().Contains(
            operationId.ToString(),
            StringComparison.OrdinalIgnoreCase).Should().BeFalse();

        HttpResponseMessage resume = await fixture.PostBrowserAsync(
            $"/api/solutions/operations/{operationId}/resume",
            cancellationToken: TestContext.Current.CancellationToken);
        resume.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadJsonAsync(resume)).GetProperty("code").GetString()
            .Should().Be("solutions.resource.not_found");
    }

    [Fact]
    public async Task ResumeSolutionInstallation_WhenPublisherWasRevoked_BlocksBeforeNextMutation()
    {
        WorkspaceAdministratorApiTestSession.AdministratorContext administrator =
            await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        Guid operationId = await SeedRevokedPartialInstallationAsync(administrator.WorkspaceId);

        HttpResponseMessage response = await fixture.PostBrowserAsync(
            $"/api/solutions/operations/{operationId}/resume",
            cancellationToken: TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonAsync(response)).GetProperty("code").GetString()
            .Should().Be("solutions.package.publisher_untrusted");
        HttpResponseMessage installationsResponse = await fixture.Client.GetAsync(
            "/api/solutions/installations",
            TestContext.Current.CancellationToken);
        installationsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement installation = (await ReadJsonAsync(installationsResponse))
            .EnumerateArray()
            .Single(value => value.GetProperty("operationId").GetGuid() == operationId);
        installation.GetProperty("provisioningStatus").GetString().Should().Be("Failed");
        installation.GetProperty("complianceStatus").GetString().Should().Be("Noncompliant");
        installation.GetProperty("operationStatus").GetString().Should().Be("Blocked");
        installation.GetProperty("components").EnumerateArray()
            .Select(value => value.GetProperty("status").GetString())
            .Should().Equal("Confirmed", "Failed");
    }

    private async Task<Guid> SeedForeignOperationAsync()
    {
        Guid versionId = Guid.NewGuid();
        Guid installationId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        Guid actorSubjectId = Guid.NewGuid();
        Guid foreignWorkspaceId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string suffix = Guid.NewGuid().ToString("N");
        string hash = new('a', 64);
        using IServiceScope scope = fixture.CreateScope();
        SolutionsDbContext db = scope.ServiceProvider.GetRequiredService<SolutionsDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO solution_versions
                (id, solution_key, version, package_sha256, envelope, axis_openapi_sha256,
                 publisher_id, publisher_key_id, source_revision, build_id, built_at,
                 source_uri, published_at, created_by_kind, created_by_display_name)
            VALUES
                ({versionId}, {"foreign_" + suffix}, {"1.0.0-" + suffix}, {hash},
                 {new byte[] { 1 }}, {hash}, {"publisher"}, {"key"}, {suffix},
                 {"build-" + suffix}, {now}, {"https://example.test/foreign"}, {now},
                 {"System"}, {ActorSnapshot.SystemDisplayName})
            """, TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO solution_installations
                (id, workspace_id, solution_key, solution_version_id, provisioning_status,
                 compliance_status, created_at, updated_at, revision,
                 created_by_kind, created_by_subject_id, created_by_display_name,
                 updated_by_kind, updated_by_subject_id, updated_by_display_name)
            VALUES
                ({installationId}, {foreignWorkspaceId}, {"foreign_" + suffix}, {versionId}, {"Installing"},
                 {"Compliant"}, {now}, {now}, {0},
                 {"User"}, {actorSubjectId}, {"Foreign Operator"},
                 {"User"}, {actorSubjectId}, {"Foreign Operator"})
            """, TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO solution_installation_operations
                (id, workspace_id, actor_subject_id, actor_subject_kind, actor_correlation_id,
                 installation_id, idempotency_key, request_hash,
                 status, lease_epoch, lease_expires_at, problem_code, created_at,
                 updated_at, revision)
            VALUES
                ({operationId}, {foreignWorkspaceId}, {actorSubjectId}, {"Human"}, {"test-correlation"},
                 {installationId}, {suffix}, {hash},
                 {"Pending"}, {0L}, {null}, {null}, {now}, {now}, {0})
            """, TestContext.Current.CancellationToken);
        return operationId;
    }

    private async Task<Guid> SeedRevokedPartialInstallationAsync(Guid workspaceId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string suffix = Guid.NewGuid().ToString("N");
        string publisherId = $"publisher_{suffix}";
        string publisherKeyId = $"key_{suffix}";
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using IServiceScope scope = fixture.CreateScope();
        SolutionsDbContext db = scope.ServiceProvider.GetRequiredService<SolutionsDbContext>();
        string currentOpenApiDigest = scope.ServiceProvider
            .GetRequiredService<IConfiguration>()["Solutions:AxisOpenApiSha256"]!;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO trusted_publisher_keys
                (id, publisher_id, key_id, spki_sha256, public_key_pem, status,
                 configuration_revision, is_tombstone)
            VALUES
                ({Guid.NewGuid()}, {publisherId}, {publisherKeyId}, {new string('a', 64)},
                 {key.ExportSubjectPublicKeyInfoPem()}, {"Active"}, {1L}, {false})
            """, TestContext.Current.CancellationToken);

        SolutionVersion version = SolutionVersion.Create(
            $"resume_{suffix}",
            "1.0.0",
            new string('b', 64),
            [1],
            currentOpenApiDigest,
            publisherId,
            publisherKeyId,
            new string('d', 40),
            $"build-{suffix}",
            now,
            new Uri("https://example.test/revoked"),
            now);
        version.InitializeMetadata(ActorSnapshot.System());
        VerifiedSolutionComponent[] components =
        [
            new("authorization.policy.v1", "policy", new string('e', 64), [1], []),
            new("business-object.definition.v1", "definition", new string('f', 64), [2], []),
        ];
        ISolutionVersionRepository versions = scope.ServiceProvider
            .GetRequiredService<ISolutionVersionRepository>();
        ISolutionInstallationRepository installations = scope.ServiceProvider
            .GetRequiredService<ISolutionInstallationRepository>();
        ISolutionOperationRepository operations = scope.ServiceProvider
            .GetRequiredService<ISolutionOperationRepository>();
        ISolutionsUnitOfWork unitOfWork = scope.ServiceProvider
            .GetRequiredService<ISolutionsUnitOfWork>();
        await versions.AddAsync(version, components, TestContext.Current.CancellationToken);
        SolutionInstallation installation = SolutionInstallation.Create(workspaceId, version.SolutionKey, version.Id, now);
        installation.InitializeMetadata(ActorSnapshot.System());
        await installations.AddAsync(installation, TestContext.Current.CancellationToken);
        SolutionInstallationOperation operation = SolutionInstallationOperation.Create(
            workspaceId,
            Guid.NewGuid(),
            SolutionSubjectKind.Human,
            "test-correlation",
            installation.Id,
            $"resume-{suffix}",
            new string('1', 64),
            components.Select((component, index) => new SolutionComponentPlan(
                component.Type,
                component.Key,
                component.Sha256,
                [])).ToArray(),
            now);
        long firstEpoch = operation.AcquireLease(now, TimeSpan.FromMinutes(1));
        SolutionInstallationStep first = operation.ClaimNext(firstEpoch, now.AddMilliseconds(1));
        operation.Confirm(first.Id, firstEpoch, now.AddMilliseconds(2));
        long secondEpoch = operation.AcquireLease(now.AddMilliseconds(2), TimeSpan.FromMinutes(1));
        SolutionInstallationStep second = operation.ClaimNext(secondEpoch, now.AddMilliseconds(3));
        operation.RecordRetryableFailure(
            second.Id,
            secondEpoch,
            "solutions.install.dependency_unavailable",
            now.AddMilliseconds(4));
        await operations.AddAsync(operation, TestContext.Current.CancellationToken);
        await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE trusted_publisher_keys
            SET status = {"Revoked"}, is_tombstone = {true}
            WHERE publisher_id = {publisherId} AND key_id = {publisherKeyId}
            """, TestContext.Current.CancellationToken);
        return operation.Id;
    }

    private async Task<Guid> SeedVersionAsync()
    {
        Guid versionId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string suffix = Guid.NewGuid().ToString("N");
        string hash = new('a', 64);
        using IServiceScope scope = fixture.CreateScope();
        SolutionsDbContext db = scope.ServiceProvider.GetRequiredService<SolutionsDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO solution_versions
                (id, solution_key, version, package_sha256, envelope, axis_openapi_sha256,
                 publisher_id, publisher_key_id, source_revision, build_id, built_at,
                 source_uri, published_at, created_by_kind, created_by_display_name)
            VALUES
                ({versionId}, {"read_" + suffix}, {"1.0.0-" + suffix}, {hash},
                 {new byte[] { 1 }}, {hash}, {"missing_publisher"}, {"missing_key"}, {suffix},
                 {"build-" + suffix}, {now}, {"https://example.test/read"}, {now},
                 {"System"}, {ActorSnapshot.SystemDisplayName})
            """, TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO solution_components
                (solution_version_id, component_type, component_key, component_sha256,
                 content, depends_on)
            VALUES
                ({versionId}, {"authorization.policy.v1"}, {"policy"}, {hash},
                 {new byte[] { 1 }}, {"[]"}::jsonb),
                ({versionId}, {"business-object.definition.v1"}, {"definition"}, {hash},
                 {new byte[] { 2 }},
                 {"[{\"Type\":\"authorization.policy.v1\",\"Key\":\"policy\"}]"}::jsonb)
            """, TestContext.Current.CancellationToken);
        return versionId;
    }

    private async Task<Guid> SeedBlockedInstallationAsync(Guid workspaceId, Guid versionId)
    {
        Guid installationId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        Guid actorSubjectId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string suffix = Guid.NewGuid().ToString("N");
        string hash = new('b', 64);
        using IServiceScope scope = fixture.CreateScope();
        SolutionsDbContext db = scope.ServiceProvider.GetRequiredService<SolutionsDbContext>();
        string solutionKey = await db.SolutionVersions
            .Where(value => value.Id == versionId)
            .Select(value => value.SolutionKey)
            .SingleAsync(TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO solution_installations
                (id, workspace_id, solution_key, solution_version_id, provisioning_status,
                 compliance_status, created_at, updated_at, revision,
                 created_by_kind, created_by_subject_id, created_by_display_name,
                 updated_by_kind, updated_by_subject_id, updated_by_display_name)
            VALUES
                ({installationId}, {workspaceId}, {solutionKey}, {versionId}, {"Failed"},
                 {"Compliant"}, {now}, {now}, {0},
                 {"User"}, {actorSubjectId}, {"Blocked Installer"},
                 {"User"}, {actorSubjectId}, {"Blocked Installer"})
            """, TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO solution_installation_operations
                (id, workspace_id, actor_subject_id, actor_subject_kind, actor_correlation_id,
                 installation_id, idempotency_key, request_hash,
                 status, lease_epoch, lease_expires_at, problem_code, created_at,
                 updated_at, revision)
            VALUES
                ({operationId}, {workspaceId}, {actorSubjectId}, {"Human"}, {"test-correlation"},
                 {installationId}, {suffix}, {hash},
                 {"Blocked"}, {0L}, {null}, {"solutions.component.invalid"}, {now}, {now}, {0})
            """, TestContext.Current.CancellationToken);
        return operationId;
    }

    private static ByteArrayContent PackageContent(byte[] bytes)
    {
        ByteArrayContent content = new(bytes);
        content.Headers.ContentType = new("application/vnd.dsse.envelope.v1+json");
        return content;
    }

    private static Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
}
