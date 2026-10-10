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
    private const string ClaimSql = """
        UPDATE axis.process_work_items w
        SET claim_token = gen_random_uuid(), lease_expires_at = now() + @lease
        FROM (SELECT id FROM axis.process_work_items
              WHERE tenant_id = @tenant AND kind = ANY(@kinds) AND due_at <= now()
                AND (lease_expires_at IS NULL OR lease_expires_at <= now())
              ORDER BY due_at, id LIMIT 1 FOR UPDATE SKIP LOCKED) c
        WHERE w.id = c.id
        RETURNING w.id, w.tenant_id, w.kind, w.claim_token, w.lease_expires_at
        """;

    /// <summary>
    /// Adds a ready work item and returns its id. <paramref name="processInstanceId"/> names the
    /// process instance the item runs a step of, when it does.
    /// </summary>
    public static async Task<Guid> EnqueueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string tenantId,
        string kind,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken,
        Guid? processInstanceId = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, process_instance_id)
            VALUES (@id, @tenant, @kind, @due, @instance)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("due", dueAt.ToUniversalTime());
        command.Parameters.Add(new NpgsqlParameter("instance", NpgsqlDbType.Uuid) { Value = (object?)processInstanceId ?? DBNull.Value });
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
            reader.GetFieldValue<DateTimeOffset>(4));
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
