using Axis.Processes.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the rows of a process start over a connection to a tenant database: the receipt of an
/// <c>Idempotency-Key</c> and the instance. The caller runs them in one transaction with the
/// instance's first work item.
/// </summary>
public static class ProcessStarts
{
    /// <summary>The kind of the work item that runs the next step of an instance.</summary>
    public const string StepWorkItemKind = "process.step";

    /// <summary>The state of an instance that has work ready or claimed.</summary>
    public const string Running = "running";

    /// <summary>The state of an instance that waits for a human task to be completed.</summary>
    public const string Waiting = "waiting";

    /// <summary>The state of an instance that reached an <c>end</c> step.</summary>
    public const string Completed = "completed";

    /// <summary>The state of an instance whose step failed. The error is in its history.</summary>
    public const string Failed = "failed";

    /// <summary>The partial unique index that allows one running or waiting instance per process and record.</summary>
    internal const string ActiveSubjectIndex = "ux_process_instances_active_subject";

    private const string UniqueViolation = "23505";

    /// <summary>
    /// Stores a <c>201</c> receipt for <paramref name="key"/> unless one exists. Returns
    /// <see langword="false"/> when the key is taken. A concurrent claim of the same key waits for
    /// the first one's transaction, so only one of them inserts.
    /// </summary>
    public static async Task<bool> TryClaimReceiptAsync(
        NpgsqlTransaction transaction,
        Guid applicationId,
        Guid processId,
        string key,
        Guid subjectId,
        Guid instanceId,
        string body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.process_start_receipts (application_id, process_id, key, subject_id, instance_id, status_code, body)
            VALUES (@application, @process, @key, @subject, @instance, 201, @body)
            ON CONFLICT (application_id, process_id, key) DO NOTHING
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("process", processId);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("subject", subjectId);
        command.Parameters.AddWithValue("instance", instanceId);
        command.Parameters.Add(new NpgsqlParameter("body", NpgsqlDbType.Jsonb) { Value = body });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Returns the stored receipt of <paramref name="key"/>, or <see langword="null"/> when there is none.</summary>
    public static async Task<ProcessStartReceipt?> FindReceiptAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        Guid processId,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = new NpgsqlCommand(
            """
            SELECT subject_id, status_code, body::text FROM axis.process_start_receipts
            WHERE application_id = @application AND process_id = @process AND key = @key
            """,
            connection);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("process", processId);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ProcessStartReceipt(reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2))
            : null;
    }

    /// <summary>
    /// Inserts <paramref name="instance"/> as <c>running</c> at revision 1. Returns
    /// <see langword="false"/> when the process already has a running or waiting instance for the
    /// subject record. The failed insert aborts the transaction, so the caller must roll back.
    /// </summary>
    public static async Task<bool> TryInsertInstanceAsync(
        NpgsqlTransaction transaction,
        ProcessInstanceRow instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(instance);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.process_instances
                (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step)
            VALUES (@id, @application, @process, @entity, @subject, @release, @state, 1, @step)
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", instance.Id);
        command.Parameters.AddWithValue("application", instance.ApplicationId);
        command.Parameters.AddWithValue("process", instance.ProcessId);
        command.Parameters.AddWithValue("entity", instance.SubjectEntityId);
        command.Parameters.AddWithValue("subject", instance.SubjectId);
        command.Parameters.AddWithValue("release", instance.ReleaseId);
        command.Parameters.AddWithValue("state", Running);
        command.Parameters.AddWithValue("step", instance.Step);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception is { SqlState: UniqueViolation, ConstraintName: ActiveSubjectIndex })
        {
            return false;
        }
    }

    private static NpgsqlConnection Connection(NpgsqlTransaction transaction) =>
        transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
}

/// <summary>A stored start response: the subject record it started for, its status code and its body as JSON text.</summary>
public sealed record ProcessStartReceipt(Guid SubjectId, int StatusCode, string Body);
