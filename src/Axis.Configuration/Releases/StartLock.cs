using Npgsql;

namespace Axis.Configuration.Releases;

/// <summary>
/// The application's start lock, a transaction-level advisory lock keyed on the application id.
/// A transaction that starts a process instance takes it shared, so starts do not wait on each
/// other. An activation with a pending migration takes it exclusively and holds it until it
/// commits. A start then either commits before the activation counts open work, or waits and pins
/// the new release. The key is a hash of the application id, so another key can collide with it.
/// A collision only makes a start wait longer. It never lets one through.
/// <para>
/// A start takes the lock as the first statement of its transaction, before it reads or writes
/// any entity table. A start that waits on an activation while it holds a table lock that the
/// activation needs would deadlock. The transaction must stay at READ COMMITTED, so the
/// statement after the lock sees the commit of the activation that held it.
/// </para>
/// </summary>
public static class StartLock
{
    /// <summary>The key of <paramref name="applicationId"/>: the XOR of the two 64-bit halves of the id.</summary>
    public static long Key(Guid applicationId)
    {
        var bytes = applicationId.ToByteArray();
        return BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8);
    }

    /// <summary>
    /// Takes the start lock shared, waiting while an activation holds it exclusively, then reads
    /// the id of the release active for <paramref name="applicationId"/>. The result is
    /// <see langword="null"/> when no release is active.
    /// </summary>
    public static async Task<Guid?> TakeSharedAsync(NpgsqlTransaction transaction, Guid applicationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var connection = transaction.Connection ?? throw new InvalidOperationException("The transaction has no connection.");
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock_shared(@key)", connection, transaction))
        {
            command.Parameters.AddWithValue("key", Key(applicationId));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var read = new NpgsqlCommand("SELECT release_id FROM axis.active_releases WHERE application_id = @application", connection, transaction);
        read.Parameters.AddWithValue("application", applicationId);
        return await read.ExecuteScalarAsync(cancellationToken) is Guid releaseId ? releaseId : null;
    }

    /// <summary>Takes the start lock exclusively, waiting for every open start, and holds it until the transaction ends.</summary>
    public static async Task TakeExclusiveAsync(NpgsqlTransaction transaction, Guid applicationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var connection = transaction.Connection ?? throw new InvalidOperationException("The transaction has no connection.");
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", connection, transaction);
        command.Parameters.AddWithValue("key", Key(applicationId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
