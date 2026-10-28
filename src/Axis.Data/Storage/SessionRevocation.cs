using Npgsql;

namespace Axis.Data.Storage;

/// <summary>
/// Ends sessions on request by deleting their rows in <c>axis.sessions</c>. The next request on a deleted session answers 401 on
/// every replica, because each request reads the row.
/// </summary>
public static class SessionRevocation
{
    /// <summary>Deletes every session of the user, whose id must match exactly, and returns how many it deleted.</summary>
    public static async Task<int> EndForUserAsync(NpgsqlConnection connection, string userId, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand("DELETE FROM axis.sessions WHERE user_id = @user", connection);
        command.Parameters.AddWithValue("user", userId);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Deletes every session of the tenant database and returns how many it deleted.</summary>
    public static async Task<int> EndAllAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand("DELETE FROM axis.sessions", connection);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
