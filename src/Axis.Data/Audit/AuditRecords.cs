using Npgsql;
using NpgsqlTypes;

namespace Axis.Data.Audit;

/// <summary>
/// Appends audit records to <c>axis.audit_records</c> of the tenant database. Other modules append
/// audit records only through this class, never through the table.
/// </summary>
public static class AuditRecords
{
    private const string InsertSql =
        """
        INSERT INTO "axis"."audit_records"
            ("id", "occurred_at", "actor", "action", "application_id", "entity_id", "record_id", "process_instance_id", "details")
        VALUES (@id, now(), @actor, @action, @application, @entity, @record, @process, @details)
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
        command.Parameters.AddWithValue("application", entry.ApplicationId);
        command.Parameters.Add(Uuid("entity", entry.EntityId));
        command.Parameters.Add(Uuid("record", entry.RecordId));
        command.Parameters.Add(Uuid("process", entry.ProcessInstanceId));
        command.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Jsonb) { Value = entry.Details.ToJsonString() });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlParameter Uuid(string name, Guid? value) =>
        new(name, NpgsqlDbType.Uuid) { Value = value is { } id ? id : DBNull.Value };
}
