using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// A record's event lock, a transaction-level advisory lock keyed on the record's id. A
/// <c>waitEvent</c> step takes it before it looks for an occurrence to go on with, and the wake-up
/// that event delivery runs for a waiting instance takes the same lock. An occurrence then cannot
/// arrive between the step's search and its move to waiting. The key is a hash of the record id, so
/// another key can collide with it. A collision only makes a transaction wait longer.
/// <para>
/// A transaction takes the lock before it locks or updates the instance row, so two transactions
/// never wait on each other in the opposite order.
/// </para>
/// </summary>
internal static class RecordEventLock
{
    /// <summary>The key of <paramref name="subjectId"/>: the XOR of the two 64-bit halves of the id, as the start lock builds its key.</summary>
    internal static long Key(Guid subjectId)
    {
        var bytes = subjectId.ToByteArray();
        return BitConverter.ToInt64(bytes, 0) ^ BitConverter.ToInt64(bytes, 8);
    }

    /// <summary>Takes the lock, waiting while another transaction holds it, and holds it until the transaction ends.</summary>
    internal static async Task TakeAsync(NpgsqlTransaction transaction, Guid subjectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(@key)",
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("key", Key(subjectId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
