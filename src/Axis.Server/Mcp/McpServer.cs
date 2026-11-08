using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Studio;
using Axis.Studio;
using Axis.Tenancy;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Server.Mcp;

/// <summary>
/// The MCP server of a tenant host, at <c>/mcp</c>. It speaks the Streamable HTTP transport as a server that offers no
/// server-sent stream, so <c>GET /mcp</c> is a <c>405</c>, and it takes <c>initialize</c>, <c>notifications/*</c>,
/// <c>ping</c>, <c>tools/list</c> and <c>tools/call</c>, see <c>docs/reference/mcp-server.md#tools</c>. The route takes only a bearer token that was issued for the host's own <c>/mcp</c> URL (see
/// <see cref="McpCaller"/>), and only from a person with the author role. A request is checked in order: token (401),
/// author role (403), a body of at most 1 MiB (413), JSON body (400), and then by method. <c>initialize</c> opens a session when the protocol version is
/// <see cref="ProtocolVersion"/>, and answers its id in <c>Mcp-Session-Id</c>. Every other call needs that id (400 when missing, 404
/// when it is not this person's session on this tenant) and the same <c>MCP-Protocol-Version</c> (400).
/// </summary>
internal static partial class McpServer
{
    /// <summary>The one protocol version the server speaks.</summary>
    public const string ProtocolVersion = "2025-11-25";

    public const string Path = "/mcp";

    public const string MetadataPath = "/.well-known/oauth-protected-resource/mcp";

    /// <summary>The scope that the metadata names, which the author role's person asks the identity provider for.</summary>
    public const string Scope = RealmSync.AuthoringScope;

    private const string SessionHeader = "Mcp-Session-Id";
    private const string VersionHeader = "MCP-Protocol-Version";
    private const int MaxBodyBytes = 1024 * 1024;
    private const string ListApplicationsTool = "list_applications";
    private const string UnknownTool = "unknown";

    private const int ParseError = -32700;
    private const int InvalidRequest = -32600;
    private const int MethodNotFound = -32601;
    private const int InvalidParams = -32602;

    /// <summary>The names of the tools the server serves, which the tools table of docs/reference/mcp-server.md lists without a planned marker.</summary>
    internal static readonly IReadOnlyList<string> Tools = [ListApplicationsTool];

    private static readonly string _serverVersion =
        typeof(McpServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";

    public static void MapMcpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Path, (HttpContext context) =>
        {
            context.Response.Headers.Allow = "POST";
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
        });
        endpoints.MapPost(Path, PostAsync).WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes));
    }

    /// <summary>
    /// Refuses a request before any MCP method runs. On a preview tenant's host both MCP routes answer <c>404</c>, so a
    /// preview serves no MCP server. On the MCP route a request whose <c>Origin</c> is not the host's own origin is a <c>403</c>,
    /// which keeps a web page of another site from driving a person's client. A request without an <c>Origin</c> passes.
    /// Runs before authentication, so a refused request does no sign-in work.
    /// </summary>
    public static IApplicationBuilder UseMcpGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var metadata = context.GetEndpoint()?.Metadata;
            var route = metadata?.GetMetadata<McpRoute>() is not null;
            if (route || metadata?.GetMetadata<McpMetadataRoute>() is not null)
            {
                var tenant = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current;
                if (tenant is not null && StudioPreview.IsPreview(context.RequestServices.GetRequiredService<ITenantCatalog>(), tenant.TenantId))
                {
                    await Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nothing is served at this path on this host.")
                        .ExecuteAsync(context);
                    return;
                }
            }

            if (route
                && context.Request.Headers.Origin.ToString() is { Length: > 0 } origin
                && !string.Equals(origin, $"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase))
            {
                await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "The request comes from another origin.")
                    .ExecuteAsync(context);
                return;
            }

            await next(context);
        });

    /// <summary>
    /// Maps the protected-resource metadata of RFC 9728. It names the tenant's identity provider as the authorization
    /// server, so a client finds where to get a token. A tenant with no authority has none to name and answers <c>404</c>.
    /// </summary>
    public static void MapMcpMetadata(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(MetadataPath, (HttpContext context, ITenantContextAccessor tenants, TenantSignInSettings settings) =>
        {
            var authority = tenants.Current is { } tenant ? settings.Find(tenant.TenantId)?.Authority : null;
            return string.IsNullOrWhiteSpace(authority)
                ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no identity provider.")
                : Results.Json(new JsonObject
                {
                    ["resource"] = McpCaller.Resource(context.Request),
                    ["authorization_servers"] = new JsonArray(authority),
                    ["scopes_supported"] = new JsonArray(Scope),
                    ["bearer_methods_supported"] = new JsonArray("header"),
                });
        }).WithMetadata(McpMetadataRoute.Instance);

    private static async Task<IResult> PostAsync(HttpContext context, ITenantContextAccessor tenants, StudioOptions studio, McpSession sessions, ILoggerFactory loggers)
    {
        var tenantId = tenants.Current?.TenantId ?? throw new InvalidOperationException("No tenant context is set.");
        if (EndpointCaller.Read(context.User) is not { } caller || !studio.ForTenant(tenantId).Allows(caller.Roles))
        {
            return Grants.Forbidden();
        }

        var (body, tooLarge) = await ReadBodyAsync(context.Request);
        if (tooLarge)
        {
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "The request body is larger than 1 MiB.");
        }

        if (body is null)
        {
            return Error(StatusCodes.Status400BadRequest, null, ParseError, "The body is not JSON.");
        }

        if (body is not JsonObject message || message["method"] is not JsonValue method || !method.TryGetValue<string>(out var name))
        {
            return Error(StatusCodes.Status400BadRequest, null, InvalidRequest, "The body is not one JSON-RPC request or notification.");
        }

        var id = message["id"]?.DeepClone();
        if (name == "initialize")
        {
            var requested = message["params"] is JsonObject parameters && parameters["protocolVersion"] is JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : null;
            if (requested != ProtocolVersion)
            {
                return Error(
                    StatusCodes.Status400BadRequest,
                    id,
                    InvalidParams,
                    "The protocol version is not supported.",
                    new JsonObject { ["supported"] = new JsonArray(ProtocolVersion), ["requested"] = requested });
            }

            context.Response.Headers[SessionHeader] = sessions.Create(tenantId, new McpSessionData(caller.Id, caller.ClientId, ProtocolVersion));
            return Result(id, new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = "Axis", ["version"] = _serverVersion },
            });
        }

        var sessionId = context.Request.Headers[SessionHeader].ToString();
        if (sessionId.Length == 0)
        {
            return Error(StatusCodes.Status400BadRequest, id, InvalidRequest, $"The {SessionHeader} header is missing.");
        }

        if (sessions.TryRead(tenantId, sessionId) is not { } session
            || session.Subject != caller.Id
            || session.ClientId != caller.ClientId)
        {
            return Error(StatusCodes.Status404NotFound, id, InvalidRequest, "The session does not exist.");
        }

        if (context.Request.Headers[VersionHeader].ToString() != session.Version)
        {
            return Error(StatusCodes.Status400BadRequest, id, InvalidRequest, $"The {VersionHeader} header is missing or is not the session's version.");
        }

        if (name.StartsWith("notifications/", StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status202Accepted);
        }

        return name switch
        {
            "ping" => Result(id, new JsonObject()),
            "tools/list" => Result(id, ListTools()),
            "tools/call" => CallTool(message["params"], id, tenantId, caller.ClientId, studio, loggers.CreateLogger(typeof(McpServer).FullName!)),
            _ => Error(StatusCodes.Status200OK, id, MethodNotFound, "The method is not supported."),
        };
    }

    /// <summary>The <c>tools/list</c> answer, which offers each tool the server serves.</summary>
    internal static JsonObject ListTools() =>
        new()
        {
            ["tools"] = new JsonArray(new JsonObject
            {
                ["name"] = ListApplicationsTool,
                ["description"] = "Lists the applications you may author.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject(),
                    ["additionalProperties"] = false,
                },
            }),
        };

    /// <summary>
    /// Runs one tool. A call that names no tool is the JSON-RPC error <c>-32602</c>. An unknown tool and bad input are a tool
    /// error, a result marked <c>isError</c> whose text is a fixed problem title. Each call writes one log entry, which
    /// names the tool only when it is known, never the input or the output.
    /// </summary>
    private static IResult CallTool(JsonNode? parameters, JsonNode? id, string tenantId, string? clientId, StudioOptions studio, ILogger logger)
    {
        if (parameters is not JsonObject call || call["name"] is not JsonValue nameValue || !nameValue.TryGetValue<string>(out var tool))
        {
            LogToolCall(logger, UnknownTool, clientId, "invalid params");
            return Error(StatusCodes.Status200OK, id, InvalidParams, "The call names no tool.");
        }

        if (!Tools.Contains(tool, StringComparer.Ordinal))
        {
            LogToolCall(logger, UnknownTool, clientId, "tool error");
            return ToolError(id, "The tool does not exist.");
        }

        if (call["arguments"] is { } arguments && arguments is not JsonObject { Count: 0 })
        {
            LogToolCall(logger, tool, clientId, "tool error");
            return ToolError(id, "The tool takes no input.");
        }

        var output = JsonSerializer.SerializeToNode(StudioEndpoints.ListApplications(studio.ForTenant(tenantId)), JsonSerializerOptions.Web);
        LogToolCall(logger, tool, clientId, "succeeded");
        return Result(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = output!.ToJsonString() }),
            ["structuredContent"] = output,
            ["isError"] = false,
        });
    }

    private static IResult ToolError(JsonNode? id, string title) =>
        Result(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = title }),
            ["isError"] = true,
        });

    [LoggerMessage(LogLevel.Information, "MCP tool {Tool} was called by client {ClientId}: {Outcome}.")]
    private static partial void LogToolCall(ILogger logger, string tool, string? clientId, string outcome);

    /// <summary>The body as JSON, which is null when it is empty or not JSON, and whether it is larger than the limit.</summary>
    private static async Task<(JsonNode? Body, bool TooLarge)> ReadBodyAsync(HttpRequest request)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        try
        {
            int read;
            while ((read = await request.Body.ReadAsync(chunk, request.HttpContext.RequestAborted)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes)
                {
                    return (null, true);
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, true);
        }

        try
        {
            return (JsonNode.Parse(buffer.ToArray()), false);
        }
        catch (JsonException)
        {
            return (null, false);
        }
    }

    private static IResult Result(JsonNode? id, JsonNode result) =>
        Results.Json(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });

    private static IResult Error(int status, JsonNode? id, int code, string text, JsonNode? data = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = text };
        if (data is not null)
        {
            error["data"] = data;
        }

        return Results.Json(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = error }, statusCode: status);
    }
}

/// <summary>The endpoint metadata of the protected-resource metadata route, which the guard reads to keep a preview tenant off it.</summary>
internal sealed class McpMetadataRoute
{
    public static readonly McpMetadataRoute Instance = new();

    private McpMetadataRoute()
    {
    }
}
