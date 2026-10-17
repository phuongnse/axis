using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Reads and writes the package calls that package operation steps stage over a connection to a
/// tenant database, and names their states. A call's id is its operation identity.
/// </summary>
internal static class PackageCalls
{
    /// <summary>The state of a call that waits for its next attempt.</summary>
    public const string Pending = "pending";

    /// <summary>The state of a call whose attempt a worker is running.</summary>
    public const string Sending = "sending";

    /// <summary>
    /// The state of a call whose last attempt has an unknown outcome and that is not run again,
    /// so the operation may have acted. It waits for an operator.
    /// </summary>
    public const string Unknown = "unknown";

    /// <summary>The state of a call that settled with an output that fits the manifest.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>The state of a call that settled with an error.</summary>
    public const string Failed = "failed";

    /// <summary>The state of a call that was still <c>pending</c> when an operator cancelled its instance. It is never run.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// Inserts <paramref name="call"/> as <c>pending</c> with no attempts, and with the retry policy
    /// and the <c>idempotent</c> flag it carries. It is created at the transaction's time. A second call for the same instance and step violates the unique index.
    /// </summary>
    public static async Task InsertAsync(NpgsqlTransaction transaction, NewPackageCall call, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.package_calls
                (id, process_instance_id, step, release_id, package, version, operation, state, attempts,
                 retry_attempts, retry_backoff, idempotent, input, created_at)
            VALUES (@id, @instance, @step, @release, @package, @version, @operation, @state, 0,
                 @retryAttempts, @retryBackoff, @idempotent, @input, now())
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", call.Id);
        command.Parameters.AddWithValue("instance", call.ProcessInstanceId);
        command.Parameters.AddWithValue("step", call.Step);
        command.Parameters.AddWithValue("release", call.ReleaseId);
        command.Parameters.AddWithValue("package", call.Package);
        command.Parameters.AddWithValue("version", call.Version);
        command.Parameters.AddWithValue("operation", call.Operation);
        command.Parameters.AddWithValue("state", Pending);
        command.Parameters.AddWithValue("retryAttempts", call.Retry.Attempts);
        command.Parameters.Add(new NpgsqlParameter("retryBackoff", NpgsqlDbType.Interval) { Value = call.Retry.Backoff });
        command.Parameters.AddWithValue("idempotent", call.Idempotent);
        command.Parameters.Add(new NpgsqlParameter("input", NpgsqlDbType.Jsonb) { Value = call.Input.ToJsonString() });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the call <paramref name="id"/> and locks it until the transaction ends, or returns
    /// <see langword="null"/> when there is none.
    /// </summary>
    public static async Task<LoadedPackageCall?> LoadAsync(NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT process_instance_id, step, release_id, package, version, operation, state, attempts,
                   retry_attempts, retry_backoff, idempotent, input::text
            FROM axis.package_calls WHERE id = @id FOR UPDATE
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LoadedPackageCall(
            id,
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7),
            new RetryPolicyModel(reader.GetInt32(8), reader.GetFieldValue<TimeSpan>(9)),
            reader.GetBoolean(10),
            await reader.IsDBNullAsync(11, cancellationToken) ? null : reader.GetString(11));
    }

    /// <summary>
    /// Marks the <c>pending</c> call <paramref name="id"/> <c>sending</c> and counts the attempt.
    /// Returns the attempts made, this one included, or <see langword="null"/> when the call is not pending.
    /// </summary>
    public static async Task<int?> MarkSendingAsync(NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.package_calls SET state = @sending, attempts = attempts + 1
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
    /// Counts an attempt that did not mark the call <c>sending</c>, because the operation never ran.
    /// The call stays <c>pending</c>. Returns the attempts made, this one included, or
    /// <see langword="null"/> when the call is not pending.
    /// </summary>
    public static async Task<int?> CountAttemptAsync(NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.package_calls SET attempts = attempts + 1
            WHERE id = @id AND state = @pending
            RETURNING attempts
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("pending", Pending);
        return await command.ExecuteScalarAsync(cancellationToken) is int attempts ? attempts : null;
    }

    /// <summary>
    /// Sets the call <paramref name="id"/> back to <c>pending</c> with the last attempt's error. Its
    /// stored input is kept, so the next attempt runs under the same operation identity.
    /// </summary>
    public static async Task RetryAsync(NpgsqlTransaction transaction, Guid id, string lastError, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE axis.package_calls SET state = @state, last_error = @error WHERE id = @id",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("state", Pending);
        command.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Text) { Value = lastError });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Sets the call <paramref name="id"/> to <c>unknown</c> with the last attempt's error. Its
    /// stored input is kept, since an operator may need it to resolve the call.
    /// </summary>
    public static async Task MarkUnknownAsync(NpgsqlTransaction transaction, Guid id, string lastError, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE axis.package_calls SET state = @state, last_error = @error WHERE id = @id",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("state", Unknown);
        command.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Text) { Value = lastError });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Settles the call <paramref name="id"/> as <paramref name="state"/>, <c>succeeded</c> or
    /// <c>failed</c>, at the transaction's time, with the last attempt's error. Its stored input is
    /// cleared.
    /// </summary>
    public static async Task SettleAsync(
        NpgsqlTransaction transaction,
        Guid id,
        string state,
        string? lastError,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.package_calls
            SET state = @state, last_error = @error, input = NULL, settled_at = now()
            WHERE id = @id
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Text) { Value = (object?)lastError ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlConnection Connection(NpgsqlTransaction transaction) =>
        transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
}

/// <summary>A package call that a package operation step stages for its instance.</summary>
/// <param name="Id">The call's operation identity, a version 7 UUID.</param>
/// <param name="Package">The package id.</param>
/// <param name="Version">The pinned package version.</param>
/// <param name="Operation">The operation's declared name.</param>
/// <param name="Retry">The step's retry policy.</param>
/// <param name="Idempotent">Whether the operation is idempotent, so an unknown outcome may be retried.</param>
/// <param name="Input">The evaluated input, keyed by the inputs' declared names.</param>
internal sealed record NewPackageCall(
    Guid Id,
    Guid ProcessInstanceId,
    string Step,
    Guid ReleaseId,
    string Package,
    string Version,
    string Operation,
    RetryPolicyModel Retry,
    bool Idempotent,
    JsonObject Input);

/// <summary>A package call as the worker that runs it loads it.</summary>
/// <param name="Id">The call's operation identity.</param>
/// <param name="Package">The package id.</param>
/// <param name="Version">The pinned package version.</param>
/// <param name="Operation">The operation's declared name.</param>
/// <param name="Attempts">The attempts made so far.</param>
/// <param name="Retry">The retry policy stored when the call was staged.</param>
/// <param name="Idempotent">Whether the operation is idempotent, stored when the call was staged.</param>
/// <param name="Input">The stored input as JSON text, or <see langword="null"/> once the call settled.</param>
internal sealed record LoadedPackageCall(
    Guid Id,
    Guid ProcessInstanceId,
    string Step,
    Guid ReleaseId,
    string Package,
    string Version,
    string Operation,
    string State,
    int Attempts,
    RetryPolicyModel Retry,
    bool Idempotent,
    string? Input);
