using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Reads and writes the connector calls that <c>callConnector</c> steps stage over a connection to
/// a tenant database, and names their states. A call's id is its operation identity.
/// </summary>
public static class ConnectorCalls
{
    /// <summary>The state of a call that waits for its next attempt.</summary>
    public const string Pending = "pending";

    /// <summary>The state of a call whose attempt a worker is sending.</summary>
    public const string Sending = "sending";

    /// <summary>
    /// The state of a call whose last attempt has an unknown outcome and that is not sent again,
    /// so the system may have acted. It waits for reconciliation.
    /// </summary>
    public const string Unknown = "unknown";

    /// <summary>The state of a call that settled with an answer that fits its output.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>The state of a call that settled with an error.</summary>
    public const string Failed = "failed";

    /// <summary>
    /// Inserts <paramref name="call"/> as <c>pending</c> with no attempts. It is created at the
    /// transaction's time. A second call for the same instance and step violates the unique index.
    /// </summary>
    internal static async Task InsertAsync(NpgsqlTransaction transaction, NewConnectorCall call, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.connector_calls
                (id, process_instance_id, step, release_id, connector, operation, state, attempts, input, created_at)
            VALUES (@id, @instance, @step, @release, @connector, @operation, @state, 0, @input, now())
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", call.Id);
        command.Parameters.AddWithValue("instance", call.ProcessInstanceId);
        command.Parameters.AddWithValue("step", call.Step);
        command.Parameters.AddWithValue("release", call.ReleaseId);
        command.Parameters.AddWithValue("connector", call.Connector);
        command.Parameters.AddWithValue("operation", call.Operation);
        command.Parameters.AddWithValue("state", Pending);
        command.Parameters.Add(new NpgsqlParameter("input", NpgsqlDbType.Jsonb) { Value = call.Input.ToJsonString() });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the call <paramref name="id"/> and locks it until the transaction ends, or returns
    /// <see langword="null"/> when there is none.
    /// </summary>
    internal static async Task<LoadedConnectorCall?> LoadAsync(NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT process_instance_id, step, release_id, connector, operation, state, attempts, input::text
            FROM axis.connector_calls WHERE id = @id FOR UPDATE
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LoadedConnectorCall(
            id,
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            await reader.IsDBNullAsync(7, cancellationToken) ? null : reader.GetString(7));
    }

    /// <summary>
    /// Marks the <c>pending</c> call <paramref name="id"/> <c>sending</c> and counts the attempt.
    /// Returns the attempts made, this one included, or <see langword="null"/> when the call is not pending.
    /// </summary>
    internal static async Task<int?> MarkSendingAsync(NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.connector_calls SET state = @sending, attempts = attempts + 1
            WHERE id = @id AND state = @pending
            RETURNING attempts
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("sending", Sending);
        command.Parameters.AddWithValue("pending", Pending);
        return await command.ExecuteScalarAsync(cancellationToken) is int attempts ? attempts : null;
    }

    /// <summary>
    /// Sets the call <paramref name="id"/> back to <c>pending</c> for its next attempt, with the last
    /// attempt's status and error. Its stored input is kept, so the retry sends it again.
    /// </summary>
    internal static Task RetryAsync(NpgsqlTransaction transaction, Guid id, int? lastStatus, string? lastError, CancellationToken cancellationToken) =>
        SetStateAsync(transaction, id, Pending, lastStatus, lastError, cancellationToken);

    /// <summary>
    /// Sets the call <paramref name="id"/> to <c>unknown</c>, with the last attempt's status and
    /// error. Its stored input is kept, since a later retry or reconciliation may still need it.
    /// </summary>
    internal static Task MarkUnknownAsync(NpgsqlTransaction transaction, Guid id, int? lastStatus, string? lastError, CancellationToken cancellationToken) =>
        SetStateAsync(transaction, id, Unknown, lastStatus, lastError, cancellationToken);

    /// <summary>
    /// Settles the call <paramref name="id"/> as <paramref name="state"/>, <c>succeeded</c> or
    /// <c>failed</c>, at the transaction's time, with the last attempt's status and error. Its
    /// stored input is cleared.
    /// </summary>
    internal static async Task SettleAsync(
        NpgsqlTransaction transaction,
        Guid id,
        string state,
        int? lastStatus,
        string? lastError,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.connector_calls
            SET state = @state, last_status = @status, last_error = @error, input = NULL, settled_at = now()
            WHERE id = @id
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Integer) { Value = (object?)lastStatus ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Text) { Value = (object?)lastError ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetStateAsync(
        NpgsqlTransaction transaction,
        Guid id,
        string state,
        int? lastStatus,
        string? lastError,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE axis.connector_calls SET state = @state, last_status = @status, last_error = @error WHERE id = @id",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.Add(new NpgsqlParameter("status", NpgsqlDbType.Integer) { Value = (object?)lastStatus ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Text) { Value = (object?)lastError ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlConnection Connection(NpgsqlTransaction transaction) =>
        transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
}

/// <summary>A connector call that a <c>callConnector</c> step stages for its instance.</summary>
/// <param name="Id">The call's operation identity, a version 7 UUID.</param>
/// <param name="Connector">The connector's declared name.</param>
/// <param name="Operation">The operation's declared name.</param>
/// <param name="Input">The evaluated input, keyed by the inputs' declared names.</param>
internal sealed record NewConnectorCall(
    Guid Id,
    Guid ProcessInstanceId,
    string Step,
    Guid ReleaseId,
    string Connector,
    string Operation,
    JsonObject Input);

/// <summary>A connector call as the worker that sends it loads it.</summary>
/// <param name="Id">The call's operation identity.</param>
/// <param name="Connector">The connector's declared name.</param>
/// <param name="Operation">The operation's declared name.</param>
/// <param name="Attempts">The attempts made so far.</param>
/// <param name="Input">The stored input as JSON text, or <see langword="null"/> once the call settled.</param>
internal sealed record LoadedConnectorCall(
    Guid Id,
    Guid ProcessInstanceId,
    string Step,
    Guid ReleaseId,
    string Connector,
    string Operation,
    string State,
    int Attempts,
    string? Input);
