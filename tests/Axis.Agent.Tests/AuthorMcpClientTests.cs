using System.Net;
using System.Text.Json.Nodes;
using Axis.Tenancy.Realms;

namespace Axis.Agent.Tests;

/// <summary>
/// The client of the tenant's MCP server opens its session at the first call and acts with the author's access token, see
/// <c>docs/reference/authoring-requests.md#the-agent-host</c>. A <c>401</c> refreshes the token once for each call, and a <c>403</c> or a second
/// <c>401</c> ends the author's token, see <c>docs/reference/authoring-requests.md#acting-for-the-author</c>.
/// </summary>
public sealed class AuthorMcpClientTests
{
    private const string Url = "https://a.example.test/mcp";
    private const string Session = "session-1";

    private static string Initialized =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 1,
            ["result"] = new JsonObject { ["protocolVersion"] = McpProtocol.Version, ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() } },
        }.ToJsonString();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static void WithSession(HttpResponseMessage response) => response.Headers.Add("Mcp-Session-Id", Session);

    private static RecordingHandler OpenedSession(RecordingHandler handler) =>
        handler.Answer(HttpStatusCode.OK, Initialized, WithSession).Answer(HttpStatusCode.Accepted);

    private static (AuthorMcpClient Client, List<string> Tokens) NewClient(RecordingHandler handler, string? host = "a.example.test")
    {
        var tokens = new List<string>();
        var client = new AuthorMcpClient(
            new HttpClient(handler),
            host,
            _ =>
            {
                tokens.Add($"token-{tokens.Count + 1}");
                return Task.FromResult(tokens[^1]);
            });
        return (client, tokens);
    }

    [Fact]
    public async Task A_list_and_a_call_open_one_session_and_later_messages_carry_the_session_and_the_protocol_version()
    {
        var handler = OpenedSession(new RecordingHandler())
            .Answer(HttpStatusCode.OK, """{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"list_resources"}]}}""")
            .Answer(HttpStatusCode.OK, """{"jsonrpc":"2.0","id":3,"result":{"content":[{"type":"text","text":"one"},{"type":"text","text":"two"}],"structuredContent":{"items":[]},"isError":false}}""");
        var (client, tokens) = NewClient(handler);

        var tools = await client.ListToolsAsync(CancellationToken);
        var result = await client.CallToolAsync("list_resources", new JsonObject { ["application"] = "PurchaseRequests" }, CancellationToken);

        Assert.Equal("list_resources", tools.Single()!["name"]!.GetValue<string>());
        Assert.False(result.IsError);
        Assert.Equal("one\ntwo", result.Text);
        Assert.NotNull(result.StructuredContent?["items"]);
        Assert.Equal(["token-1"], tokens);
        Assert.Equal(
            ["initialize", "notifications/initialized", "tools/list", "tools/call"],
            handler.Requests.Select(request => JsonNode.Parse(request.Body)!["method"]!.GetValue<string>()));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(new Uri(Url), request.Uri);
            Assert.Equal("Bearer token-1", request.Headers["Authorization"]);
            Assert.Equal("application/json,text/event-stream", request.Headers["Accept"]);
            Assert.False(request.Headers.ContainsKey("Origin"));
        });
        Assert.Equal(
            [false, true, true, true],
            handler.Requests.Select(request => request.Headers.ContainsKey("Mcp-Session-Id")));
        Assert.All(handler.Requests.Skip(1), request =>
        {
            Assert.Equal(Session, request.Headers["Mcp-Session-Id"]);
            Assert.Equal(McpProtocol.Version, request.Headers["MCP-Protocol-Version"]);
        });
        Assert.False(handler.Requests[0].Headers.ContainsKey("MCP-Protocol-Version"));
        Assert.Equal(McpProtocol.Version, JsonNode.Parse(handler.Requests[0].Body)!["params"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal([1, 2, 3], handler.Requests.Where(request => JsonNode.Parse(request.Body)!["id"] is not null).Select(request => JsonNode.Parse(request.Body)!["id"]!.GetValue<int>()));
    }

    [Fact]
    public async Task A_401_asks_for_a_new_token_once_and_sends_the_same_message_again_with_it()
    {
        var handler = OpenedSession(new RecordingHandler())
            .Answer(HttpStatusCode.Unauthorized)
            .Answer(HttpStatusCode.OK, """{"jsonrpc":"2.0","id":2,"result":{"tools":[]}}""");
        var (client, tokens) = NewClient(handler);

        var tools = await client.ListToolsAsync(CancellationToken);

        Assert.Empty(tools);
        Assert.Equal(["token-1", "token-2"], tokens);
        Assert.Equal(["Bearer token-1", "Bearer token-1", "Bearer token-1", "Bearer token-2"], handler.Requests.Select(request => request.Headers["Authorization"]));
        Assert.Equal(handler.Requests[2].Body, handler.Requests[3].Body);
        Assert.Equal(Session, handler.Requests[3].Headers["Mcp-Session-Id"]);
    }

    [Fact]
    public async Task Each_call_gets_its_own_retry_after_a_401()
    {
        var handler = OpenedSession(new RecordingHandler())
            .Answer(HttpStatusCode.Unauthorized)
            .Answer(HttpStatusCode.OK, """{"jsonrpc":"2.0","id":2,"result":{"tools":[]}}""")
            .Answer(HttpStatusCode.Unauthorized)
            .Answer(HttpStatusCode.OK, """{"jsonrpc":"2.0","id":3,"result":{"tools":[]}}""");
        var (client, tokens) = NewClient(handler);

        await client.ListToolsAsync(CancellationToken);
        await client.ListToolsAsync(CancellationToken);

        Assert.Equal(["token-1", "token-2", "token-3"], tokens);
    }

    [Fact]
    public async Task A_second_401_on_the_same_call_ends_the_authors_token()
    {
        var handler = OpenedSession(new RecordingHandler()).Answer(HttpStatusCode.Unauthorized).Answer(HttpStatusCode.Unauthorized);
        var (client, tokens) = NewClient(handler);

        await Assert.ThrowsAsync<AuthoringTokenEndedException>(() => client.ListToolsAsync(CancellationToken));

        Assert.Equal(["token-1", "token-2"], tokens);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task A_403_ends_the_authors_token_with_no_retry()
    {
        var handler = OpenedSession(new RecordingHandler()).Answer(HttpStatusCode.Forbidden);
        var (client, tokens) = NewClient(handler);

        await Assert.ThrowsAsync<AuthoringTokenEndedException>(() => client.CallToolAsync("list_resources", null, CancellationToken));

        Assert.Equal(["token-1"], tokens);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task A_tool_that_fails_comes_back_as_a_result_with_IsError()
    {
        var handler = OpenedSession(new RecordingHandler())
            .Answer(HttpStatusCode.OK, """{"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"The application does not exist."}],"isError":true}}""");
        var (client, _) = NewClient(handler);

        var result = await client.CallToolAsync("list_resources", null, CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("The application does not exist.", result.Text);
        Assert.Null(result.StructuredContent);
    }

    [Fact]
    public async Task A_tenant_with_no_host_throws_at_the_first_call_and_asks_for_no_token()
    {
        var handler = new RecordingHandler();
        var (client, tokens) = NewClient(handler, host: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListToolsAsync(CancellationToken));

        Assert.Empty(tokens);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task An_unexpected_status_and_a_JSON_RPC_error_throw_without_naming_the_token_the_session_or_the_body()
    {
        var failed = OpenedSession(new RecordingHandler()).Answer(HttpStatusCode.NotFound, "secret-body");
        var rpc = OpenedSession(new RecordingHandler()).Answer(HttpStatusCode.OK, """{"jsonrpc":"2.0","id":2,"error":{"code":-32601,"message":"secret-body"}}""");

        var status = await Assert.ThrowsAsync<InvalidOperationException>(() => NewClient(failed).Client.ListToolsAsync(CancellationToken));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => NewClient(rpc).Client.ListToolsAsync(CancellationToken));

        Assert.Contains("404", status.Message, StringComparison.Ordinal);
        Assert.Contains("-32601", error.Message, StringComparison.Ordinal);
        Assert.All([status.Message, error.Message], message =>
        {
            Assert.Contains("tools/list", message, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-body", message, StringComparison.Ordinal);
            Assert.DoesNotContain("token-1", message, StringComparison.Ordinal);
            Assert.DoesNotContain(Session, message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task An_initialize_answer_with_no_session_or_another_protocol_version_throws()
    {
        var noSession = new RecordingHandler().Answer(HttpStatusCode.OK, Initialized);
        var otherVersion = new RecordingHandler().Answer(HttpStatusCode.OK, """{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2024-01-01"}}""", WithSession);

        await Assert.ThrowsAsync<InvalidOperationException>(() => NewClient(noSession).Client.ListToolsAsync(CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => NewClient(otherVersion).Client.ListToolsAsync(CancellationToken));
    }
}
