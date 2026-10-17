using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the rows of <c>axis.process_call_attempts</c>: one for each attempt and each reconciliation
/// lookup of a connector call or a package call. The outcome transaction of the attempt or lookup
/// writes its row, in the same transaction that updates the call.
/// </summary>
internal static class CallAttempts
{
    /// <summary>The kind of a call that <c>axis.connector_calls</c> holds.</summary>
    public const string Connector = "connector";

    /// <summary>The kind of a call that <c>axis.package_calls</c> holds.</summary>
    public const string Package = "package";

    /// <summary>The kind of a row for one send of a connector call or one run of a package operation.</summary>
    public const string Attempt = "attempt";

    /// <summary>The kind of a row for one lookup of reconciliation.</summary>
    public const string Lookup = "lookup";

    /// <summary>The attempt reached the system or the operation and it did what was asked.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>The attempt is known not to have acted.</summary>
    public const string Retryable = "retryable";

    /// <summary>The attempt may have acted.</summary>
    public const string Unknown = "unknown";

    /// <summary>The attempt or lookup failed.</summary>
    public const string Failed = "failed";

    /// <summary>The lookup found the call.</summary>
    public const string Found = "found";

    /// <summary>The lookup found no call.</summary>
    public const string NotFound = "notFound";

    /// <summary>
    /// Inserts the row for <paramref name="attempt"/> of the call <paramref name="callId"/>, finished at
    /// the transaction's time. A row with a number that exists already violates the unique index.
    /// </summary>
    internal static async Task InsertAsync(
        NpgsqlTransaction transaction,
        string callKind,
        Guid callId,
        CallAttempt attempt,
        CancellationToken cancellationToken)
    {
        var connection = transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.process_call_attempts
                (id, call_kind, call_id, kind, number, started_at, finished_at, outcome, http_status, error)
            VALUES (
                @id, @callKind, @callId, @kind,
                COALESCE(@number, (SELECT COALESCE(max(number), 0) + 1 FROM axis.process_call_attempts WHERE call_id = @callId AND kind = @kind)),
                @startedAt, now(), @outcome, @status, @error)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("callKind", callKind);
        command.Parameters.AddWithValue("callId", callId);
        command.Parameters.AddWithValue("kind", attempt.Kind);
        command.Parameters.Add(new NpgsqlParameter("number", NpgsqlDbType.Integer) { Value = (object?)attempt.Number ?? DBNull.Value });
        command.Parameters.AddWithValue("startedAt", attempt.StartedAt);
        command.Parameters.AddWithValue("outcome", attempt.Outcome);
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Integer) { Value = (object?)attempt.HttpStatus ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Text) { Value = (object?)attempt.Error ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>An attempt or lookup that a call handler records.</summary>
/// <param name="Kind"><see cref="CallAttempts.Attempt"/> or <see cref="CallAttempts.Lookup"/>.</param>
/// <param name="Number">The number to record, or <see langword="null"/> to take the next number of the call's <paramref name="Kind"/>.</param>
/// <param name="StartedAt">When the attempt or lookup started.</param>
/// <param name="Outcome">The outcome class.</param>
/// <param name="HttpStatus">The HTTP status, or <see langword="null"/> when none answered.</param>
/// <param name="Error">A fixed text. It holds no body, no exception message and no secret.</param>
internal sealed record CallAttempt(string Kind, int? Number, DateTimeOffset StartedAt, string Outcome, int? HttpStatus, string? Error);
