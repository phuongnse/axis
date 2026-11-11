using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Tenancy.Realms;

namespace Axis.Agent;

/// <summary>What a tool call answered: whether the tool failed, its structured result and the texts of its content joined by new lines.</summary>
public sealed record AuthorMcpToolResult(bool IsError, JsonNode? StructuredContent, string Text);

/// <summary>
/// The one client of the tenant's MCP server, which a step uses to act as the author, see <c>docs/reference/authoring-requests.md#the-agent-host</c>.
/// It posts to <c>https://&lt;first tenant host&gt;/mcp</c> with the author's access token. It opens its session at the first call: it sends
/// <c>initialize</c>, checks the protocol version of <see cref="McpProtocol"/> and sends <c>notifications/initialized</c>. Every later message
/// carries <c>Mcp-Session-Id</c> and <c>MCP-Protocol-Version</c>. A <c>401</c> makes the client ask for a new access token once and send the same
/// message again, for each call on its own. A <c>403</c>, or a <c>401</c> after that retry, means the author's grants have ended and the client says
/// so with <see cref="AuthoringTokenEndedException"/>, which stops the request as <c>failed</c>, see
/// <c>docs/reference/authoring-requests.md#acting-for-the-author</c>. Any other failure throws <see cref="InvalidOperationException"/>, whose message names the
/// method and the status and never a token, a session id or a body. The client reads only JSON answers, because the server never streams.
/// It serves one call at a time.
/// </summary>
public sealed class AuthorMcpClient
{
    /// <summary>The name of the HTTP client that calls a tenant's MCP server.</summary>
    public const string HttpClientName = "AgentMcp";

    private const string SessionHeader = "Mcp-Session-Id";
    private const string VersionHeader = "MCP-Protocol-Version";

    private static readonly string _clientVersion = typeof(AuthorMcpClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly HttpClient _http;
    private readonly string? _host;
    private readonly Func<CancellationToken, Task<string>> _accessToken;
    private string? _token;
    private string? _session;
    private int _lastId;

    internal AuthorMcpClient(HttpClient http, string? host, Func<CancellationToken, Task<string>> accessToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(accessToken);

        _http = http;
        _host = host;
        _accessToken = accessToken;
    }

    /// <summary>The tools the server offers, as its <c>tools/list</c> answers them.</summary>
    /// <exception cref="AuthoringTokenEndedException">The server refused the author's token twice, or refused the author.</exception>
    /// <exception cref="InvalidOperationException">The tenant has no host, or the server did not answer as expected.</exception>
    public async Task<JsonArray> ListToolsAsync(CancellationToken cancellationToken)
    {
        var result = await CallAsync("tools/list", null, cancellationToken);
        return result["tools"] is JsonArray tools
            ? (JsonArray)tools.DeepClone()
            : throw new InvalidOperationException("The MCP server answered tools/list without tools.");
    }

    /// <summary>
    /// Calls <paramref name="tool"/>. A tool that fails answers a result with <see cref="AuthorMcpToolResult.IsError"/>, which is data for the step to read, not an exception.
    /// </summary>
    /// <exception cref="AuthoringTokenEndedException">The server refused the author's token twice, or refused the author.</exception>
    /// <exception cref="InvalidOperationException">The tenant has no host, or the server did not answer as expected.</exception>
    public async Task<AuthorMcpToolResult> CallToolAsync(string tool, JsonObject? arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(tool);

        var parameters = new JsonObject { ["name"] = tool };
        if (arguments is not null)
        {
            parameters["arguments"] = arguments.DeepClone();
        }

        var result = await CallAsync("tools/call", parameters, cancellationToken);
        var texts = result["content"] is JsonArray content
            ? content.OfType<JsonObject>().Select(part => part["text"] is JsonValue text && text.TryGetValue<string>(out var value) ? value : null).OfType<string>()
            : [];
        return new AuthorMcpToolResult(
            result["isError"] is JsonValue flag && flag.TryGetValue<bool>(out var isError) && isError,
            result["structuredContent"]?.DeepClone(),
            string.Join('\n', texts));
    }

    private async Task<JsonObject> CallAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            await OpenSessionAsync(cancellationToken);
        }

        return await RequestAsync(method, parameters, cancellationToken);
    }

    private async Task OpenSessionAsync(CancellationToken cancellationToken)
    {
        var (initialized, session) = await SendAsync(
            "initialize",
            new JsonObject
            {
                ["protocolVersion"] = McpProtocol.Version,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject { ["name"] = "axis-agent", ["version"] = _clientVersion },
            },
            notification: false,
            cancellationToken);
        if (initialized is not JsonObject answer
            || answer["protocolVersion"] is not JsonValue version
            || !version.TryGetValue<string>(out var protocolVersion)
            || protocolVersion != McpProtocol.Version)
        {
            throw new InvalidOperationException("The MCP server answered initialize with another protocol version.");
        }

        _session = !string.IsNullOrEmpty(session) ? session : throw new InvalidOperationException("The MCP server answered initialize without a session.");
        await SendAsync("notifications/initialized", null, notification: true, cancellationToken);
    }

    private async Task<JsonObject> RequestAsync(string method, JsonObject? parameters, CancellationToken cancellationToken) =>
        (await SendAsync(method, parameters, notification: false, cancellationToken)).Result as JsonObject
            ?? throw new InvalidOperationException($"The MCP server answered {method} without a result.");

    private async Task<(JsonNode? Result, string? Session)> SendAsync(string method, JsonObject? parameters, bool notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_host))
        {
            throw new InvalidOperationException("The tenant has no host, so the agent host cannot call its MCP server.");
        }

        var message = new JsonObject { ["jsonrpc"] = "2.0" };
        if (!notification)
        {
            message["id"] = ++_lastId;
        }

        message["method"] = method;
        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        var body = message.ToJsonString();
        var retried = false;
        _token ??= await _accessToken(cancellationToken);
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, McpProtocol.Url(_host))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            if (_session is not null)
            {
                request.Headers.Add(SessionHeader, _session);
                request.Headers.Add(VersionHeader, McpProtocol.Version);
            }

            using var response = await _http.SendAsync(request, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized when !retried:
                    // The access token lives minutes, so a long step meets this more than once, and each call gets its own retry.
                    retried = true;
                    _token = await _accessToken(cancellationToken);
                    continue;
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw new AuthoringTokenEndedException();
            }

            if (notification)
            {
                return response.StatusCode == HttpStatusCode.Accepted
                    ? (null, null)
                    : throw new InvalidOperationException($"The MCP server answered {method} with status {(int)response.StatusCode}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"The MCP server answered {method} with status {(int)response.StatusCode}.");
            }

            return (await ReadResultAsync(method, response, cancellationToken), response.Headers.TryGetValues(SessionHeader, out var values) ? values.FirstOrDefault() : null);
        }
    }

    private static async Task<JsonNode?> ReadResultAsync(string method, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType != "application/json")
        {
            throw new InvalidOperationException($"The MCP server answered {method} with something other than JSON.");
        }

        JsonNode? answer;
        try
        {
            answer = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"The MCP server answered {method} with something other than JSON.");
        }

        if (answer is not JsonObject reply)
        {
            throw new InvalidOperationException($"The MCP server answered {method} with something other than a JSON-RPC answer.");
        }

        if (reply["error"] is JsonObject error)
        {
            var code = error["code"] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;
            throw new InvalidOperationException($"The MCP server answered {method} with the JSON-RPC error {code}.");
        }

        return reply["result"];
    }
}
