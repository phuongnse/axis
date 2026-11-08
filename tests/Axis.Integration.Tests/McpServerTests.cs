using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Loading;
using Axis.Server.Mcp;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>
/// The MCP server at <c>/mcp</c> on a tenant host, against the development realm's Keycloak. Tenant <c>a</c> is served at
/// <c>localhost</c>, tenant <c>b</c> at <c>b.localhost</c> and the preview tenant of <c>a</c> at <c>preview.localhost</c>.
/// A token counts only when it was issued for the <c>/mcp</c> URL of the host that gets it. The database is unreachable, so a
/// request that reaches it fails the test.
/// </summary>
public sealed class McpServerTests(KeycloakFixture keycloak) : IAsyncLifetime
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";
    private const string Host = "localhost";
    private const string OtherHost = "b.localhost";
    private const string PreviewHost = "preview.localhost";
    private const string Version = "2025-11-25";
    private const string Metadata = "/.well-known/oauth-protected-resource/mcp";

    private Uri? _authority;
    private string? _applications;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private readonly LogCollector _logs = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_author_with_a_token_for_the_mcp_url_opens_a_session_and_the_session_is_checked_on_later_calls()
    {
        var token = await RequestTokenAsync("author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret);

        using var initialized = await PostAsync(Host, token, Initialize(Version));

        Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);
        using var result = JsonDocument.Parse(await initialized.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(Version, result.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.Equal("Axis", result.RootElement.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
        var session = Assert.Single(initialized.Headers.GetValues("Mcp-Session-Id"));

        using var ping = await PostAsync(Host, token, Ping(), session, Version);
        Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
        using var pong = JsonDocument.Parse(await ping.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(JsonValueKind.Object, pong.RootElement.GetProperty("result").ValueKind);

        using var notified = await PostAsync(Host, token, """{"jsonrpc":"2.0","method":"notifications/initialized"}""", session, Version);
        Assert.Equal(HttpStatusCode.Accepted, notified.StatusCode);

        using var wrongVersion = await PostAsync(Host, token, Ping(), session, "2025-06-18");
        Assert.Equal(HttpStatusCode.BadRequest, wrongVersion.StatusCode);

        using var noVersion = await PostAsync(Host, token, Ping(), session, null);
        Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);

        using var noSession = await PostAsync(Host, token, Ping(), null, Version);
        Assert.Equal(HttpStatusCode.BadRequest, noSession.StatusCode);

        var altered = session[..(session.Length / 2)] + (session[session.Length / 2] == 'A' ? 'B' : 'A') + session[(session.Length / 2 + 1)..];
        using var forged = await PostAsync(Host, token, Ping(), altered, Version);
        Assert.Equal(HttpStatusCode.NotFound, forged.StatusCode);
    }

    [Fact]
    public async Task An_initialize_for_another_protocol_version_is_a_400_with_the_supported_version_and_no_session()
    {
        var token = await RequestTokenAsync("author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret);

        using var response = await PostAsync(Host, token, Initialize("2024-11-05"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.Contains("Mcp-Session-Id"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        var error = body.RootElement.GetProperty("error");
        Assert.Equal(-32602, error.GetProperty("code").GetInt32());
        Assert.Equal(Version, error.GetProperty("data").GetProperty("supported")[0].GetString());
    }

    [Fact]
    public async Task A_body_that_is_not_one_request_is_a_400_and_an_unknown_method_is_a_jsonrpc_error()
    {
        var token = await RequestTokenAsync("author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret);
        using var initialized = await PostAsync(Host, token, Initialize(Version));
        var session = Assert.Single(initialized.Headers.GetValues("Mcp-Session-Id"));

        using var notJson = await PostAsync(Host, token, "{");
        using var batch = await PostAsync(Host, token, "[]");
        using var unknown = await PostAsync(Host, token, """{"jsonrpc":"2.0","id":3,"method":"resources/list"}""", session, Version);

        Assert.Equal(HttpStatusCode.BadRequest, notJson.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, batch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        using var body = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal(-32601, body.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task An_author_lists_the_tools_and_list_applications_answers_the_applications_of_the_studio_folders()
    {
        var (token, session) = await OpenSessionAsync();

        using var tools = await PostAsync(Host, token, """{"jsonrpc":"2.0","id":3,"method":"tools/list"}""", session, Version);
        using var call = await PostAsync(Host, token, ToolCall("list_applications"), session, Version);

        Assert.Equal(HttpStatusCode.OK, tools.StatusCode);
        using var listed = JsonDocument.Parse(await tools.Content.ReadAsStringAsync(CancellationToken));
        var offered = listed.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToList();
        Assert.Equal(McpServer.Tools, offered.Select(tool => tool.GetProperty("name").GetString()!).ToList());
        Assert.All(offered, tool => Assert.Equal("object", tool.GetProperty("inputSchema").GetProperty("type").GetString()));

        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        using var called = JsonDocument.Parse(await call.Content.ReadAsStringAsync(CancellationToken));
        var result = called.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        var names = result.GetProperty("structuredContent").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["LeaveRequests", "PurchaseRequests"], names);
    }

    [Fact]
    public async Task An_unknown_tool_and_input_for_list_applications_answer_a_tool_error_with_a_problem_title_only()
    {
        var (token, session) = await OpenSessionAsync();

        using var unknown = await PostAsync(Host, token, ToolCall("delete_everything"), session, Version);
        using var input = await PostAsync(Host, token, ToolCall("list_applications", """{"x":1}"""), session, Version);

        foreach (var response in new[] { unknown, input })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync(CancellationToken);
            using var body = JsonDocument.Parse(text);
            var result = body.RootElement.GetProperty("result");
            Assert.True(result.GetProperty("isError").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("content")[0].GetProperty("text").GetString()));
            Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
            Assert.DoesNotContain(_applications!, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Read_reference_answers_the_embedded_schema_and_the_page_text_of_a_resource_kind()
    {
        var (token, session) = await OpenSessionAsync();

        using var call = await PostAsync(Host, token, ToolCall("read_reference", """{"topic":"entity"}"""), session, Version);

        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        var body = JsonNode.Parse(await call.Content.ReadAsStringAsync(CancellationToken))!;
        var result = body["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        var answer = result["structuredContent"]!;
        Assert.Equal("entity", answer["topic"]!.GetValue<string>());
        Assert.True(ResourceSchemas.TryGetText("entity", out var schema));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(schema), answer["schema"]));
        var page = Assert.Single(answer["pages"]!.AsArray(), entry => entry!["page"]!.GetValue<string>() == "resource-files.md");
        Assert.Equal(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "reference", "resource-files.md")),
            page!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Read_reference_for_an_unknown_topic_answers_a_fixed_tool_error_that_holds_no_topic_and_no_path()
    {
        var (token, session) = await OpenSessionAsync();
        var topic = $"unknown-{Guid.NewGuid():N}";

        using var call = await PostAsync(Host, token, ToolCall("read_reference", $$"""{"topic":"{{topic}}"}"""), session, Version);

        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        var text = await call.Content.ReadAsStringAsync(CancellationToken);
        using var body = JsonDocument.Parse(text);
        var result = body.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        Assert.Equal(McpReference.NoTopic, result.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.DoesNotContain(topic, text, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
        Assert.DoesNotContain(_applications!, text, StringComparison.Ordinal);
        Assert.DoesNotContain(AppContext.BaseDirectory, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_over_1_MiB_is_a_413()
    {
        var token = await RequestTokenAsync("author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret);
        var body = "\"" + new string(' ', 1024 * 1024 - 1) + "\"";
        Assert.Equal(1024 * 1024 + 1, Encoding.UTF8.GetByteCount(body));

        using var response = await PostAsync(Host, token, body);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task A_tool_call_writes_one_log_entry_with_the_tool_the_client_and_the_outcome_and_no_content()
    {
        var (token, session) = await OpenSessionAsync();

        using var call = await PostAsync(Host, token, ToolCall("list_applications"), session, Version);

        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        var entry = Assert.Single(_logs.Records, record => record.Message.StartsWith("MCP tool", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains("list_applications", entry.Message, StringComparison.Ordinal);
        Assert.Contains(KeycloakFixture.McpClientId, entry.Message, StringComparison.Ordinal);
        Assert.Contains("succeeded", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("LeaveRequests", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_without_the_mcp_audience_or_sent_to_another_tenants_host_is_a_401()
    {
        var plain = await RequestTokenAsync("author", KeycloakFixture.ClientId, KeycloakFixture.ClientSecret);
        var mcp = await RequestTokenAsync("author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret);

        using var wrongAudience = await PostAsync(Host, plain, Initialize(Version));
        using var otherTenant = await PostAsync(OtherHost, mcp, Initialize(Version));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherTenant.StatusCode);
        Assert.StartsWith("Bearer resource_metadata=", otherTenant.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_person_without_the_author_role_and_a_foreign_origin_are_a_403()
    {
        var requester = await RequestTokenAsync("requester", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret);
        var author = await RequestTokenAsync("author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret);

        using var noRole = await PostAsync(Host, requester, Initialize(Version));
        using var foreign = await PostAsync(Host, author, Initialize(Version), origin: "https://evil.example");
        using var own = await PostAsync(Host, author, Initialize(Version), origin: "https://localhost");

        Assert.Equal(HttpStatusCode.Forbidden, noRole.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task The_metadata_names_the_realm_and_an_unauthenticated_call_points_to_it_and_a_get_is_a_405()
    {
        using var metadata = await SendAsync(HttpMethod.Get, Host, Metadata, bearer: null);
        using var anonymous = await PostAsync(Host, null, Initialize(Version));
        var author = await RequestTokenAsync("author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret);
        using var get = await SendAsync(HttpMethod.Get, Host, "/mcp", author);

        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        using var body = JsonDocument.Parse(await metadata.Content.ReadAsStringAsync(CancellationToken));
        Assert.Equal("https://localhost/mcp", body.RootElement.GetProperty("resource").GetString());
        Assert.Equal(_authority!.ToString(), body.RootElement.GetProperty("authorization_servers")[0].GetString());
        Assert.Equal("axis:authoring", body.RootElement.GetProperty("scopes_supported")[0].GetString());
        Assert.Equal("header", body.RootElement.GetProperty("bearer_methods_supported")[0].GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(
            $"Bearer resource_metadata=\"https://localhost{Metadata}\"",
            anonymous.Headers.WwwAuthenticate.ToString());

        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
    }

    [Fact]
    public async Task A_preview_tenants_host_answers_404_on_the_mcp_route_and_the_metadata()
    {
        var author = await RequestTokenAsync("author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret);

        using var mcp = await PostAsync(PreviewHost, author, Initialize(Version));
        using var metadata = await SendAsync(HttpMethod.Get, PreviewHost, Metadata, bearer: null);

        Assert.Equal(HttpStatusCode.NotFound, mcp.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);
    }

    public async ValueTask InitializeAsync()
    {
        _authority = await keycloak.StartAsync();
        _applications = Directory.CreateTempSubdirectory("axis-mcp-").FullName;
        foreach (var sample in new[] { "leave-requests", "purchase-requests" })
        {
            StudioReleaseHistoryTests.CopyDirectory(Path.Combine(AppContext.BaseDirectory, "Samples", sample), Path.Combine(_applications, sample));
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.AddProvider(_logs));
            builder.UseTestPlatform(UnreachableDatabase);
            builder.WithoutPlatformMigration();
            foreach (var (tenant, host) in new[] { ("a", Host), ("b", OtherHost) })
            {
                builder.UseSetting($"Tenants:{tenant}:Hosts:0", host);
                builder.UseSetting($"Tenants:{tenant}:ConnectionString", UnreachableDatabase);
                builder.UseSetting($"Tenants:{tenant}:SchemaConnectionString", UnreachableDatabase);
                builder.UseSetting($"Tenants:{tenant}:Oidc:Authority", _authority.ToString());
                builder.UseSetting($"Tenants:{tenant}:Oidc:ClientId", KeycloakFixture.ClientId);
                builder.UseSetting($"Tenants:{tenant}:Oidc:ClientSecret", KeycloakFixture.ClientSecret);
            }

            builder.UseSetting("Tenants:a-preview:Hosts:0", PreviewHost);
            builder.UseSetting("Tenants:a-preview:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:a-preview:SchemaConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:a-preview:PreviewOf", "a");
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseSetting("Studio:ApplicationsPath", _applications);
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_applications is not null)
        {
            Directory.Delete(_applications, recursive: true);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }

    private static string Initialize(string version) => McpTestCalls.Initialize(version);

    private static string ToolCall(string tool, string? arguments = null) => McpTestCalls.ToolCall(tool, arguments);

    private Task<string> RequestTokenAsync(string user, string clientId, string clientSecret) =>
        McpTestCalls.RequestTokenAsync(_authority!, user, clientId, clientSecret, CancellationToken);

    private Task<HttpResponseMessage> PostAsync(string host, string? bearer, string body, string? session = null, string? version = null, string? origin = null) =>
        McpTestCalls.PostAsync(Client(), host, bearer, body, session, version, origin, CancellationToken);

    private async Task<(string Token, string Session)> OpenSessionAsync()
    {
        var token = await RequestTokenAsync("author", KeycloakFixture.McpClientId, KeycloakFixture.McpClientSecret);
        using var initialized = await PostAsync(Host, token, Initialize(Version));
        return (token, Assert.Single(initialized.Headers.GetValues("Mcp-Session-Id")));
    }

    private static string Ping() => """{"jsonrpc":"2.0","id":2,"method":"ping"}""";

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string host, string path, string? bearer)
    {
        var request = new HttpRequestMessage(method, path);
        McpTestCalls.Prepare(request, host, bearer);
        return Client().SendAsync(request, CancellationToken);
    }

    private HttpClient Client() => _client ?? throw new InvalidOperationException("The test host is not started.");
}
