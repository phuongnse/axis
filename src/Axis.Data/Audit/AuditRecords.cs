using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Audit;

/// <summary>
/// Appends audit records to <c>axis.audit_records</c> of the tenant database and reads one
/// record's audit records back as its history. Other modules append and read audit records only
/// through this class, never through the table.
/// </summary>
public static class AuditRecords
{
    private const string InsertSql =
        """
        INSERT INTO "axis"."audit_records"
            ("id", "occurred_at", "actor", "action", "application_id", "entity_id", "record_id", "process_instance_id", "details")
        VALUES (@id, now(), @actor, @action, @application, @entity, @record, @process, @details)
        """;

    private const string RecordFilter = """WHERE "entity_id" = @entity AND "record_id" = @record""";

    /// <summary>Counts the audit records of one record.</summary>
    internal const string HistoryCountSql = $"""SELECT count(*) FROM "axis"."audit_records" {RecordFilter}""";

    /// <summary>Reads one page of the audit records of one record, newest first.</summary>
    internal const string HistoryPageSql =
        $"""
        SELECT "id", "occurred_at", "actor", "actor_name", "action", "process_instance_id", "details"::text
        FROM "axis"."audit_records" {RecordFilter}
        ORDER BY "occurred_at" DESC, "id" DESC
        LIMIT @limit OFFSET @offset
        """;

    /// <summary>
    /// Appends <paramref name="entry"/> in <paramref name="transaction"/>, so the audit record
    /// commits or rolls back with the caller's transaction and never on its own. Its time is the
    /// transaction time, and its id is a new version 7 UUID.
    /// </summary>
    /// <exception cref="ArgumentException">The transaction is completed and has no connection.</exception>
    public static async Task AppendAsync(NpgsqlTransaction transaction, AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(entry);
        var connection = transaction.Connection
            ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));

        await using var command = new NpgsqlCommand(InsertSql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("actor", entry.Actor);
        command.Parameters.AddWithValue("action", entry.Action);
        command.Parameters.Add(Uuid("application", entry.ApplicationId));
        command.Parameters.Add(Uuid("entity", entry.EntityId));
        command.Parameters.Add(Uuid("record", entry.RecordId));
        command.Parameters.Add(Uuid("process", entry.ProcessInstanceId));
        command.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Jsonb) { Value = entry.Details.ToJsonString() });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Reads page <paramref name="page"/> of <paramref name="pageSize"/> audit records that name
    /// the record <paramref name="recordId"/> of the entity <paramref name="entityId"/>, newest
    /// first and then by id descending, and counts them. A page past the last one has no items.
    /// </summary>
    public static async Task<AuditRecordPage> ListForRecordAsync(
        NpgsqlConnection connection,
        Guid entityId,
        Guid recordId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        long totalCount;
        await using (var count = new NpgsqlCommand(HistoryCountSql, connection))
        {
            count.Parameters.AddWithValue("entity", entityId);
            count.Parameters.AddWithValue("record", recordId);
            totalCount = (long)(await count.ExecuteScalarAsync(cancellationToken))!;
        }

        await using var command = new NpgsqlCommand(HistoryPageSql, connection);
        command.Parameters.AddWithValue("entity", entityId);
        command.Parameters.AddWithValue("record", recordId);
        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", (long)(page - 1) * pageSize);

        var items = new List<AuditRecordItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new AuditRecordItem(
                reader.GetGuid(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5),
                reader.GetString(6)));
        }

        return new AuditRecordPage(items, totalCount);
    }

    /// <summary>
    /// Returns whether <paramref name="actor"/> started the instance <paramref name="instanceId"/>
    /// on the record <paramref name="recordId"/> of the entity <paramref name="entityId"/> through
    /// the endpoint <paramref name="endpoint"/>, as its <c>process.started</c> audit record says.
    /// </summary>
    public static async Task<bool> IsProcessStartedByAsync(
        NpgsqlConnection connection,
        Guid entityId,
        Guid recordId,
        Guid instanceId,
        string actor,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = new NpgsqlCommand(
            $"""
            SELECT EXISTS (
                SELECT 1 FROM "axis"."audit_records" {RecordFilter}
                  AND "action" = @action AND "process_instance_id" = @instance
                  AND "actor" = @actor AND "details"->>'endpoint' = @endpoint)
            """,
            connection);
        command.Parameters.AddWithValue("entity", entityId);
        command.Parameters.AddWithValue("record", recordId);
        command.Parameters.AddWithValue("action", AuditActions.ProcessStarted);
        command.Parameters.AddWithValue("instance", instanceId);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("endpoint", endpoint);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static NpgsqlParameter Uuid(string name, Guid? value) =>
        new(name, NpgsqlDbType.Uuid) { Value = value is { } id ? id : DBNull.Value };
}
