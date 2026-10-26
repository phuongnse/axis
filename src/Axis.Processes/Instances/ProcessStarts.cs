using Axis.Processes.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the rows of a process start over a connection to a tenant database: the receipt of an
/// <c>Idempotency-Key</c> and the instance. The caller runs them in one transaction with the
/// instance's first work item. It also reads an instance back, for the endpoint status location.
/// </summary>
public static class ProcessStarts
{
    /// <summary>The kind of the work item that runs the next step of an instance.</summary>
    public const string StepWorkItemKind = "process.step";

    /// <summary>The kind of the work item that sends or reconciles one staged connector call.</summary>
    public const string ConnectorCallWorkItemKind = "connector.call";

    /// <summary>The kind of the work item that runs one staged package operation call.</summary>
    public const string PackageCallWorkItemKind = "package.call";

    /// <summary>The kind of the work item that delivers one event occurrence to the <c>async</c> event triggers of the active release.</summary>
    public const string EventDeliverWorkItemKind = "event.deliver";

    /// <summary>The kind of the work item that waits for the next occurrence of one schedule trigger.</summary>
    public const string TriggerScheduleWorkItemKind = "trigger.schedule";

    /// <summary>The kind of the work item that starts the process of a schedule trigger for each record of its data source, for one occurrence.</summary>
    public const string TriggerFanOutWorkItemKind = "trigger.fanOut";

    /// <summary>The kind of the work item that expires one open task at its step's deadline.</summary>
    public const string TaskDeadlineWorkItemKind = "task.deadline";

    /// <summary>The kind of the work item that resumes a parent instance when its sub-process child completes.</summary>
    public const string ProcessResumeWorkItemKind = "process.resume";

    /// <summary>The state of an instance that has work ready or claimed.</summary>
    public const string Running = "running";

    /// <summary>The state of an instance that waits for a human task to be completed, a connector call to settle or a sub-process child to complete.</summary>
    public const string Waiting = "waiting";

    /// <summary>The state of an instance that reached an <c>end</c> step.</summary>
    public const string Completed = "completed";

    /// <summary>The state of an instance whose step failed. The error is in its history.</summary>
    public const string Failed = "failed";

    /// <summary>The state of an instance that an operator cancelled. It does not block a new start.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The partial unique index that allows one running or waiting instance per process and record.</summary>
    internal const string ActiveSubjectIndex = "ux_process_instances_active_subject";

    internal const string UniqueViolation = "23505";

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
        Guid? subjectId,
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
        command.Parameters.Add(new NpgsqlParameter("subject", NpgsqlDbType.Uuid) { Value = (object?)subjectId ?? DBNull.Value });
        command.Parameters.AddWithValue("instance", instanceId);
        command.Parameters.Add(new NpgsqlParameter("body", NpgsqlDbType.Jsonb) { Value = body });
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Returns the stored receipt of <paramref name="key"/>, or <see langword="null"/> when there is none.
    /// For a process without a subject, the receipt carries the parameters stored on its instance.
    /// </summary>
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
            SELECT r.subject_id, r.status_code, r.body::text, i.parameters::text
            FROM axis.process_start_receipts r
            LEFT JOIN axis.process_instances i ON i.id = r.instance_id
            WHERE r.application_id = @application AND r.process_id = @process AND r.key = @key
            """,
            connection);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("process", processId);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ProcessStartReceipt(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3))
            : null;
    }

    /// <summary>
    /// Inserts <paramref name="instance"/> as <c>running</c> at revision 1. Returns
    /// <see langword="false"/> when the process already has a running or waiting instance for the
    /// subject record, or, for a process without a subject, already has one. The failed insert aborts the transaction, so the caller must roll back,
    /// either the transaction or to a savepoint taken before the insert.
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
                (id, application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step, parameters,
                 parent_instance_id, parent_step)
            VALUES (@id, @application, @process, @entity, @subject, @release, @state, 1, @step, @parameters,
                    @parentInstance, @parentStep)
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", instance.Id);
        command.Parameters.AddWithValue("application", instance.ApplicationId);
        command.Parameters.AddWithValue("process", instance.ProcessId);
        command.Parameters.Add(new NpgsqlParameter("entity", NpgsqlDbType.Uuid) { Value = (object?)instance.SubjectEntityId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("subject", NpgsqlDbType.Uuid) { Value = (object?)instance.SubjectId ?? DBNull.Value });
        command.Parameters.AddWithValue("release", instance.ReleaseId);
        command.Parameters.AddWithValue("state", Running);
        command.Parameters.AddWithValue("step", instance.Step);
        command.Parameters.Add(new NpgsqlParameter("parameters", NpgsqlDbType.Jsonb) { Value = (object?)instance.Parameters ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("parentInstance", NpgsqlDbType.Uuid) { Value = (object?)instance.ParentInstanceId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("parentStep", NpgsqlDbType.Text) { Value = (object?)instance.ParentStep ?? DBNull.Value });
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

    /// <summary>
    /// Returns whether a running or waiting instance has the record <paramref name="subjectId"/> of the entity
    /// <paramref name="subjectEntityId"/> as its subject.
    /// </summary>
    public static async Task<bool> HasOpenInstanceAsync(
        NpgsqlTransaction transaction,
        Guid applicationId,
        Guid subjectEntityId,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM axis.process_instances
                WHERE application_id = @application AND subject_entity_id = @entity AND subject_id = @subject
                  AND state IN (@running, @waiting))
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("entity", subjectEntityId);
        command.Parameters.AddWithValue("subject", subjectId);
        command.Parameters.AddWithValue("running", Running);
        command.Parameters.AddWithValue("waiting", Waiting);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Returns the instance <paramref name="id"/>, or <see langword="null"/> when there is none.</summary>
    public static async Task<ProcessInstanceState?> FindInstanceAsync(
        NpgsqlConnection connection,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = new NpgsqlCommand(
            """
            SELECT application_id, process_id, subject_entity_id, subject_id, state
            FROM axis.process_instances WHERE id = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ProcessInstanceState(id, reader.GetGuid(0), reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2), reader.IsDBNull(3) ? null : reader.GetGuid(3), reader.GetString(4))
            : null;
    }

    private static NpgsqlConnection Connection(NpgsqlTransaction transaction) =>
        transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
}

/// <summary>
/// A stored start response: the subject record it started for, its status code and its body as JSON
/// text, and the parameters stored on its instance as JSON text. The subject is <see langword="null"/>
/// for a process without a subject, and the parameters are <see langword="null"/> for one with a subject.
/// </summary>
public sealed record ProcessStartReceipt(Guid? SubjectId, int StatusCode, string Body, string? Parameters);

/// <summary>
/// An instance's application, process, subject record and state. The subject fields are
/// <see langword="null"/> for an instance of a process without a subject.
/// </summary>
public sealed record ProcessInstanceState(Guid Id, Guid ApplicationId, Guid ProcessId, Guid? SubjectEntityId, Guid? SubjectId, string State);
