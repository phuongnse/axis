namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.mcp_clients</c>: an MCP client that called the MCP server, keyed by its client id. The row holds the
/// time of the client's last use and the time its consent was revoked, see <c>docs/reference/mcp-server.md#sign-in</c>.
/// </summary>
public sealed class McpClientRow
{
    /// <summary>The client's id, which is the <c>azp</c> claim of its tokens.</summary>
    public required string ClientId { get; init; }

    /// <summary>The time of the client's last call, written at most once a minute. It is null before the first call.</summary>
    public DateTimeOffset? LastUsedAt { get; init; }

    /// <summary>
    /// The time the client's consent was revoked. The server refuses a token issued at or before it. It is null when the consent
    /// was never revoked.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; init; }
}
