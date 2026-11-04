using Npgsql;

namespace Axis.ControlPlane.Audit;

/// <summary>An audit record as stored. <see cref="ActorKind"/> is the lowercase column value.</summary>
internal sealed record AuditRecord(Guid Id, DateTimeOffset OccurredAt, string ActorKind, string Actor, string Action, string Subject);

/// <summary>
/// Appends to and reads <c>control_plane.audit_records</c>. There is no update or delete method, and the database rejects
/// both. A record is appended in the transaction of the change it describes, so both commit or neither does.
/// </summary>
internal static class AuditRecords
{
    /// <summary>
    /// Appends a record for <paramref name="action"/> on <paramref name="subject"/> by <paramref name="actor"/>: the account, the
    /// provider, the job id or the operator. It never holds a secret, a payment detail or a webhook body.
    /// </summary>
    public static async Task AppendAsync(
        NpgsqlTransaction transaction,
        AuditActorKind kind,
        string actor,
        string action,
        string subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO control_plane.audit_records (id, actor_kind, actor, action, subject)
            VALUES (@id, @kind, @actor, @action, @subject)
            """,
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("kind", kind.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("subject", subject);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Reads the records of <paramref name="subject"/>, oldest first.</summary>
    public static async Task<IReadOnlyList<AuditRecord>> ReadAsync(NpgsqlConnection connection, string subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, occurred_at, actor_kind, actor, action, subject FROM control_plane.audit_records
            WHERE subject = @subject ORDER BY occurred_at, id
            """,
            connection);
        command.Parameters.AddWithValue("subject", subject);
        var records = new List<AuditRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new AuditRecord(
                reader.GetGuid(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return records;
    }
}
