using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the receipts of trigger starts in <c>axis.trigger_starts</c> over a connection to a
/// tenant database. A receipt is keyed by the trigger, the source of the cause and the record, so
/// each trigger starts its process at most once per cause and record.
/// </summary>
internal static class TriggerStarts
{
    /// <summary>The outcome of a receipt whose start happened.</summary>
    internal const string Started = "started";

    /// <summary>The outcome of a receipt whose start was skipped.</summary>
    internal const string Skipped = "skipped";

    /// <summary>
    /// Stores a <c>skipped</c> receipt unless one exists for the key. Returns
    /// <see langword="false"/> when the key is taken, so the trigger already handled the cause. A
    /// concurrent insert of the same key waits for the first one's transaction, so only one of them
    /// inserts.
    /// </summary>
    internal static async Task<bool> TryInsertAsync(
        NpgsqlTransaction transaction,
        Guid triggerId,
        string source,
        Guid subjectId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.trigger_starts (trigger_id, source, subject_id, outcome, process_instance_id, created_at)
            VALUES (@trigger, @source, @subject, @outcome, NULL, now())
            ON CONFLICT (trigger_id, source, subject_id) DO NOTHING
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("trigger", triggerId);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("subject", subjectId);
        command.Parameters.AddWithValue("outcome", Skipped);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Marks the receipt of the key <c>started</c>, with the instance the start inserted.</summary>
    internal static async Task MarkStartedAsync(
        NpgsqlTransaction transaction,
        Guid triggerId,
        string source,
        Guid subjectId,
        Guid instanceId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.trigger_starts SET outcome = @outcome, process_instance_id = @instance
            WHERE trigger_id = @trigger AND source = @source AND subject_id = @subject
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("outcome", Started);
        command.Parameters.AddWithValue("instance", instanceId);
        command.Parameters.AddWithValue("trigger", triggerId);
        command.Parameters.AddWithValue("source", source);
        command.Parameters.AddWithValue("subject", subjectId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlConnection Connection(NpgsqlTransaction transaction) =>
        transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
}
