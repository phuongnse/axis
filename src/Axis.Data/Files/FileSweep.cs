using Npgsql;

namespace Axis.Data.Files;

/// <summary>
/// The clean-up of files that nobody needs any more, run by the worker. A <c>deleted</c> file is purged at once.
/// A <c>pending</c> file is purged when its upload is more than <see cref="PendingLifetime"/> old.
/// </summary>
public static class FileSweep
{
    /// <summary>The time after which a file that no record attached is abandoned.</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// Deletes the <c>deleted</c> files, and the <c>pending</c> files uploaded before <paramref name="now"/> minus
    /// <see cref="PendingLifetime"/>, and returns how many it deleted. The foreign key from
    /// <c>axis.file_contents</c> cascades, so the bytes go with their file. An <c>attached</c> file is never touched.
    /// </summary>
    public static async Task<int> PurgeAsync(NpgsqlConnection connection, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand(
            """
            DELETE FROM "axis"."files"
            WHERE "state" = 'deleted' OR ("state" = 'pending' AND "uploaded_at" < @pendingBefore)
            """,
            connection);
        command.Parameters.AddWithValue("pendingBefore", now - PendingLifetime);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
