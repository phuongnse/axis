using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
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
/// <c>ping</c>, <c>tools/list</c> and <c>tools/call</c>, see <c>docs/reference/mcp-server.md#tools</c>. The tools are
/// <c>list_applications</c> and the read tools of <see cref="McpReadTools"/>. The route takes only a bearer token that was issued for the host's own <c>/mcp</c> URL (see
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
    private const string ListResourcesTool = "list_resources";
    private const string ReadResourceTool = "read_resource";
    private const string ReadModuleResourceTool = "read_module_resource";
    private const string ReadNamesTool = "read_names";
    private const string InputMismatch = "The input does not match the tool's input schema.";
    private const string UnknownTool = "unknown";

    private const int ParseError = -32700;
    private const int InvalidRequest = -32600;
    private const int MethodNotFound = -32601;
    private const int InvalidParams = -32602;

    private static readonly ToolProperty _applicationInput = new("application", "The name of the application.", true);
    private static readonly ToolProperty _draftInput = new("draft", "The id of one of your drafts of the application. Leave it out to read the working folder.", false);
    private static readonly ToolProperty _idInput = new("id", "The id of the resource.", true);
    private static readonly ToolProperty _moduleInput = new("module", "The id of a module the application pins.", true);

    private static readonly ToolDefinition[] _definitions =
    [
        new(ListApplicationsTool, "Lists the applications you may author.", []),
        new(
            ListResourcesTool,
            "Lists the resources of an application: each one's id, kind, name, file, version, titles and how many references use it. "
            + "Reads the working folder, or your draft when you name one.",
            [_applicationInput, _draftInput]),
        new(
            ReadResourceTool,
            "Reads one resource of an application by its id: its content and version. Reads the working folder, or your draft when you name one.",
            [_applicationInput, _idInput, _draftInput]),
        new(
            ReadModuleResourceTool,
            "Reads one resource of the module version the application pins, with the application's customization layer applied, "
            + "and tells which layer applied and what it added, changed and removed.",
            [_applicationInput, _moduleInput, _idInput]),
        new(
            ReadNamesTool,
            "Lists every name the application defines, module names included. Reads the working folder, or your draft when you name one.",
            [_applicationInput, _draftInput]),
    ];

    /// <summary>The names of the tools the server serves, which the tools table of docs/reference/mcp-server.md lists without a planned marker.</summary>
    internal static readonly IReadOnlyList<string> Tools = [.. _definitions.Select(definition => definition.Name)];

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

    private static async Task<IResult> PostAsync(
        HttpContext context,
        ITenantContextAccessor tenants,
        StudioOptions studio,
        McpSession sessions,
        ILoggerFactory loggers,
        PackageCatalog packages,
        ModuleCatalog modules,
        IStudioWriteLock writeLock)
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
            "tools/call" => await CallToolAsync(
                message["params"],
                id,
                new McpReadContext(studio.ForTenant(tenantId), caller, packages, modules, writeLock),
                loggers.CreateLogger(typeof(McpServer).FullName!),
                context.RequestAborted),
            _ => Error(StatusCodes.Status200OK, id, MethodNotFound, "The method is not supported."),
        };
    }

    /// <summary>The <c>tools/list</c> answer, which offers each tool the server serves.</summary>
    internal static JsonObject ListTools() =>
        new() { ["tools"] = new JsonArray([.. _definitions.Select(definition => (JsonNode)definition.Describe())]) };

    /// <summary>
    /// Runs one tool. A call that names no tool is the JSON-RPC error <c>-32602</c>. An unknown tool, input that breaks the tool's
    /// input schema and a refused call are a tool error, a result marked <c>isError</c> whose text is a problem title of the
    /// authoring API or a fixed one. Each call writes one log entry, which names the tool only when it is known, never the input
    /// or the output.
    /// </summary>
    private static async Task<IResult> CallToolAsync(JsonNode? parameters, JsonNode? id, McpReadContext context, ILogger logger, CancellationToken ct)
    {
        var clientId = context.Caller.ClientId;
        if (parameters is not JsonObject call || call["name"] is not JsonValue nameValue || !nameValue.TryGetValue<string>(out var tool))
        {
            LogToolCall(logger, UnknownTool, clientId, "invalid params");
            return Error(StatusCodes.Status200OK, id, InvalidParams, "The call names no tool.");
        }

        if (_definitions.FirstOrDefault(definition => definition.Name == tool) is not { } definition)
        {
            LogToolCall(logger, UnknownTool, clientId, "tool error");
            return ToolError(id, "The tool does not exist.");
        }

        if (definition.Read(call["arguments"]) is not { } arguments)
        {
            LogToolCall(logger, tool, clientId, "tool error");
            return ToolError(id, definition.Properties.Count == 0 ? "The tool takes no input." : InputMismatch);
        }

        var (output, error) = await RunAsync(tool, arguments, context, ct);
        if (error is not null)
        {
            LogToolCall(logger, tool, clientId, "tool error");
            return ToolError(id, error);
        }

        var node = JsonSerializer.SerializeToNode(output, JsonSerializerOptions.Web);
        LogToolCall(logger, tool, clientId, "succeeded");
        return Result(id, new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = node!.ToJsonString() }),
            ["structuredContent"] = node,
            ["isError"] = false,
        });
    }

    // The tool's output, which is a JSON object, or the text of its tool error. The arguments are checked against the tool's properties.
    private static async Task<(object? Output, string? Error)> RunAsync(
        string tool, IReadOnlyDictionary<string, string> arguments, McpReadContext context, CancellationToken ct)
    {
        arguments.TryGetValue(_draftInput.Name, out var draft);
        return tool switch
        {
            ListApplicationsTool => (StudioEndpoints.ListApplications(context.Options), null),
            ListResourcesTool => await McpReadTools.ListResourcesAsync(context, arguments[_applicationInput.Name], draft, ct),
            ReadResourceTool => await McpReadTools.ReadResourceAsync(context, arguments[_applicationInput.Name], arguments[_idInput.Name], draft, ct),
            ReadModuleResourceTool => McpReadTools.ReadModuleResource(context, arguments[_applicationInput.Name], arguments[_moduleInput.Name], arguments[_idInput.Name]),
            ReadNamesTool => await McpReadTools.ReadNamesAsync(context, arguments[_applicationInput.Name], draft, ct),
            _ => throw new InvalidOperationException("The tool has no implementation."),
        };
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

/// <summary>One string property of a tool's input: its name, what it holds and whether a call must give it.</summary>
internal sealed record ToolProperty(string Name, string Description, bool Required);

/// <summary>A tool of the MCP server: its name, its description and the string properties of its input.</summary>
internal sealed record ToolDefinition(string Name, string Description, IReadOnlyList<ToolProperty> Properties)
{
    /// <summary>The <c>tools/list</c> entry: the name, the description and an input schema that lists exactly the properties.</summary>
    public JsonObject Describe()
    {
        var properties = new JsonObject();
        foreach (var property in Properties)
        {
            properties[property.Name] = new JsonObject { ["type"] = "string", ["description"] = property.Description };
        }

        return new JsonObject
        {
            ["name"] = Name,
            ["description"] = Description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray([.. Properties.Where(property => property.Required).Select(property => (JsonNode)property.Name)]),
                ["additionalProperties"] = false,
            },
        };
    }

    /// <summary>
    /// The arguments by property name, or null when they break the input schema: a key that is not a property, a required
    /// property that is missing or a value that is not a string. A missing <c>arguments</c> is none.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Read(JsonNode? arguments)
    {
        if (arguments is not null and not JsonObject)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in arguments?.AsObject() ?? [])
        {
            if (!Properties.Any(property => property.Name == key) || value is not JsonValue text || !text.TryGetValue<string>(out var content))
            {
                return null;
            }

            values[key] = content;
        }

        return Properties.All(property => !property.Required || values.ContainsKey(property.Name)) ? values : null;
    }
}
