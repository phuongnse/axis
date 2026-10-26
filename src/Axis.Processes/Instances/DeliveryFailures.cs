using System.Data.Common;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Counts the failures of delivery targets in <c>axis.delivery_failures</c> and writes their dead
/// letters in <c>axis.dead_letters</c>, over a connection to a tenant database. The caller runs each
/// call in a transaction of its own, after the failed one has rolled back.
/// </summary>
internal static class DeliveryFailures
{
    /// <summary>The kind of an event delivery to one <c>async</c> trigger.</summary>
    internal const string EventDeliverKind = ProcessStarts.EventDeliverWorkItemKind;

    /// <summary>The kind of a schedule trigger's fan-out of one occurrence.</summary>
    internal const string TriggerFanOutKind = ProcessStarts.TriggerFanOutWorkItemKind;

    /// <summary>The state of a dead letter that waits for an operator.</summary>
    internal const string Open = DeadLetters.Open;

    private static readonly TimeSpan _firstBackoff = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan _longestBackoff = TimeSpan.FromHours(1);

    /// <summary>
    /// The wait after the <paramref name="attempts"/>th failure: 30 seconds doubled for each earlier
    /// failure, at most one hour.
    /// </summary>
    internal static TimeSpan Backoff(int attempts)
    {
        // 30 s doubled 7 times is 64 minutes, so no later count needs the shift.
        var doublings = Math.Clamp(attempts - 1, 0, 7);
        var delay = _firstBackoff * (1L << doublings);
        return delay < _longestBackoff ? delay : _longestBackoff;
    }

    /// <summary>
    /// The fixed text that stands for a failure: the type of the exception and, for a database error,
    /// its SQLSTATE. It never holds the exception message, which can quote a payload value.
    /// </summary>
    internal static string ErrorText(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var type = exception.GetType().FullName ?? exception.GetType().Name;
        return exception is DbException { SqlState: { } state } ? $"{type} with SQLSTATE {state}" : type;
    }

    /// <summary>
    /// Adds one to the count of the target and stores <paramref name="error"/> as its last error.
    /// Returns the count after the failure.
    /// </summary>
    internal static async Task<int> CountAsync(
        NpgsqlTransaction transaction,
        string kind,
        string target,
        string error,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.delivery_failures (kind, target, attempts, last_error, updated_at)
            VALUES (@kind, @target, 1, @error, now())
            ON CONFLICT (kind, target) DO UPDATE
            SET attempts = delivery_failures.attempts + 1, last_error = excluded.last_error, updated_at = now()
            RETURNING attempts
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("target", target);
        command.Parameters.AddWithValue("error", error);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Writes an <c>open</c> dead letter for an event delivery to one trigger and returns its id.</summary>
    internal static async Task<Guid> InsertDeadLetterAsync(
        NpgsqlTransaction transaction,
        Guid applicationId,
        Guid processId,
        Guid eventId,
        Guid triggerId,
        int attempts,
        string error,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.dead_letters (id, kind, application_id, process_id, event_id, trigger_id, attempts, last_error, state, created_at)
            VALUES (@id, @kind, @application, @process, @event, @trigger, @attempts, @error, @state, now())
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("kind", EventDeliverKind);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("process", processId);
        command.Parameters.AddWithValue("event", eventId);
        command.Parameters.AddWithValue("trigger", triggerId);
        command.Parameters.AddWithValue("attempts", attempts);
        command.Parameters.AddWithValue("error", error);
        command.Parameters.AddWithValue("state", Open);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    /// <summary>Writes an <c>open</c> dead letter for the fan-out of one occurrence of a schedule trigger and returns its id.</summary>
    internal static async Task<Guid> InsertFanOutDeadLetterAsync(
        NpgsqlTransaction transaction,
        Guid applicationId,
        Guid processId,
        Guid triggerId,
        DateTimeOffset occurrenceAt,
        int attempts,
        string error,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.dead_letters (id, kind, application_id, process_id, trigger_id, occurrence_at, attempts, last_error, state, created_at)
            VALUES (@id, @kind, @application, @process, @trigger, @occurrence, @attempts, @error, @state, now())
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("kind", TriggerFanOutKind);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("process", processId);
        command.Parameters.AddWithValue("trigger", triggerId);
        command.Parameters.AddWithValue("occurrence", occurrenceAt);
        command.Parameters.AddWithValue("attempts", attempts);
        command.Parameters.AddWithValue("error", error);
        command.Parameters.AddWithValue("state", Open);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    private static NpgsqlConnection Connection(NpgsqlTransaction transaction) =>
        transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
}
