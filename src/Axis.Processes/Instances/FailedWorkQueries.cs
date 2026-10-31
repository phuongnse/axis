using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Reads the failed work of a tenant over a connection to its database, for the operator console: the
/// instances that are <c>failed</c>, and the connector and package calls whose outcome is <c>unknown</c>,
/// across every process of every application. An item never carries a call input, a history input or output,
/// or a record value.
/// </summary>
public static class FailedWorkQueries
{
    // A failed instance has no error column, so its error is the newest history error. A failed instance with no end time
    // falls back to its start. An unknown call has no time for when it became unknown, so its time is the finish of
    // its newest attempt, whose outcome was lost, and without an attempt row the time the call was staged.
    private const string WorkSql =
        """
        WITH work AS (
            SELECT 'instance' AS kind, i.application_id, i.process_id, i.release_id, i.id AS instance_id, NULL::uuid AS call_id,
                   i.step, NULL::text AS connector, NULL::text AS package, NULL::text AS operation,
                   COALESCE(i.ended_at, i.created_at) AS failed_at,
                   (SELECT h.error FROM axis.process_step_history AS h
                    WHERE h.process_instance_id = i.id AND h.error IS NOT NULL
                    ORDER BY h.finished_at DESC, h.id DESC LIMIT 1) AS error
            FROM axis.process_instances AS i
            WHERE i.state = @failed
            UNION ALL
            SELECT 'connector', i.application_id, i.process_id, c.release_id, i.id, c.id,
                   c.step, c.connector, NULL::text, c.operation,
                   COALESCE((SELECT max(a.finished_at) FROM axis.process_call_attempts AS a
                             WHERE a.call_kind = @connectorKind AND a.call_id = c.id AND a.kind = @attemptKind), c.created_at),
                   c.last_error
            FROM axis.connector_calls AS c
            JOIN axis.process_instances AS i ON i.id = c.process_instance_id
            WHERE c.state = @unknown
            UNION ALL
            SELECT 'package', i.application_id, i.process_id, c.release_id, i.id, c.id,
                   c.step, NULL::text, c.package, c.operation,
                   COALESCE((SELECT max(a.finished_at) FROM axis.process_call_attempts AS a
                             WHERE a.call_kind = @packageKind AND a.call_id = c.id AND a.kind = @attemptKind), c.created_at),
                   c.last_error
            FROM axis.package_calls AS c
            JOIN axis.process_instances AS i ON i.id = c.process_instance_id
            WHERE c.state = @unknown
        )
        """;

    private const string ApplicationFilter = "WHERE (@application::uuid IS NULL OR application_id = @application)";

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> items of failed work, newest first, and
    /// counts them. <paramref name="applicationId"/> narrows the list to one application when set. A page past the
    /// last one has no items.
    /// </summary>
    public static async Task<FailedWorkPage> ListAsync(
        NpgsqlConnection connection,
        Guid? applicationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        long totalCount;
        await using (var count = new NpgsqlCommand($"{WorkSql} SELECT count(*) FROM work {ApplicationFilter}", connection))
        {
            AddParameters(count, applicationId);
            totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var command = new NpgsqlCommand(
            $"""
            {WorkSql}
            SELECT kind, application_id, process_id, release_id, instance_id, call_id, step, connector, package, operation, failed_at, error
            FROM work {ApplicationFilter}
            ORDER BY failed_at DESC, COALESCE(call_id, instance_id) DESC
            LIMIT @limit OFFSET @offset
            """,
            connection);
        AddParameters(command, applicationId);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);

        var items = new List<FailedWorkItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new FailedWorkItem(
                reader.GetString(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetGuid(3),
                reader.GetGuid(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.GetFieldValue<DateTimeOffset>(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return new FailedWorkPage(items, totalCount);
    }

    private static void AddParameters(NpgsqlCommand command, Guid? applicationId)
    {
        command.Parameters.AddWithValue("failed", ProcessStarts.Failed);
        command.Parameters.AddWithValue("unknown", ConnectorCalls.Unknown);
        command.Parameters.AddWithValue("connectorKind", CallAttempts.Connector);
        command.Parameters.AddWithValue("packageKind", CallAttempts.Package);
        command.Parameters.AddWithValue("attemptKind", CallAttempts.Attempt);
        command.Parameters.Add(new NpgsqlParameter("application", NpgsqlDbType.Uuid) { Value = (object?)applicationId ?? DBNull.Value });
    }
}

/// <summary>One page of failed work and the number of items in all pages.</summary>
public sealed record FailedWorkPage(IReadOnlyList<FailedWorkItem> Items, long TotalCount);

/// <summary>
/// One failed instance or one call with an unknown outcome. <see cref="Kind"/> is <c>instance</c>, <c>connector</c> or
/// <c>package</c>. A call item has its <see cref="CallId"/>, its <see cref="Operation"/> and its <see cref="Connector"/> or
/// <see cref="Package"/>, and an instance item has none of them. <see cref="ReleaseId"/> is the release the instance is pinned to.
/// </summary>
public sealed record FailedWorkItem(
    string Kind,
    Guid ApplicationId,
    Guid ProcessId,
    Guid ReleaseId,
    Guid InstanceId,
    Guid? CallId,
    string Step,
    string? Connector,
    string? Package,
    string? Operation,
    DateTimeOffset FailedAt,
    string? Error);
