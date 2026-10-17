using Axis.Configuration.Model;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Processes.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Reads the instances of a process over a connection to a tenant database, for the people who
/// inspect the process. An instance is listed only while its subject record exists and matches the
/// reader's read filter, so a list never shows an instance whose record the reader may not read.
/// </summary>
public static class InstanceQueries
{
    // The instance is aliased i, because a record condition refers to its record as r.
    private const string InstanceAlias = "i";

    private const string Columns =
        "i.id, i.application_id, i.process_id, i.subject_entity_id, i.subject_id, i.release_id, i.state, i.revision, i.step, i.created_at, i.ended_at";

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> instances of the process,
    /// whatever release they are pinned to, newest first, and counts them. <paramref name="state"/>
    /// and <paramref name="subjectId"/> narrow the list when set. An instance is kept only when its
    /// subject record exists in <paramref name="subjectEntity"/> and matches
    /// <paramref name="condition"/>, or has no condition to match. A page past the last one has no
    /// items. Returns <see langword="null"/> when the database rejects the condition for these
    /// records with a data exception (SQLSTATE class 22). Any other database error is thrown.
    /// </summary>
    public static async Task<InstanceListPage?> ListAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        Guid processId,
        EntityModel subjectEntity,
        string? state,
        Guid? subjectId,
        RecordCondition? condition,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(subjectEntity);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var where = Where(subjectEntity, condition);
        try
        {
            long totalCount;
            await using (var count = new NpgsqlCommand($"SELECT count(*) FROM axis.process_instances AS {InstanceAlias} {where}", connection))
            {
                AddListParameters(count, applicationId, processId, subjectEntity.Id, state, subjectId, condition);
                totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
            }

            await using var command = new NpgsqlCommand(ListPageSql(subjectEntity, condition), connection);
            AddListParameters(command, applicationId, processId, subjectEntity.Id, state, subjectId, condition);
            command.Parameters.AddWithValue("limit", pageSize);
            command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);

            var items = new List<ProcessInstanceRow>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(ReadInstance(reader));
            }

            return new InstanceListPage(items, totalCount);
        }
        catch (PostgresException exception) when (condition is not null && exception.SqlState.StartsWith("22", StringComparison.Ordinal))
        {
            // Class 22 is a data exception: the condition could not be evaluated for some record.
            return null;
        }
    }

    /// <summary>
    /// Reads the instance <paramref name="id"/> of the process, or returns <see langword="null"/> when
    /// there is none, it belongs to another process or application, or its subject record in
    /// <paramref name="subjectEntity"/> no longer exists. It applies no read filter: the caller
    /// checks the subject record against the reader's filters.
    /// </summary>
    public static async Task<ProcessInstanceRow?> FindAsync(
        NpgsqlConnection connection,
        Guid applicationId,
        Guid processId,
        EntityModel subjectEntity,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(subjectEntity);

        var row = EntityNaming.Quote(RecordQueries.RowAlias);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT {Columns} FROM axis.process_instances AS {InstanceAlias}
            WHERE i.id = @id AND i.application_id = @application AND i.process_id = @process AND i.subject_entity_id = @entity
              AND EXISTS (SELECT 1 FROM {RecordQueries.Table(subjectEntity)} AS {row} WHERE {row}.{EntityNaming.Quote(EntityNaming.IdColumn)} = {InstanceAlias}.subject_id)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("process", processId);
        command.Parameters.AddWithValue("entity", subjectEntity.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadInstance(reader) : null;
    }

    /// <summary>Reads the step history rows of the instance <paramref name="instanceId"/>, oldest first.</summary>
    public static async Task<IReadOnlyList<ProcessStepHistoryRow>> HistoryAsync(
        NpgsqlConnection connection,
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand(
            """
            SELECT id, process_instance_id, step, revision, input::text, output::text, decision, error, started_at, finished_at, trace_id
            FROM axis.process_step_history WHERE process_instance_id = @id ORDER BY finished_at, id
            """,
            connection);
        command.Parameters.AddWithValue("id", instanceId);
        var rows = new List<ProcessStepHistoryRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ProcessStepHistoryRow
            {
                Id = reader.GetGuid(0),
                ProcessInstanceId = reader.GetGuid(1),
                Step = reader.GetString(2),
                Revision = reader.GetInt64(3),
                Input = reader.GetString(4),
                Output = reader.IsDBNull(5) ? null : reader.GetString(5),
                Decision = reader.IsDBNull(6) ? null : reader.GetString(6),
                Error = reader.IsDBNull(7) ? null : reader.GetString(7),
                StartedAt = reader.GetFieldValue<DateTimeOffset>(8),
                FinishedAt = reader.GetFieldValue<DateTimeOffset>(9),
                TraceId = reader.IsDBNull(10) ? null : reader.GetString(10),
            });
        }

        return rows;
    }

    private static string Where(EntityModel subjectEntity, RecordCondition? condition)
    {
        var row = EntityNaming.Quote(RecordQueries.RowAlias);
        var visible = $"{row}.{EntityNaming.Quote(EntityNaming.IdColumn)} = {InstanceAlias}.subject_id"
            + (condition is null ? "" : $" AND {condition.Sql}");
        return $"""
            WHERE i.application_id = @application AND i.process_id = @process AND i.subject_entity_id = @entity
              AND (@state::text IS NULL OR i.state = @state) AND (@subject::uuid IS NULL OR i.subject_id = @subject)
              AND EXISTS (SELECT 1 FROM {RecordQueries.Table(subjectEntity)} AS {row} WHERE {visible})
            """;
    }

    /// <summary>
    /// The query that reads one page of the list. <c>@limit</c> and <c>@offset</c> are bound by the caller,
    /// next to the parameters of <see cref="AddListParameters"/>.
    /// </summary>
    internal static string ListPageSql(EntityModel subjectEntity, RecordCondition? condition) =>
        $"SELECT {Columns} FROM axis.process_instances AS {InstanceAlias} {Where(subjectEntity, condition)} ORDER BY i.created_at DESC, i.id DESC LIMIT @limit OFFSET @offset";

    internal static void AddListParameters(
        NpgsqlCommand command,
        Guid applicationId,
        Guid processId,
        Guid entityId,
        string? state,
        Guid? subjectId,
        RecordCondition? condition)
    {
        command.Parameters.AddWithValue("application", applicationId);
        command.Parameters.AddWithValue("process", processId);
        command.Parameters.AddWithValue("entity", entityId);
        command.Parameters.Add(new NpgsqlParameter("state", NpgsqlDbType.Text) { Value = (object?)state ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("subject", NpgsqlDbType.Uuid) { Value = (object?)subjectId ?? DBNull.Value });
        condition?.AddTo(command);
    }

    private static ProcessInstanceRow ReadInstance(NpgsqlDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            ApplicationId = reader.GetGuid(1),
            ProcessId = reader.GetGuid(2),
            SubjectEntityId = reader.GetGuid(3),
            SubjectId = reader.GetGuid(4),
            ReleaseId = reader.GetGuid(5),
            State = reader.GetString(6),
            Revision = reader.GetInt64(7),
            Step = reader.GetString(8),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(9),
            EndedAt = reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
        };
}

/// <summary>One page of the instances of a process, and the number of them on every page.</summary>
public sealed record InstanceListPage(IReadOnlyList<ProcessInstanceRow> Items, long TotalCount);
