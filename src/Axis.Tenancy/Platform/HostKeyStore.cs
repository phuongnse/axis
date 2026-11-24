using Npgsql;

namespace Axis.Tenancy.Platform;

/// <summary>
/// Publishes and reads the public key of a host in <c>platform.host_keys</c> over the platform database. The agent host publishes its key
/// at start, and the server reads it to answer <c>GET /api/agent-host/key</c>. The platform role that the data source connects as has
/// <c>INSERT</c> and <c>UPDATE</c> on the table and no <c>DELETE</c>, see <see cref="PlatformRoleGrants"/>.
/// </summary>
public sealed class HostKeyStore(NpgsqlDataSource platform)
{
    /// <summary>The <c>host</c> of the agent host's row.</summary>
    public const string AgentHost = "agent";

    /// <summary>Publishes <paramref name="publicKey"/> as the key of <paramref name="host"/>, replacing the key it published before.</summary>
    public async Task PublishAsync(string host, string keyId, byte[] publicKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(publicKey);
        await using var command = platform.CreateCommand(
            """
            INSERT INTO platform.host_keys (host, key_id, public_key, published_at)
            VALUES (@host, @keyId, @publicKey, @now)
            ON CONFLICT (host) DO UPDATE
            SET key_id = EXCLUDED.key_id, public_key = EXCLUDED.public_key, published_at = EXCLUDED.published_at
            """);
        command.Parameters.AddWithValue("host", host);
        command.Parameters.AddWithValue("keyId", keyId);
        command.Parameters.AddWithValue("publicKey", publicKey);
        command.Parameters.AddWithValue("now", now.UtcDateTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The key id and the public key of <paramref name="host"/>, or <c>null</c> when it has published none.</summary>
    public async Task<(string KeyId, byte[] PublicKey)?> FindAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        await using var command = platform.CreateCommand("SELECT key_id, public_key FROM platform.host_keys WHERE host = @host");
        command.Parameters.AddWithValue("host", host);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetString(0), reader.GetFieldValue<byte[]>(1)) : null;
    }
}
