namespace Axis.Server.Authentication;

/// <summary>
/// A person who calls the MCP server at <c>/mcp</c> with a bearer token from their tenant's identity provider. A route
/// opts in with <see cref="McpRoute"/>. On such a route the token is judged against the audience <see cref="Resource"/>, which
/// the request's own scheme and host give, the session cookie is never read or renewed, and no CSRF header is needed.
/// </summary>
internal static class McpCaller
{
    /// <summary>Forwards to the MCP bearer scheme of the request's tenant.</summary>
    public const string Scheme = "Axis.McpCaller";

    public const string SchemePrefix = "mcp-";

    public static string SchemeName(string tenantId) => $"{SchemePrefix}{tenantId}";

    /// <summary>Whether the request's route carries <see cref="McpRoute"/>.</summary>
    public static bool Applies(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<McpRoute>() is not null;

    /// <summary>The URL of the MCP server on the request's host, which a token's audience must name.</summary>
    public static string Resource(HttpRequest request) => $"{request.Scheme}://{request.Host}/mcp";
}

/// <summary>
/// The endpoint metadata that marks the MCP server's route. The scheme choice, the CSRF exemption, the guard and the route
/// tests all read it, so none of them matches paths.
/// </summary>
internal sealed class McpRoute
{
    public static readonly McpRoute Instance = new();

    private McpRoute()
    {
    }
}
