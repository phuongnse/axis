namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.mcp_consents</c>: the platform audit record of one person's consent to one MCP client, which the server has
/// written, see <c>docs/reference/mcp-server.md#audit</c>. A row at or before the client's revoke time counts as not written.
/// </summary>
public sealed class McpConsentRow
{
    /// <summary>The client's id, which is the <c>azp</c> claim of its tokens.</summary>
    public required string ClientId { get; init; }

    /// <summary>The person's user id claim.</summary>
    public required string UserId { get; init; }

    /// <summary>The time of the call that wrote the record, from the database clock.</summary>
    public DateTimeOffset RecordedAt { get; init; }
}
