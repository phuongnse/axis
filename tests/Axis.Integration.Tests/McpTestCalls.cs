using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Server.Tests;
using Axis.Studio;

namespace Axis.Integration.Tests;

/// <summary>The requests that the tests of the MCP server at <c>/mcp</c> send, against the development realm's Keycloak.</summary>
internal static class McpTestCalls
{
    /// <summary>The access token of <paramref name="user"/> that <paramref name="clientId"/> gets from the realm at <paramref name="authority"/>.</summary>
    public static async Task<string> RequestTokenAsync(Uri authority, string user, string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["username"] = user,
            ["password"] = KeycloakFixture.Password,
            ["otp"] = DevelopmentTotp.Code(user),
        });
        using var response = await client.PostAsync(new Uri($"{authority}/protocol/openid-connect/token"), form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var tokens = JsonDocument.Parse(body);
        return tokens.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>The <c>initialize</c> request for protocol <paramref name="version"/>.</summary>
    public static string Initialize(string version) =>
        JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { protocolVersion = version, capabilities = new { }, clientInfo = new { name = "test", version = "1" } },
        });

    /// <summary>The <c>tools/call</c> request of <paramref name="tool"/> with <paramref name="arguments"/> as JSON, or none.</summary>
    public static string ToolCall(string tool, string? arguments = null)
    {
        var parameters = new JsonObject { ["name"] = tool };
        if (arguments is not null)
        {
            parameters["arguments"] = JsonNode.Parse(arguments);
        }

        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 3, ["method"] = "tools/call", ["params"] = parameters }.ToJsonString();
    }

    /// <summary>The arguments of <paramref name="arguments"/> with the <c>cursor</c> of a page added, as a paged tool call reads its next page.</summary>
    public static string WithCursor(string arguments, string cursor)
    {
        var withCursor = JsonNode.Parse(arguments)!.AsObject();
        withCursor["cursor"] = cursor;
        return withCursor.ToJsonString();
    }

    /// <summary>
    /// The parts of a paged tool output read to the end and joined, parsed as the output's JSON text. <paramref name="readPage"/>
    /// answers the page that a cursor names, or the first page for null. Each page is at most <see cref="StudioDiffPages.PageBytes"/>,
    /// and the output has more than one page and at most ten.
    /// </summary>
    public static async Task<JsonNode> ReadPagesAsync(Func<string?, Task<JsonNode>> readPage)
    {
        var parts = new List<string>();
        string? cursor = null;
        do
        {
            var page = await readPage(cursor);
            Assert.True(Encoding.UTF8.GetByteCount(page.ToJsonString()) <= StudioDiffPages.PageBytes);
            parts.Add(page["part"]!.GetValue<string>());
            cursor = page["cursor"]?.GetValue<string>();
            Assert.True(parts.Count <= 10, "The pages do not end.");
        }
        while (cursor is not null);

        Assert.True(parts.Count > 1);
        return JsonNode.Parse(string.Concat(parts))!;
    }

    /// <summary>Posts <paramref name="body"/> to <c>/mcp</c> on <paramref name="host"/>, with the headers that are not null.</summary>
    public static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string host,
        string? bearer,
        string body,
        string? session,
        string? version,
        string? origin,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        Prepare(request, host, bearer);
        if (session is not null)
        {
            request.Headers.Add("Mcp-Session-Id", session);
        }

        if (version is not null)
        {
            request.Headers.Add("MCP-Protocol-Version", version);
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return client.SendAsync(request, cancellationToken);
    }

    /// <summary>Sets the host and the bearer token of <paramref name="request"/>.</summary>
    public static void Prepare(HttpRequestMessage request, string host, string? bearer)
    {
        request.Headers.Host = host;
        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }
    }
}
