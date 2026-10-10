using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Reads and writes the rows of a process step over a connection to a tenant database: the
/// instance and its step history. Every change of an instance is guarded by the revision the step
/// loaded, so a step that lost a race to a newer revision changes nothing.
/// </summary>
internal static class ProcessSteps
{
    /// <summary>
    /// Reads the instance <paramref name="id"/>, with the transaction's time as the step's start, or
    /// <see langword="null"/> when there is none.
    /// </summary>
    public static async Task<LoadedInstance?> LoadAsync(NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT application_id, process_id, subject_entity_id, subject_id, release_id, state, revision, step, now()
            FROM axis.process_instances WHERE id = @id
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new LoadedInstance(
            id,
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            reader.GetGuid(4),
            reader.GetString(5),
            reader.GetInt64(6),
            reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8));
    }

    /// <summary>
    /// Moves a <c>running</c> instance at <paramref name="revision"/> to <paramref name="state"/> and
    /// <paramref name="step"/>, with the next revision. A <c>completed</c> instance gets its end time.
    /// Returns <see langword="false"/> when the instance changed since it was loaded. A concurrent
    /// change waits for the other transaction, then finds the newer revision.
    /// </summary>
    public static async Task<bool> TryAdvanceAsync(
        NpgsqlTransaction transaction,
        Guid id,
        long revision,
        string state,
        string step,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.process_instances
            SET state = @state, step = @step, revision = revision + 1,
                ended_at = CASE WHEN @state = @completed THEN now() END
            WHERE id = @id AND revision = @revision AND state = @running
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("step", step);
        command.Parameters.AddWithValue("completed", ProcessStarts.Completed);
        command.Parameters.AddWithValue("running", ProcessStarts.Running);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Marks a <c>running</c> instance at <paramref name="revision"/> <c>failed</c>, with the next
    /// revision and its end time. Returns <see langword="false"/> when the instance changed since.
    /// </summary>
    public static async Task<bool> TryFailAsync(NpgsqlTransaction transaction, Guid id, long revision, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.process_instances
            SET state = @failed, revision = revision + 1, ended_at = now()
            WHERE id = @id AND revision = @revision AND state = @running
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("revision", revision);
        command.Parameters.AddWithValue("failed", ProcessStarts.Failed);
        command.Parameters.AddWithValue("running", ProcessStarts.Running);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Moves an instance that is <c>waiting</c> on the task step <paramref name="taskStep"/> back to
    /// <c>running</c> on <paramref name="next"/>, with the next revision, and returns the revision
    /// it waited at. Returns <see langword="null"/> when the instance is not waiting on that step.
    /// </summary>
    public static async Task<long?> TryResumeAsync(
        NpgsqlTransaction transaction,
        Guid id,
        string taskStep,
        string next,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.process_instances
            SET state = @running, step = @next, revision = revision + 1
            WHERE id = @id AND state = @waiting AND step = @step
            RETURNING revision - 1
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("step", taskStep);
        command.Parameters.AddWithValue("next", next);
        command.Parameters.AddWithValue("running", ProcessStarts.Running);
        command.Parameters.AddWithValue("waiting", ProcessStarts.Waiting);
        return await command.ExecuteScalarAsync(cancellationToken) is long revision ? revision : null;
    }

    /// <summary>Adds the history row of a step occurrence. It finishes at the database's current time.</summary>
    public static async Task InsertHistoryAsync(NpgsqlTransaction transaction, StepOccurrence occurrence, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.process_step_history
                (id, process_instance_id, step, revision, input, output, decision, error, started_at, finished_at)
            VALUES (@id, @instance, @step, @revision, @input, @output, @decision, @error, @started, clock_timestamp())
            """,
            Connection(transaction),
            transaction);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("instance", occurrence.InstanceId);
        command.Parameters.AddWithValue("step", occurrence.Step);
        command.Parameters.AddWithValue("revision", occurrence.Revision);
        command.Parameters.Add(new NpgsqlParameter("input", NpgsqlDbType.Jsonb) { Value = occurrence.Input.ToJsonString() });
        command.Parameters.Add(new NpgsqlParameter("output", NpgsqlDbType.Jsonb) { Value = (object?)occurrence.Output?.ToJsonString() ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("decision", NpgsqlDbType.Text) { Value = (object?)occurrence.Decision ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Text) { Value = (object?)occurrence.Error ?? DBNull.Value });
        command.Parameters.AddWithValue("started", occurrence.StartedAt.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlConnection Connection(NpgsqlTransaction transaction) =>
        transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
}

/// <summary>An instance as a step loads it. <see cref="Now"/> is the time of the step's transaction.</summary>
internal sealed record LoadedInstance(
    Guid Id,
    Guid ApplicationId,
    Guid ProcessId,
    Guid SubjectEntityId,
    Guid SubjectId,
    Guid ReleaseId,
    string State,
    long Revision,
    string Step,
    DateTimeOffset Now);

/// <summary>One occurrence of a step, as its history row records it.</summary>
/// <param name="Revision">The instance revision the step ran at.</param>
/// <param name="Decision">
/// The branch a decision took, its zero-based index or <c>otherwise</c>, or the outcome a task was completed with.
/// </param>
internal sealed record StepOccurrence(
    Guid InstanceId,
    string Step,
    long Revision,
    JsonObject Input,
    JsonObject? Output,
    string? Decision,
    string? Error,
    DateTimeOffset StartedAt);
