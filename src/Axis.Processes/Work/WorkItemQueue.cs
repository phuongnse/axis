using System.Diagnostics.CodeAnalysis;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Work;

/// <summary>
/// Reads and writes <c>axis.process_work_items</c> over a connection to a tenant database. Lease
/// times use the database clock, so workers on different machines agree when a lease expires.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "It is the queue of ready work items.")]
public static class WorkItemQueue
{
    internal const string ClaimSql = """
        UPDATE axis.process_work_items w
        SET claim_token = gen_random_uuid(), lease_expires_at = now() + @lease
        FROM (SELECT id FROM axis.process_work_items
              WHERE tenant_id = @tenant AND kind = ANY(@kinds) AND due_at <= now()
                AND (lease_expires_at IS NULL OR lease_expires_at <= now())
              ORDER BY due_at, id LIMIT 1 FOR UPDATE SKIP LOCKED) c
        WHERE w.id = c.id
        RETURNING w.id, w.tenant_id, w.kind, w.claim_token, w.lease_expires_at, w.process_instance_id, w.connector_call_id, w.event_id
        """;

    /// <summary>
    /// Adds a ready work item and returns its id. <paramref name="processInstanceId"/> names the
    /// process instance the item runs a step or a connector call of, when it does,
    /// <paramref name="connectorCallId"/> the connector call it sends, and <paramref name="eventId"/>
    /// the event occurrence it delivers.
    /// </summary>
    public static async Task<Guid> EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string tenantId,
        string kind,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken,
        Guid? processInstanceId = null,
        Guid? connectorCallId = null,
        Guid? eventId = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, process_instance_id, connector_call_id, event_id)
            VALUES (@id, @tenant, @kind, @due, @instance, @call, @event)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("due", dueAt.ToUniversalTime());
        command.Parameters.Add(new NpgsqlParameter("instance", NpgsqlDbType.Uuid) { Value = (object?)processInstanceId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("call", NpgsqlDbType.Uuid) { Value = (object?)connectorCallId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("event", NpgsqlDbType.Uuid) { Value = (object?)eventId ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    /// <summary>
    /// Claims the earliest due item of the tenant and of one of the kinds that is not held by a live
    /// lease. The claim gets a new token and a lease of <paramref name="lease"/>. Concurrent callers
    /// never claim the same item. Returns <see langword="null"/> when no item is ready.
    /// </summary>
    public static async Task<ClaimedWorkItem?> ClaimAsync(
        NpgsqlConnection connection,
        string tenantId,
        IReadOnlyCollection<string> kinds,
        TimeSpan lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(kinds);
        await using var command = new NpgsqlCommand(ClaimSql, connection);
        command.Parameters.AddWithValue("lease", NpgsqlDbType.Interval, lease);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("kinds", NpgsqlDbType.Array | NpgsqlDbType.Text, kinds.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ClaimedWorkItem(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetGuid(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            await reader.IsDBNullAsync(5, cancellationToken) ? null : reader.GetGuid(5),
            await reader.IsDBNullAsync(6, cancellationToken) ? null : reader.GetGuid(6),
            await reader.IsDBNullAsync(7, cancellationToken) ? null : reader.GetGuid(7),
            lease);
    }

    /// <summary>
    /// Sets the item's lease to end <paramref name="length"/> after the database's current time, in
    /// the transaction, if the claim is still current. Returns <see langword="false"/> when another
    /// worker has claimed it since, so the caller must roll back.
    /// </summary>
    public static async Task<bool> ExtendLeaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ClaimedWorkItem item,
        TimeSpan length,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(item);
        await using var command = new NpgsqlCommand(
            "UPDATE axis.process_work_items SET lease_expires_at = now() + @length WHERE id = @id AND claim_token = @token",
            connection,
            transaction);
        command.Parameters.AddWithValue("length", NpgsqlDbType.Interval, length);
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("token", item.ClaimToken);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Keeps the item and makes it due <paramref name="delay"/> after the database's current time,
    /// in the transaction, if the claim is still current. The claim and its lease are released, so
    /// any worker can claim the item once it is due. Returns <see langword="false"/> when another
    /// worker has claimed it since, so the caller must roll back.
    /// </summary>
    public static async Task<bool> RescheduleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ClaimedWorkItem item,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(item);
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.process_work_items
            SET due_at = now() + @delay, claim_token = NULL, lease_expires_at = NULL
            WHERE id = @id AND claim_token = @token
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("delay", NpgsqlDbType.Interval, delay);
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("token", item.ClaimToken);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Deletes the item in the transaction if the claim is still current. Returns
    /// <see langword="false"/> when another worker has claimed it since, so the caller must roll back.
    /// </summary>
    public static async Task<bool> CompleteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ClaimedWorkItem item,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(item);
        await using var command = new NpgsqlCommand(
            "DELETE FROM axis.process_work_items WHERE id = @id AND claim_token = @token",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("token", item.ClaimToken);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
