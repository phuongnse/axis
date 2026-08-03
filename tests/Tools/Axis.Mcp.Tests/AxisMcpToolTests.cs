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
            status: "published",
            query: "risk review",
            language: "en",
            cancellationToken: cancellationToken);

        Assert.Equal("{\"items\":[]}", result);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(
            "/api/rules?page=2&pageSize=10&origin=Workspace&status=Published&query=risk%20review&language=en",
            handler.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer", handler.Authorization!.Scheme);
        Assert.Equal("test-token", handler.Authorization.Parameter);
    }

    [Fact]
    public async Task SimulateRule_WhenCorrelationIdIsOmitted_GeneratesOneWithoutWorkspaceArguments()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        RecordingHandler handler = new("{\"isMatch\":true}");
        using HttpClient httpClient = new(handler)
        {
            BaseAddress = new Uri("https://localhost:5281/"),
        };
        AxisMcpTools tools = new(new AxisApiClient(httpClient, new FixedAccessTokenProvider("test-token")));

        string result = await tools.SimulateRuleAsync(
            "risk rule",
            definitionVersion: null,
            inputs: new Dictionary<string, RuleInputValue>
            {
                ["amount"] = new("Decimal", ["10.00"]),
            },
            cancellationToken: cancellationToken);

        Assert.Equal("{\"isMatch\":true}", result);
        Assert.Equal("/api/rules/risk%20rule/simulate", handler.RequestUri!.PathAndQuery);
        Assert.Contains("\"correlationId\":\"", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("\"amount\":{\"type\":\"Decimal\",\"values\":[\"10.00\"]}", handler.RequestBody, StringComparison.Ordinal);
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
            objectKey: "loan application",
            cancellationToken: cancellationToken);
        Assert.Equal(
            "/api/business-object-records?page=2&pageSize=10&objectKey=loan%20application",
            handler.RequestUri!.PathAndQuery);

        await writeTools.CreateBusinessObjectRecordAsync(
            "loan_application",
            new CreateBusinessObjectRecordInput(
                "record-1",
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["applicant_name"] = ["Ada Lovelace"],
                }),
            cancellationToken);
        Assert.Equal("POST", handler.Method!.Method);
        Assert.Equal("/api/business-object-records/loan_application", handler.RequestUri!.PathAndQuery);
        Assert.Contains("\"idempotencyKey\":\"record-1\"", handler.RequestBody, StringComparison.Ordinal);

        await writeTools.SaveBusinessObjectRecordAsync(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new SaveBusinessObjectRecordInput(
                4,
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["applicant_name"] = ["Ada Lovelace"],
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
            () => guard.EnsureEnabled("PublishRuleDefinition"));

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

        guard.EnsureEnabled("PublishRuleDefinition");
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
    public async Task ApiClient_WhenApiReturnsProblemDetails_PreservesStableCodeAndFieldErrors()
    {
        using HttpClient httpClient = new(new ProblemHandler())
        {
            BaseAddress = new Uri("https://localhost:5281/"),
        };
        AxisApiClient api = new(httpClient, new FixedAccessTokenProvider("test-token"));

        AxisApiException exception = await Assert.ThrowsAsync<AxisApiException>(
            () => api.PutJsonAsync(
                "api/business-object-records/11111111-1111-1111-1111-111111111111",
                new { expectedRevision = 1, values = new { amount = new[] { "invalid" } } },
                TestContext.Current.CancellationToken));

        Assert.Equal(422, exception.StatusCode);
        Assert.Equal("businessObjects.recordInvalid", exception.ProblemCode);
        Assert.Equal("urn:axis:problem:businessObjects.recordInvalid", exception.ProblemType);
        Assert.Equal(["businessObjects.amountInvalid"], exception.FieldErrors["amount"]);
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
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
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
