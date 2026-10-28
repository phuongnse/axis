using Npgsql;

namespace Axis.Data.Storage;

/// <summary>
/// A person's own rows in <c>axis.sessions</c>: listing the live ones and ending them. Every query names the user id,
/// so a row of another person is never read or deleted.
/// </summary>
public static class OwnSessions
{
    /// <summary>One live session of a person. <see cref="IdHash"/> is the hash that keys the row.</summary>
    public sealed record Entry(byte[] IdHash, DateTimeOffset SignedInAt, DateTimeOffset LastRequestAt);

    /// <summary>The user's sessions that are within both limits of <see cref="SessionLifetime"/> at <paramref name="now"/>, newest sign-in first.</summary>
    public static async Task<IReadOnlyList<Entry>> ListAsync(
        NpgsqlConnection connection, string userId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT id_hash, signed_in_at, last_request_at FROM axis.sessions
            WHERE user_id = @user AND signed_in_at > @signedInAfter AND last_request_at > @activeAfter
            ORDER BY signed_in_at DESC, id_hash
            """,
            connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("signedInAfter", now - SessionLifetime.MaxLifetime);
        command.Parameters.AddWithValue("activeAfter", now - SessionLifetime.IdleTimeout);
        var entries = new List<Entry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new Entry(reader.GetFieldValue<byte[]>(0), reader.GetFieldValue<DateTimeOffset>(1), reader.GetFieldValue<DateTimeOffset>(2)));
        }

        return entries;
    }

    /// <summary>Deletes the user's live session with this hash and returns how many it deleted, 0 or 1.</summary>
    public static async Task<int> EndOneAsync(
        NpgsqlConnection connection, string userId, byte[] idHash, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM axis.sessions
            WHERE id_hash = @hash AND user_id = @user AND signed_in_at > @signedInAfter AND last_request_at > @activeAfter
            """,
            connection);
        command.Parameters.AddWithValue("hash", idHash);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("signedInAfter", now - SessionLifetime.MaxLifetime);
        command.Parameters.AddWithValue("activeAfter", now - SessionLifetime.IdleTimeout);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Deletes every session of the user except the one with <paramref name="keepHash"/> and returns how many it deleted.</summary>
    public static async Task<int> EndOthersAsync(
        NpgsqlConnection connection, string userId, byte[] keepHash, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM axis.sessions WHERE user_id = @user AND id_hash <> @keep",
            connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("keep", keepHash);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
