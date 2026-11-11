namespace Axis.Tenancy.Realms;

/// <summary>
/// What the MCP server of a tenant host, the realm's audience for it and the agent host's client of it share, see
/// <c>docs/reference/authoring-requests.md#the-agent-host</c>. The server lives in <c>Axis.Server</c>, which the agent host cannot reference, so the one pinned
/// protocol version and the URL that the token's audience names are kept here.
/// </summary>
public static class McpProtocol
{
    /// <summary>The one protocol version the MCP server speaks and its client asks for.</summary>
    public const string Version = "2025-11-25";

    /// <summary>The path of the MCP server on a tenant host.</summary>
    public const string Path = "/mcp";

    /// <summary>The URL of the MCP server on <paramref name="host"/>, which is also the audience the realm puts into the author's access token.</summary>
    public static Uri Url(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        return new Uri($"https://{host}{Path}");
    }
}
