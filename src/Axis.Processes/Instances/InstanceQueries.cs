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
                AddParameters(count, applicationId, processId, subjectEntity.Id, state, subjectId, condition);
                totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
            }

            await using var command = new NpgsqlCommand(
                $"SELECT {Columns} FROM axis.process_instances AS {InstanceAlias} {where} ORDER BY i.created_at DESC, i.id DESC LIMIT @limit OFFSET @offset",
                connection);
            AddParameters(command, applicationId, processId, subjectEntity.Id, state, subjectId, condition);
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

    private static string Where(EntityModel subjectEntity, RecordCondition? condition)
    {
        var row = EntityNaming.Quote("r");
        var visible = $"{row}.{EntityNaming.Quote(EntityNaming.IdColumn)} = {InstanceAlias}.subject_id"
            + (condition is null ? "" : $" AND {condition.Sql}");
        return $"""
            WHERE i.application_id = @application AND i.process_id = @process AND i.subject_entity_id = @entity
              AND (@state::text IS NULL OR i.state = @state) AND (@subject::uuid IS NULL OR i.subject_id = @subject)
              AND EXISTS (SELECT 1 FROM {RecordQueries.Table(subjectEntity)} AS {row} WHERE {visible})
            """;
    }

    private static void AddParameters(
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
