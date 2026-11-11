using Npgsql;

namespace Axis.Data.Storage;

/// <summary>
/// Reads and writes <c>axis.mcp_clients</c> of the tenant database, see <c>docs/reference/mcp-server.md#sign-in</c>. The last use is also read by <c>docs/reference/user-administration.md#outside-mcp-clients</c>. The
/// database clock is the only clock used.
/// </summary>
public static class McpClients
{
    private const string ReadSql =
        """
        SELECT "revoked_at", ("last_used_at" IS NULL OR "last_used_at" < now() - interval '1 minute')
        FROM "axis"."mcp_clients" WHERE "client_id" = @client
        """;

    private const string ListLastUsesSql =
        """
        SELECT "client_id", "last_used_at" FROM "axis"."mcp_clients" WHERE "client_id" = ANY(@clients)
        """;

    private const string RecordUseSql =
        """
        INSERT INTO "axis"."mcp_clients" ("client_id", "last_used_at") VALUES (@client, now())
        ON CONFLICT ("client_id") DO UPDATE SET "last_used_at" = now()
        WHERE "mcp_clients"."last_used_at" IS NULL OR "mcp_clients"."last_used_at" < now() - interval '1 minute'
        """;

    /// <summary>
    /// Reads what the server needs to know of a client on a call. A client without a row was never revoked, and its use is
    /// stale.
    /// </summary>
    public static async Task<McpClientUse> ReadAsync(NpgsqlConnection connection, string clientId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        await using var command = new NpgsqlCommand(ReadSql, connection);
        command.Parameters.AddWithValue("client", clientId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new McpClientUse(null, true);
        }

        return new McpClientUse(
            await reader.IsDBNullAsync(0, cancellationToken) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken),
            reader.GetBoolean(1));
    }

    /// <summary>
    /// Reads the last use of each of the clients. A client with no row, or a row with no last use, has a <c>null</c> use.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, DateTimeOffset?>> ListLastUsesAsync(
        NpgsqlConnection connection, IReadOnlyCollection<string> clientIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(clientIds);

        var uses = new Dictionary<string, DateTimeOffset?>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(ListLastUsesSql, connection);
        command.Parameters.AddWithValue("clients", clientIds.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            uses[reader.GetString(0)] = await reader.IsDBNullAsync(1, cancellationToken) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(1, cancellationToken);
        }

        return uses;
    }

    /// <summary>
    /// Records a use of the client now. It adds the row when there is none and changes nothing when the last use is under a
    /// minute old, so a busy client does not write on every call.
    /// </summary>
    public static async Task RecordUseAsync(NpgsqlConnection connection, string clientId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        await using var command = new NpgsqlCommand(RecordUseSql, connection);
        command.Parameters.AddWithValue("client", clientId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>What the server reads of an MCP client on a call.</summary>
/// <param name="RevokedAt">The time the client's consent was revoked, or null when it never was.</param>
/// <param name="UseIsStale">Whether the client's last use is unset or over a minute old, so a call should record a use.</param>
public sealed record McpClientUse(DateTimeOffset? RevokedAt, bool UseIsStale);
