using System.Net;
using System.Net.Http.Headers;
using Axis.Mcp.Api;
using Axis.Mcp.Authentication;
using Axis.Mcp.Tools;

namespace Axis.Mcp.Tests;

public sealed class AxisMcpToolTests
{
    [Fact]
    public async Task ListRules_WhenFiltersContainSpaces_EncodesTheRequestAndUsesBearerAuth()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RecordingHandler handler = new("{\"items\":[]}");
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://localhost:5281/"),
        };
        AxisApiClient api = new(httpClient, new FixedAccessTokenProvider("test-token"));
        AxisMcpTools tools = new(api);

        string result = await tools.ListRulesAsync(
            page: 2,
            pageSize: 10,
            origin: "workspace",
            status: "active",
            query: "risk review",
            language: "en",
            cancellationToken: cancellationToken);

        Assert.Equal("{\"items\":[]}", result);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(
            "/api/rules?page=2&pageSize=10&origin=Workspace&status=Active&query=risk%20review&language=en",
            handler.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", handler.Authorization!.Scheme);
        Assert.Equal("test-token", handler.Authorization.Parameter);
    }

    [Fact]
    public async Task GetRuleBinding_WhenInvoked_UsesAuthenticatedDetailRoute()
    {
        RecordingHandler handler = new("{\"id\":\"binding\"}");
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://localhost:5281/"),
        };
        AxisMcpBindingReadTools tools = new(
            new AxisApiClient(httpClient, new FixedAccessTokenProvider("test-token")));
        Guid bindingId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        string result = await tools.GetRuleBindingAsync(bindingId, TestContext.Current.CancellationToken);

        Assert.Equal("{\"id\":\"binding\"}", result);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal($"/api/rule-bindings/{bindingId:D}", handler.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", handler.Authorization!.Scheme);
    }

    [Fact]
    public async Task IdentityTools_WhenOrganizationIsCreated_ForwardsOnlyNameAndIdempotencyHeader()
    {
        RecordingHandler handler = new("{\"organizationName\":\"Acme\"}");
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://localhost:5281/"),
        };
        AxisMcpIdentityTools tools = new(
            new AxisApiClient(httpClient, new FixedAccessTokenProvider("test-token")),
            new Axis.Mcp.Configuration.AxisMcpMutationGuard(
                Axis.Mcp.Configuration.AxisMcpOptions.Create(
                    new Uri("https://localhost:5281/"),
                    ".dev-certs/rootCA.pem",
                    "write")));

        await tools.CreateOrganizationWorkspaceAsync(
            new CreateOrganizationWorkspaceInput("Acme", "organization-1"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/organizations", handler.RequestUri!.PathAndQuery);
        Assert.Equal("organization-1", handler.IdempotencyKey);
        Assert.Equal("{\"name\":\"Acme\"}", handler.RequestBody);
        Assert.DoesNotContain("userId", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("workspaceId", handler.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("idempotencyKey", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeleteRuleBinding_WhenInvoked_SendsExpectedRevision()
    {
        RecordingHandler handler = new(string.Empty);
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://localhost:5281/"),
        };
        AxisMcpBindingWriteTools tools = new(
            new AxisApiClient(httpClient, new FixedAccessTokenProvider("test-token")),
            new Axis.Mcp.Configuration.AxisMcpMutationGuard(
                Axis.Mcp.Configuration.AxisMcpOptions.Create(
                    new Uri("https://localhost:5281/"),
                    ".dev-certs/rootCA.pem",
                    "write")));
        Guid bindingId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await tools.DeleteRuleBindingAsync(bindingId, 4, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Delete, handler.Method);
        Assert.Equal($"/api/rule-bindings/{bindingId:D}", handler.RequestUri!.PathAndQuery);
        Assert.Equal("{\"expectedRevision\":4}", handler.RequestBody);
    }

    [Fact]
    public async Task RuleLifecycleTools_WhenInvoked_UseTheCurrentVersionAndActivationRoutes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RecordingHandler handler = new("{\"isMatch\":true}");
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://localhost:5281/"),
        };
        AxisMcpRuleLifecycleTools tools = new(
            new AxisApiClient(httpClient, new FixedAccessTokenProvider("test-token")),
            new Axis.Mcp.Configuration.AxisMcpMutationGuard(
                Axis.Mcp.Configuration.AxisMcpOptions.Create(
                    new Uri("https://localhost:5281/"),
                    ".dev-certs/rootCA.pem",
                    "write")));

        await tools.CreateRuleDefinitionVersionAsync(
            "risk rule",
            new RuleRevisionInput(3),
            cancellationToken: cancellationToken);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/rules/risk%20rule/versions", handler.RequestUri!.PathAndQuery);
        Assert.Equal("{\"expectedRevision\":3}", handler.RequestBody);

        await tools.ActivateRuleDefinitionVersionAsync(
            "risk rule",
            new ActivateRuleDefinitionVersionInput(2, 4),
            cancellationToken);
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal("/api/rules/risk%20rule/active-version", handler.RequestUri!.PathAndQuery);
        Assert.Equal("{\"version\":2,\"expectedRevision\":4}", handler.RequestBody);

        await tools.DeactivateRuleDefinitionAsync("risk rule", new RuleRevisionInput(5), cancellationToken);
        Assert.Equal(HttpMethod.Delete, handler.Method);
        Assert.Equal("/api/rules/risk%20rule/active-version", handler.RequestUri!.PathAndQuery);
        Assert.Equal("{\"expectedRevision\":5}", handler.RequestBody);

        await tools.ArchiveRuleDefinitionAsync("risk rule", new RuleRevisionInput(6), cancellationToken);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/rules/risk%20rule/archive", handler.RequestUri!.PathAndQuery);
        Assert.Equal("{\"expectedRevision\":6}", handler.RequestBody);
    }

    [Fact]
    public async Task RuleReadTools_AndBindingEvaluation_UseCurrentReadOnlyRoutes()
    {
        RecordingHandler handler = new("{\"isMatch\":true}");
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://localhost:5281/"),
        };
        AxisApiClient api = new(httpClient, new FixedAccessTokenProvider("test-token"));
        AxisMcpRuleReadTools ruleTools = new(api);
        AxisMcpBindingEvaluationTools bindingTools = new(api);

        await ruleTools.SimulateRuleDefinitionDraftAsync(
            "risk rule",
            new SimulateRuleDraftInput(new Dictionary<string, RuleInputValue>
            {
                ["amount"] = new("Decimal", ["10.00"]),
            }),
            TestContext.Current.CancellationToken);
        Assert.Equal("/api/rules/risk%20rule/draft/simulate", handler.RequestUri!.PathAndQuery);
        Assert.Contains("\"amount\":{\"type\":\"Decimal\",\"values\":[\"10.00\"]}", handler.RequestBody, StringComparison.Ordinal);

        await ruleTools.SimulateRuleDefinitionVersionAsync(
            "risk rule",
            2,
            new SimulateRuleVersionInput(new Dictionary<string, RuleInputValue>()),
            TestContext.Current.CancellationToken);
        Assert.Equal("/api/rules/risk%20rule/versions/2/simulate", handler.RequestUri!.PathAndQuery);

        await bindingTools.EvaluateRuleBindingAsync(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new EvaluateRuleBindingInput(
                new RuleContextInput(new Dictionary<string, RuleInputValue>
                {
                    ["amount"] = new("Decimal", ["10.00"]),
                }),
                BindingRevision: 4),
            TestContext.Current.CancellationToken);
        Assert.Equal("/api/rule-bindings/11111111-1111-1111-1111-111111111111/evaluate", handler.RequestUri!.PathAndQuery);
        Assert.Contains("\"bindingRevision\":4", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BusinessObjectRecordTools_UsePersistedRecordRoutes_AndRevisionPayloads()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RecordingHandler handler = new("{\"id\":\"record\"}");
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://localhost:5281/"),
        };
        AxisApiClient api = new(httpClient, new FixedAccessTokenProvider("test-token"));
        AxisMcpBusinessObjectRecordReadTools readTools = new(api);
        AxisMcpBusinessObjectRecordWriteTools writeTools = new(
            api,
            new Axis.Mcp.Configuration.AxisMcpMutationGuard(
                Axis.Mcp.Configuration.AxisMcpOptions.Create(
                    new Uri("https://localhost:5281/"),
                    ".dev-certs/rootCA.pem",
                    "write")));

        await readTools.ListBusinessObjectRecordsAsync(
            page: 2,
            pageSize: 10,
            objectKey: "business record",
            cancellationToken: cancellationToken);
        Assert.Equal(
            "/api/business-object-records?page=2&pageSize=10&objectKey=business%20record",
            handler.RequestUri!.PathAndQuery);

        await writeTools.CreateBusinessObjectRecordAsync(
            "business_record",
            new CreateBusinessObjectRecordInput(
                "record-1",
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["display_name"] = ["Ada Lovelace"],
                }),
            cancellationToken);
        Assert.Equal("POST", handler.Method!.Method);
        Assert.Equal("/api/business-object-records/business_record", handler.RequestUri!.PathAndQuery);
        Assert.Contains("\"idempotencyKey\":\"record-1\"", handler.RequestBody, StringComparison.Ordinal);

        await writeTools.SaveBusinessObjectRecordAsync(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new SaveBusinessObjectRecordInput(
                4,
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["display_name"] = ["Ada Lovelace"],
                }),
            cancellationToken);
        Assert.Equal("PUT", handler.Method!.Method);
        Assert.Equal(
            "/api/business-object-records/11111111-1111-1111-1111-111111111111",
            handler.RequestUri!.PathAndQuery);
        Assert.Contains("\"expectedRevision\":4", handler.RequestBody, StringComparison.Ordinal);

        await writeTools.SubmitBusinessObjectRecordAsync(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new SubmitBusinessObjectRecordInput(5),
            cancellationToken);
        Assert.Equal("POST", handler.Method!.Method);
        Assert.Equal(
            "/api/business-object-records/11111111-1111-1111-1111-111111111111/submit",
            handler.RequestUri!.PathAndQuery);
        Assert.Contains("\"expectedRevision\":5", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_WhenApiUrlIsExternal_RejectsConfiguration()
    {
        Assert.Throws<ArgumentException>(() => Axis.Mcp.Configuration.AxisMcpOptions.Create(
            new Uri("https://example.test/"),
            ".dev-certs/rootCA.pem"));
    }

    [Fact]
    public void MutationGuard_WhenAccessIsRead_RejectsWrites()
    {
        Axis.Mcp.Configuration.AxisMcpOptions options = Axis.Mcp.Configuration.AxisMcpOptions.Create(
            new Uri("https://localhost:5281/"),
            ".dev-certs/rootCA.pem",
            "read");
        Axis.Mcp.Configuration.AxisMcpMutationGuard guard =
            new(options);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => guard.EnsureEnabled("CreateRuleDefinitionVersion"));

        Assert.Contains("AXIS_MCP_ACCESS", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MutationGuard_WhenAccessIsWrite_AllowsWrites()
    {
        Axis.Mcp.Configuration.AxisMcpOptions options = Axis.Mcp.Configuration.AxisMcpOptions.Create(
            new Uri("https://localhost:5281/"),
            ".dev-certs/rootCA.pem",
            "write");
        Axis.Mcp.Configuration.AxisMcpMutationGuard guard =
            new(options);

        guard.EnsureEnabled("CreateRuleDefinitionVersion");
    }

    [Fact]
    public async Task ApiClient_WhenFirstRequestIsUnauthorized_InvalidatesAndRetriesOnce()
    {
        RotatingAccessTokenProvider tokenProvider = new();
        UnauthorizedThenSuccessHandler handler = new();
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://localhost:5281/"),
        };
        AxisApiClient api = new(httpClient, tokenProvider);

        string result = await api.GetJsonAsync("api/users/me", TestContext.Current.CancellationToken);

        Assert.Equal("{\"ok\":true}", result);
        Assert.Equal(1, tokenProvider.InvalidateCount);
        Assert.Equal(["first-token", "second-token"], handler.AuthorizationTokens);
    }

    [Fact]
    public async Task BusinessObjectRecordTools_WhenApiReturnsProblemDetails_PreservesStructuredErrors()
    {
        using HttpClient httpClient = new(new ProblemHandler())
        {
            BaseAddress = new Uri("https://localhost:5281/"),
        };
        AxisApiClient api = new(httpClient, new FixedAccessTokenProvider("test-token"));
        AxisMcpBusinessObjectRecordWriteTools tools = new(
            api,
            new Axis.Mcp.Configuration.AxisMcpMutationGuard(
                Axis.Mcp.Configuration.AxisMcpOptions.Create(
                    new Uri("https://localhost:5281/"),
                    ".dev-certs/rootCA.pem",
                    "write")));

        AxisApiException exception = await Assert.ThrowsAsync<AxisApiException>(
            () => tools.SaveBusinessObjectRecordAsync(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                new SaveBusinessObjectRecordInput(
                    1,
                    new Dictionary<string, IReadOnlyList<string>>
                    {
                        ["amount"] = ["invalid"],
                    }),
                TestContext.Current.CancellationToken));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("businessObjects.recordInvalid", exception.ProblemCode);
        Assert.Equal("urn:axis:problem:businessObjects.recordInvalid", exception.ProblemType);
        Assert.Equal(["The value is invalid."], exception.FieldErrors["amount"]);
        Assert.Equal(["businessObjects.amountInvalid"], exception.ErrorCodes["amount"]);
    }

    private sealed class FixedAccessTokenProvider(string token) : IAxisAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);

        public void Invalidate()
        {
        }
    }

    private sealed class RotatingAccessTokenProvider : IAxisAccessTokenProvider
    {
        private int _calls;

        public int InvalidateCount { get; private set; }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            _calls++;
            return Task.FromResult(_calls == 1 ? "first-token" : "second-token");
        }

        public void Invalidate() => InvalidateCount++;
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

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
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            };
        }
    }

    private sealed class UnauthorizedThenSuccessHandler : HttpMessageHandler
    {
        public List<string> AuthorizationTokens { get; } = [];
        private int _calls;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationTokens.Add(request.Headers.Authorization!.Parameter!);
            _calls++;
            return Task.FromResult(
                _calls == 1
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"ok\":true}"),
                    });
        }
    }

    private sealed class ProblemHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                {
                    Content = new StringContent(
                        "{\"type\":\"urn:axis:problem:businessObjects.recordInvalid\",\"title\":\"Validation failed\",\"detail\":\"Fix the values.\",\"code\":\"businessObjects.recordInvalid\",\"errors\":{\"amount\":[\"The value is invalid.\"]},\"errorCodes\":{\"amount\":[\"businessObjects.amountInvalid\"]}}"),
                });
    }
}
