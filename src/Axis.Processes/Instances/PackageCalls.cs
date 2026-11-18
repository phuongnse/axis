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
    /// and the <c>idempotent</c> flag it carries. It is created at the transaction's time. A step stages one call per pass of a loop, told apart by the
    /// revision it ran at. A second call for the same instance, step and revision violates the unique index. A failed call is
    /// staged again by <see cref="RestageAsync"/>.
    /// </summary>
    public static async Task InsertAsync(NpgsqlTransaction transaction, NewPackageCall call, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.package_calls
                (id, process_instance_id, step, staged_revision, release_id, package, version, operation, state, attempts,
                 retry_attempts, retry_backoff, idempotent, input, created_at)
            VALUES (@id, @instance, @step, @revision, @release, @package, @version, @operation, @state, 0,
                 @retryAttempts, @retryBackoff, @idempotent, @input, now())
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", call.Id);
        command.Parameters.AddWithValue("instance", call.ProcessInstanceId);
        command.Parameters.AddWithValue("step", call.Step);
        command.Parameters.AddWithValue("revision", call.StagedRevision);
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
    /// Finds the latest call of <paramref name="step"/> for the instance, the one of the newest pass,
    /// and locks it until the transaction ends. Returns its id when it is <c>failed</c>, or
    /// <see langword="null"/> when it is not or the step has no call. An operator retry leaves such a call.
    /// </summary>
    public static async Task<Guid?> FindFailedAsync(NpgsqlTransaction transaction, Guid instanceId, string step, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT id, state FROM axis.package_calls
            WHERE process_instance_id = @instance AND step = @step
            ORDER BY staged_revision DESC LIMIT 1 FOR UPDATE
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("instance", instanceId);
        command.Parameters.AddWithValue("step", step);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) && reader.GetString(1) == Failed ? reader.GetGuid(0) : null;
    }

    /// <summary>
    /// Sets the <c>failed</c> call <paramref name="call"/> back to <c>pending</c> under its id, so its
    /// operation identity stays. It stores the new input, the revision of the new run, the step's current retry policy and
    /// <c>idempotent</c> flag, counts attempts from 0 again, and clears the last error and the
    /// settled time. Throws when the call is not failed.
    /// </summary>
    public static async Task RestageAsync(NpgsqlTransaction transaction, NewPackageCall call, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.package_calls
            SET state = @pending, attempts = 0, retry_attempts = @retryAttempts, retry_backoff = @retryBackoff,
                idempotent = @idempotent, input = @input, staged_revision = @revision, last_error = NULL, settled_at = NULL
            WHERE id = @id AND state = @failed
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", call.Id);
        command.Parameters.AddWithValue("pending", Pending);
        command.Parameters.AddWithValue("failed", Failed);
        command.Parameters.AddWithValue("revision", call.StagedRevision);
        command.Parameters.AddWithValue("retryAttempts", call.Retry.Attempts);
        command.Parameters.Add(new NpgsqlParameter("retryBackoff", NpgsqlDbType.Interval) { Value = call.Retry.Backoff });
        command.Parameters.AddWithValue("idempotent", call.Idempotent);
        command.Parameters.Add(new NpgsqlParameter("input", NpgsqlDbType.Jsonb) { Value = call.Input.ToJsonString() });
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException($"The package call {call.Id} is not failed, so it cannot be staged again.");
        }
    }

    /// <summary>
    /// Reads the call <paramref name="id"/> and locks it until the transaction ends, or returns
    /// <see langword="null"/> when there is none.
    /// </summary>
    public static async Task<LoadedPackageCall?> LoadAsync(NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT {CallColumns} FROM axis.package_calls WHERE id = @id FOR UPDATE",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? await ReadCallAsync(reader, cancellationToken) : null;
    }

    /// <summary>
    /// Finds the call that holds the lease <paramref name="leaseId"/> and locks it until the transaction ends, or returns
    /// <see langword="null"/> when no call does. The call's instance is locked first, as a cancel locks it before its calls, so a result and
    /// a cancel cannot deadlock. The answer says which runner holds the lease and whether it has ended, by the database clock.
    /// </summary>
    public static async Task<LeasedPackageCall?> LockLeasedAsync(NpgsqlTransaction transaction, Guid leaseId, CancellationToken cancellationToken)
    {
        await using (var lockInstance = new NpgsqlCommand(
            """
            SELECT 1 FROM axis.process_instances
            WHERE id = (SELECT process_instance_id FROM axis.package_calls WHERE lease_id = @lease) FOR UPDATE
            """,
            Connection(transaction),
            transaction))
        {
            lockInstance.Parameters.AddWithValue("lease", leaseId);
            await lockInstance.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(
            $"SELECT {CallColumns}, lease_until, lease_until < now() FROM axis.package_calls WHERE lease_id = @lease FOR UPDATE",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("lease", leaseId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var call = await ReadCallAsync(reader, cancellationToken);
        return new LeasedPackageCall(call, reader.GetFieldValue<DateTimeOffset>(14), reader.GetBoolean(15));
    }

    private const string CallColumns =
        """
        id, process_instance_id, step, release_id, package, version, operation, state, attempts,
        retry_attempts, retry_backoff, idempotent, input::text, runner_id
        """;

    /// <summary>Maps a row that selects <see cref="CallColumns"/> first.</summary>
    private static async Task<LoadedPackageCall> ReadCallAsync(NpgsqlDataReader reader, CancellationToken cancellationToken) =>
        new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetInt32(8),
            new RetryPolicyModel(reader.GetInt32(9), reader.GetFieldValue<TimeSpan>(10)),
            reader.GetBoolean(11),
            await reader.IsDBNullAsync(12, cancellationToken) ? null : reader.GetString(12),
            await reader.IsDBNullAsync(13, cancellationToken) ? null : reader.GetGuid(13));

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

    /// <summary>
    /// Settles the <c>unknown</c> call <paramref name="id"/> as <paramref name="state"/>, <c>succeeded</c>
    /// or <c>failed</c>, by an operator's hand, at the transaction's time. Its stored input is cleared. Its
    /// last error is kept, so inspection still shows what the last attempt ended with. Throws when the call
    /// is not unknown.
    /// </summary>
    public static async Task ResolveAsync(NpgsqlTransaction transaction, Guid id, string state, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE axis.package_calls SET state = @state, input = NULL, settled_at = now() WHERE id = @id AND state = @unknown",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("unknown", Unknown);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException($"The package call {id} is not unknown, so it cannot be resolved.");
        }
    }

    /// <summary>
    /// Finds the oldest <c>pending</c> call that the runner <paramref name="runnerId"/> may take and locks it until the transaction ends, or
    /// returns <see langword="null"/> when there is none. The call's package and version must be in both <paramref name="heldJson"/> and
    /// the runner's registered list, and it must have no <c>package.call</c> work item. A call that another transaction has locked, and
    /// a call in <paramref name="skipped"/>, are passed over.
    /// </summary>
    public static async Task<TakeableCall?> FindTakeableAsync(
        NpgsqlTransaction transaction,
        Guid runnerId,
        string heldJson,
        Guid[] skipped,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT c.id, c.step, c.release_id, c.package, c.version, c.operation, c.input::text, i.process_id, c.process_instance_id
            FROM axis.package_calls c
            JOIN axis.process_instances i ON i.id = c.process_instance_id
            WHERE c.state = @pending
              AND EXISTS (
                SELECT 1 FROM jsonb_to_recordset(@held) AS h(id text, version text)
                WHERE h.id = c.package AND h.version = c.version)
              AND EXISTS (
                SELECT 1 FROM axis.package_runners r, jsonb_to_recordset(r.packages) AS p(id text, version text)
                WHERE r.id = @runner AND r.revoked_at IS NULL AND p.id = c.package AND p.version = c.version)
              AND NOT EXISTS (
                SELECT 1 FROM axis.process_work_items w WHERE w.package_call_id = c.id AND w.kind = @kind)
              AND c.id <> ALL(@skipped)
            ORDER BY c.created_at, c.id
            LIMIT 1
            FOR UPDATE OF c SKIP LOCKED
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("pending", Pending);
        command.Parameters.AddWithValue("runner", runnerId);
        command.Parameters.Add(new NpgsqlParameter("held", NpgsqlDbType.Jsonb) { Value = heldJson });
        command.Parameters.AddWithValue("kind", ProcessStarts.PackageCallWorkItemKind);
        command.Parameters.Add(new NpgsqlParameter("skipped", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = skipped });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new TakeableCall(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            await reader.IsDBNullAsync(6, cancellationToken) ? null : reader.GetString(6),
            reader.GetGuid(7),
            reader.GetGuid(8));
    }

    /// <summary>
    /// Marks the <c>pending</c> call <paramref name="id"/> <c>sending</c>, counts the attempt and stores the runner, the lease and its end,
    /// which is the database clock's now plus <paramref name="timeout"/>. Returns the end of the lease, or <see langword="null"/> when the call
    /// is not pending.
    /// </summary>
    public static async Task<DateTimeOffset?> TakeAsync(
        NpgsqlTransaction transaction,
        Guid id,
        Guid runnerId,
        Guid leaseId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.package_calls
            SET state = @sending, attempts = attempts + 1, runner_id = @runner, lease_id = @lease, lease_until = now() + @timeout
            WHERE id = @id AND state = @pending
            RETURNING lease_until
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("runner", runnerId);
        command.Parameters.AddWithValue("lease", leaseId);
        command.Parameters.Add(new NpgsqlParameter("timeout", NpgsqlDbType.Interval) { Value = timeout });
        command.Parameters.AddWithValue("sending", Sending);
        command.Parameters.AddWithValue("pending", Pending);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? await reader.GetFieldValueAsync<DateTimeOffset>(0, cancellationToken) : null;
    }

    /// <summary>
    /// Deletes the <c>package.call</c> work items of the call <paramref name="callId"/>, which hold it back from a take or end a runner's lease.
    /// </summary>
    public static async Task DeleteItemsAsync(NpgsqlTransaction transaction, Guid callId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "DELETE FROM axis.process_work_items WHERE package_call_id = @call AND kind = @kind",
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("call", callId);
        command.Parameters.AddWithValue("kind", ProcessStarts.PackageCallWorkItemKind);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlConnection Connection(NpgsqlTransaction transaction) =>
        transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
}

/// <summary>A <c>pending</c> call that a customer-hosted runner may take, as <see cref="PackageCalls.FindTakeableAsync"/> reads it.</summary>
/// <param name="Id">The call's operation identity.</param>
/// <param name="Step">The declared name of the package operation step.</param>
/// <param name="ReleaseId">The release the instance is pinned to.</param>
/// <param name="Package">The package id.</param>
/// <param name="Version">The pinned package version.</param>
/// <param name="Operation">The operation's declared name.</param>
/// <param name="Input">The stored input as JSON text.</param>
/// <param name="ProcessId">The process of the call's instance.</param>
/// <param name="ProcessInstanceId">The call's instance.</param>
internal sealed record TakeableCall(
    Guid Id,
    string Step,
    Guid ReleaseId,
    string Package,
    string Version,
    string Operation,
    string? Input,
    Guid ProcessId,
    Guid ProcessInstanceId);

/// <summary>A package call that a package operation step stages for its instance.</summary>
/// <param name="Id">The call's operation identity, a version 7 UUID.</param>
/// <param name="StagedRevision">The instance revision the step ran at.</param>
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
    long StagedRevision,
    Guid ReleaseId,
    string Package,
    string Version,
    string Operation,
    RetryPolicyModel Retry,
    bool Idempotent,
    JsonObject Input);

/// <summary>A call found by its lease, as <see cref="PackageCalls.LockLeasedAsync"/> reads it.</summary>
/// <param name="Call">The call, whose <see cref="LoadedPackageCall.RunnerId"/> is the runner that holds the lease.</param>
/// <param name="LeaseUntil">The end of the lease.</param>
/// <param name="Ended">Whether the end of the lease has passed by the database clock.</param>
internal sealed record LeasedPackageCall(LoadedPackageCall Call, DateTimeOffset LeaseUntil, bool Ended);

/// <summary>A package call as the worker that runs it loads it.</summary>
/// <param name="Id">The call's operation identity.</param>
/// <param name="Package">The package id.</param>
/// <param name="Version">The pinned package version.</param>
/// <param name="Operation">The operation's declared name.</param>
/// <param name="Attempts">The attempts made so far.</param>
/// <param name="Retry">The retry policy stored when the call was staged.</param>
/// <param name="Idempotent">Whether the operation is idempotent, stored when the call was staged.</param>
/// <param name="Input">The stored input as JSON text, or <see langword="null"/> once the call settled.</param>
/// <param name="RunnerId">The customer-hosted runner that last took the call, or <see langword="null"/> when none did. Only a take sets it.</param>
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
    string? Input,
    Guid? RunnerId);
