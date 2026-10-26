using Npgsql;

namespace Axis.Data.Storage;

/// <summary>
/// The two limits of a session row in <c>axis.sessions</c>, shared by the server, which refuses a row past
/// either, and the worker, which deletes the rows that are past either.
/// </summary>
public static class SessionLifetime
{
    /// <summary>The time without a request after which a session ends.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(1);

    /// <summary>The time after sign-in after which a session ends, however active it is.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(8);

    /// <summary>
    /// Deletes the rows that are past a limit at <paramref name="now"/> and returns how many it deleted. This is the
    /// exact opposite of the check that keeps a session live.
    /// </summary>
    public static async Task<int> DeleteExpiredAsync(NpgsqlConnection connection, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM axis.sessions WHERE signed_in_at <= @signedInBefore OR last_request_at <= @activeBefore",
            connection);
        command.Parameters.AddWithValue("signedInBefore", now - MaxLifetime);
        command.Parameters.AddWithValue("activeBefore", now - IdleTimeout);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
