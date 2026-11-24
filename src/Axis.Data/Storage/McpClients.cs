using Npgsql;

namespace Axis.Data.Storage;

/// <summary>
/// Reads and writes <c>axis.mcp_clients</c> of the tenant database, see <c>docs/reference/mcp-server.md#sign-in</c>. The last use is also read by <c>docs/reference/user-administration.md#outside-mcp-clients</c>, which also sets the revoke time. It also
/// reads and writes <c>axis.mcp_consents</c>, which remembers the consents whose platform audit record is written, see
/// <c>docs/reference/mcp-server.md#audit</c>. The database clock is the only clock used.
/// </summary>
public static class McpClients
{
    private const string ReadSql =
        """
        SELECT c."revoked_at", (c."last_used_at" IS NULL OR c."last_used_at" < now() - interval '1 minute'),
          EXISTS (SELECT 1 FROM "axis"."mcp_consents" s WHERE s."client_id" = @client AND s."user_id" = @user
                  AND s."recorded_at" > COALESCE(c."revoked_at", '-infinity'))
        FROM (SELECT 1) AS one LEFT JOIN "axis"."mcp_clients" c ON c."client_id" = @client
        """;

    private const string ClaimConsentSql =
        """
        INSERT INTO "axis"."mcp_consents" ("client_id", "user_id", "recorded_at") VALUES (@client, @user, now())
        ON CONFLICT ("client_id", "user_id") DO UPDATE SET "recorded_at" = now()
        WHERE "mcp_consents"."recorded_at" <= COALESCE((SELECT "revoked_at" FROM "axis"."mcp_clients" WHERE "client_id" = @client), '-infinity')
        RETURNING 1
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

    private const string RevokeSql =
        """
        INSERT INTO "axis"."mcp_clients" ("client_id", "revoked_at") VALUES (@client, now())
        ON CONFLICT ("client_id") DO UPDATE SET "revoked_at" = now()
        """;

    /// <summary>
    /// Reads what the server needs to know of a client on a call. A client without a row was never revoked, and its use is
    /// stale.
    /// </summary>
    /// <param name="userId">The person who calls, whose consent record the read looks up.</param>
    public static async Task<McpClientUse> ReadAsync(NpgsqlConnection connection, string clientId, string userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var command = new NpgsqlCommand(ReadSql, connection);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // The join on a one-row source answers one row for a client with no row too.
        await reader.ReadAsync(cancellationToken);
        return new McpClientUse(
            await reader.IsDBNullAsync(0, cancellationToken) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken),
            reader.GetBoolean(1),
            reader.GetBoolean(2));
    }

    /// <summary>
    /// Claims the right to write the platform audit record of the person's consent to the client, in <paramref name="transaction"/>. It
    /// returns true and moves the record time to now when there is no row, or when the row is at or before the client's revoke time.
    /// It returns false when the consent is recorded. A second claim at the same time waits for the first to commit and then returns false.
    /// </summary>
    public static async Task<bool> ClaimConsentAsync(NpgsqlTransaction transaction, string clientId, string userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var command = new NpgsqlCommand(ClaimConsentSql, transaction.Connection, transaction);
        command.Parameters.AddWithValue("client", clientId);
        command.Parameters.AddWithValue("user", userId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
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

    /// <summary>
    /// Sets the revoke time of the client to now in <paramref name="transaction"/>, and adds the row when there is none. A repeat moves the
    /// time to now.
    /// </summary>
    public static async Task RevokeAsync(NpgsqlTransaction transaction, string clientId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        await using var command = new NpgsqlCommand(RevokeSql, transaction.Connection, transaction);
        command.Parameters.AddWithValue("client", clientId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>What the server reads of an MCP client on a call.</summary>
/// <param name="RevokedAt">The time the client's consent was revoked, or null when it never was.</param>
/// <param name="UseIsStale">Whether the client's last use is unset or over a minute old, so a call should record a use.</param>
/// <param name="ConsentRecorded">Whether the platform audit record of the person's consent to the client is written, and the client's consent was not revoked since.</param>
public sealed record McpClientUse(DateTimeOffset? RevokedAt, bool UseIsStale, bool ConsentRecorded);
