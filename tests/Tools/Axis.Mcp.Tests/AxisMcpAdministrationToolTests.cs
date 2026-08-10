using System.Net;
using System.Net.Http.Headers;
using Axis.Mcp.Api;
using Axis.Mcp.Authentication;
using Axis.Mcp.Configuration;
using Axis.Mcp.Tools;

namespace Axis.Mcp.Tests;

public sealed class AxisMcpAdministrationToolTests
{
    [Fact]
    public async Task ServiceIdentityTools_WhenInvoked_UseNonSecretCurrentWorkspaceContracts()
    {
        RecordingHandler handler = new("{\"id\":\"service\"}");
        AxisApiClient api = CreateApi(handler);
        AxisMcpServiceIdentityReadTools reads = new(api);
        AxisMcpServiceIdentityWriteTools writes = new(api, WriteGuard());
        Guid identityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid keyId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await writes.CreateServiceIdentityAsync(
            new CreateServiceIdentityInput("release-agent"),
            TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Post, "/api/service-identities");
        Assert.Equal("{\"clientId\":\"release-agent\"}", handler.RequestBody);

        await reads.GetServiceIdentityAsync(identityId, TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Get, $"/api/service-identities/{identityId:D}");

        await reads.ListServiceIdentitiesAsync(TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Get, "/api/service-identities");

        const string publicJwk = "{\"kty\":\"EC\",\"crv\":\"P-256\",\"kid\":\"release-1\",\"x\":\"x\",\"y\":\"y\"}";
        await writes.AddServiceIdentityKeyAsync(
            identityId,
            new AddServiceIdentityKeyInput(2, publicJwk),
            TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Post, $"/api/service-identities/{identityId:D}/keys");
        Assert.Contains("\"expectedRevision\":2", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"publicJwk\":", handler.RequestBody, StringComparison.Ordinal);

        await writes.RevokeServiceIdentityKeyAsync(
            identityId,
            keyId,
            new ExpectedRevisionInput(3),
            TestContext.Current.CancellationToken);
        AssertRequest(
            handler,
            HttpMethod.Post,
            $"/api/service-identities/{identityId:D}/keys/{keyId:D}/revoke");

        await writes.RevokeServiceIdentityAsync(
            identityId,
            new ExpectedRevisionInput(4),
            TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Post, $"/api/service-identities/{identityId:D}/revoke");
        Assert.DoesNotContain("private", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workspaceId", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("userId", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductRoleTools_WhenInvoked_ForwardSubjectReferenceAndIdempotency()
    {
        RecordingHandler handler = new("{\"revision\":1}");
        AxisApiClient api = CreateApi(handler);
        AxisMcpAuthorizationReadTools reads = new(api);
        AxisMcpAuthorizationTools tools = new(api, WriteGuard());
        SubjectReferenceInput subject = new(
            "Service",
            Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Guid policyVersionId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await reads.ListProductRoleAssignmentsAsync(
            "vi-VN",
            TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Get, "/api/product-role-assignments?language=vi-VN");

        await tools.AssignProductRoleAsync(
            new AssignProductRoleInput(subject, policyVersionId, "caseworker", "assign-1"),
            TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Post, "/api/product-role-assignments/assign");
        Assert.Equal("assign-1", handler.IdempotencyKey);
        Assert.Contains(
            "\"target\":{\"kind\":\"Service\",\"subjectId\":\"11111111-1111-1111-1111-111111111111\"}",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("userId", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("workspaceId", handler.RequestBody, StringComparison.Ordinal);

        await tools.RevokeProductRoleAsync(
            new RevokeProductRoleInput(subject, policyVersionId, "caseworker", "revoke-1", 3),
            TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Post, "/api/product-role-assignments/revoke");
        Assert.Equal("revoke-1", handler.IdempotencyKey);
        Assert.Contains("\"expectedRevision\":3", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SolutionTools_WhenInvoked_UseTypedCurrentWorkspaceRoutes()
    {
        RecordingHandler handler = new("{\"status\":\"Pending\"}");
        AxisApiClient api = CreateApi(handler);
        AxisMcpSolutionReadTools reads = new(api);
        AxisMcpSolutionWriteTools writes = new(api, WriteGuard());
        Guid versionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid operationId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await reads.ListSolutionVersionsAsync(TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Get, "/api/solutions/versions");

        await reads.GetSolutionVersionStatusAsync(versionId, TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Get, $"/api/solutions/versions/{versionId:D}");

        await reads.ListSolutionInstallationsAsync(TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Get, "/api/solutions/installations");

        await reads.GetSolutionInstallationStatusAsync(operationId, TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Get, $"/api/solutions/operations/{operationId:D}");

        await writes.InstallSolutionVersionAsync(
            versionId,
            new InstallSolutionVersionInput("install-1"),
            TestContext.Current.CancellationToken);
        AssertRequest(
            handler,
            HttpMethod.Post,
            $"/api/solutions/versions/{versionId:D}/installations");
        Assert.Equal("install-1", handler.IdempotencyKey);
        Assert.Equal(string.Empty, handler.RequestBody);

        await writes.ResumeSolutionInstallationAsync(
            operationId,
            TestContext.Current.CancellationToken);
        AssertRequest(handler, HttpMethod.Post, $"/api/solutions/operations/{operationId:D}/resume");
        Assert.Equal(string.Empty, handler.RequestBody);
    }

    [Fact]
    public async Task PublishSolutionVersion_WhenFileIsRegularAndBounded_UploadsOnlyItsBytes()
    {
        byte[] package = "signed-package-bytes"u8.ToArray();
        string packagePath = Path.Combine(Path.GetTempPath(), $"axis-solution-{Guid.NewGuid():N}.dsse");
        await File.WriteAllBytesAsync(packagePath, package, TestContext.Current.CancellationToken);
        try
        {
            RecordingHandler handler = new("{\"version\":{\"status\":\"Published\"}}");
            AxisMcpSolutionWriteTools tools = new(CreateApi(handler), WriteGuard());

            string result = await tools.PublishSolutionVersionAsync(
                new PublishSolutionVersionInput(packagePath),
                TestContext.Current.CancellationToken);

            AssertRequest(handler, HttpMethod.Post, "/api/solutions/versions");
            Assert.Equal("application/vnd.dsse.envelope.v1+json", handler.ContentType?.MediaType);
            Assert.Equal(package, handler.RequestBytes);
            Assert.Equal("{\"version\":{\"status\":\"Published\"}}", result);
            Assert.DoesNotContain("signed-package-bytes", result, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public async Task PublishSolutionVersion_WhenPathIsNotRegularOrExceedsLimit_RejectsBeforeApiCall()
    {
        RecordingHandler handler = new("{}");
        AxisMcpSolutionWriteTools tools = new(CreateApi(handler), WriteGuard());

        await Assert.ThrowsAsync<ArgumentException>(() => tools.PublishSolutionVersionAsync(
            new PublishSolutionVersionInput(Path.GetTempPath()),
            TestContext.Current.CancellationToken));

        string packagePath = Path.Combine(Path.GetTempPath(), $"axis-solution-large-{Guid.NewGuid():N}.dsse");
        try
        {
            await using (FileStream stream = File.Create(packagePath))
                stream.SetLength((10 * 1024 * 1024) + 1);

            await Assert.ThrowsAsync<ArgumentException>(() => tools.PublishSolutionVersionAsync(
                new PublishSolutionVersionInput(packagePath),
                TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(packagePath);
        }

        Assert.Null(handler.Method);
    }

    [Fact]
    public async Task MutationTools_WhenMutationGuardIsRead_RejectBeforeApiCall()
    {
        RecordingHandler handler = new("{}");
        AxisApiClient api = CreateApi(handler);
        AxisMcpMutationGuard guard = new(AxisMcpOptions.Create(
            new Uri("https://localhost:5281/"),
            ".dev-certs/rootCA.pem",
            "read"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AxisMcpAuthorizationTools(api, guard).AssignProductRoleAsync(
                new AssignProductRoleInput(
                    new SubjectReferenceInput("Human", Guid.NewGuid()),
                    Guid.NewGuid(),
                    "applicant",
                    "assign-1"),
                TestContext.Current.CancellationToken));
        Assert.Null(handler.Method);
    }

    private static AxisApiClient CreateApi(RecordingHandler handler)
    {
        HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://localhost:5281/"),
        };
        return new AxisApiClient(httpClient, new FixedAccessTokenProvider());
    }

    private static AxisMcpMutationGuard WriteGuard() => new(AxisMcpOptions.Create(
        new Uri("https://localhost:5281/"),
        ".dev-certs/rootCA.pem",
        "write"));

    private static void AssertRequest(RecordingHandler handler, HttpMethod method, string path)
    {
        Assert.Equal(method, handler.Method);
        Assert.Equal(path, handler.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
    }

    private sealed class FixedAccessTokenProvider : IAxisAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            Task.FromResult("test-token");

        public void Invalidate()
        {
        }
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public MediaTypeHeaderValue? ContentType { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;
        public byte[] RequestBytes { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out IEnumerable<string>? values)
                ? values.Single()
                : null;
            ContentType = request.Content?.Headers.ContentType;
            RequestBytes = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            };
        }
    }
}
