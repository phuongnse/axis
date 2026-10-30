using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Axis.Core.Telemetry;
using Axis.Expressions.Sql;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Work;

/// <summary>
/// Reads and writes <c>axis.process_work_items</c> over a connection to a tenant database. Due times
/// read the transaction time, which a test scenario's <c>axis.now</c> fixes, and are the real time
/// otherwise. Lease times use the database clock, so workers on different machines agree when a
/// lease expires.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "It is the queue of ready work items.")]
public static class WorkItemQueue
{
    internal const string ClaimSql = $"""
        UPDATE axis.process_work_items w
        SET claim_token = gen_random_uuid(), lease_expires_at = now() + @lease
        FROM (SELECT id FROM axis.process_work_items
              WHERE {ClaimFilterSql}
              ORDER BY due_at, id LIMIT 1 FOR UPDATE SKIP LOCKED) c
        WHERE w.id = c.id
        RETURNING {ClaimedColumnsSql}, {ClaimDelaySql}
        """;

    // The scenario runner claims in queue order. A due time comes from the clock, which can step back
    // during a run, and then an item queued later would run before an earlier one.
    internal const string ClaimInQueueOrderSql = $"""
        UPDATE axis.process_work_items w
        SET claim_token = gen_random_uuid(), lease_expires_at = now() + @lease
        FROM (SELECT id FROM axis.process_work_items
              WHERE {ClaimFilterSql}
              ORDER BY seq LIMIT 1 FOR UPDATE SKIP LOCKED) c
        WHERE w.id = c.id
        RETURNING {ClaimedColumnsSql}, {ClaimDelaySql}
        """;

    private const string ClaimFilterSql = $"""
        tenant_id = @tenant AND kind = ANY(@kinds) AND due_at <= {SqlTranslator.NowSql}
                AND (lease_expires_at IS NULL OR lease_expires_at <= now())
        """;

    private const string ClaimedColumnsSql = "w.id, w.tenant_id, w.kind, w.claim_token, w.lease_expires_at, w.process_instance_id, w.connector_call_id, w.event_id, w.package_call_id, w.trigger_id, w.release_id, w.occurrence_at, w.cursor, w.entity_id, w.trace_id, w.span_id, w.task_id, w.revision, w.escalation, w.email_id";

    private const string ClaimDelaySql = $"extract(epoch FROM {SqlTranslator.NowSql} - w.due_at)::double precision";

    internal const string ReadDueSql = $"""
        SELECT kind, count(*), extract(epoch FROM {SqlTranslator.NowSql} - min(due_at))::double precision
        FROM axis.process_work_items
        WHERE tenant_id = @tenant AND due_at <= {SqlTranslator.NowSql}
        GROUP BY kind
        """;

    /// <summary>
    /// Adds a ready work item and returns its id. <paramref name="processInstanceId"/> names the
    /// process instance the item runs a step or a connector call of, when it does,
    /// <paramref name="connectorCallId"/> the connector call it sends, <paramref name="eventId"/>
    /// the event occurrence it delivers, <paramref name="packageCallId"/> the package call it runs,
    /// <paramref name="triggerId"/> the schedule trigger it runs, <paramref name="releaseId"/> the
    /// release whose trigger a fan-out runs, <paramref name="occurrenceAt"/> its occurrence,
    /// <paramref name="taskId"/> the task a deadline expires or an escalation runs on,
    /// <paramref name="revision"/> the revision of the instance that a timer item resumes,
    /// <paramref name="escalation"/> the zero-based index of the escalation that a <c>task.escalate</c> item runs,
    /// and <paramref name="emailId"/> the outbox email a <c>notification.email</c> item sends.
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
        Guid? eventId = null,
        Guid? packageCallId = null,
        Guid? triggerId = null,
        Guid? releaseId = null,
        DateTimeOffset? occurrenceAt = null,
        Guid? taskId = null,
        long? revision = null,
        int? escalation = null,
        Guid? emailId = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, process_instance_id, connector_call_id, event_id, package_call_id, trigger_id, release_id, occurrence_at, trace_id, span_id, task_id, revision, escalation, email_id)
            VALUES (@id, @tenant, @kind, @due, @instance, @call, @event, @packageCall, @trigger, @release, @occurrence, @trace, @span, @task, @revision, @escalation, @email)
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
        command.Parameters.Add(new NpgsqlParameter("packageCall", NpgsqlDbType.Uuid) { Value = (object?)packageCallId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("trigger", NpgsqlDbType.Uuid) { Value = (object?)triggerId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("release", NpgsqlDbType.Uuid) { Value = (object?)releaseId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("occurrence", NpgsqlDbType.TimestampTz) { Value = occurrenceAt is { } occurrence ? occurrence.ToUniversalTime() : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("trace", NpgsqlDbType.Text) { Value = (object?)AxisTelemetry.CurrentTraceId() ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("span", NpgsqlDbType.Text) { Value = (object?)AxisTelemetry.CurrentSpanId() ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("task", NpgsqlDbType.Uuid) { Value = (object?)taskId ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("revision", NpgsqlDbType.Bigint) { Value = revision is { } rev ? rev : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("escalation", NpgsqlDbType.Integer) { Value = escalation is { } index ? index : DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("email", NpgsqlDbType.Uuid) { Value = (object?)emailId ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    /// <summary>
    /// Claims the earliest due item of the tenant and of one of the kinds that is not held by a live
    /// lease. The claim gets a new token and a lease of <paramref name="lease"/>. Concurrent callers
    /// never claim the same item. Returns <see langword="null"/> when no item is ready. With
    /// <paramref name="inQueueOrder"/> the claim is the item queued first of those that are due, not
    /// the one due first.
    /// </summary>
    public static async Task<ClaimedWorkItem?> ClaimAsync(
        NpgsqlConnection connection,
        string tenantId,
        IReadOnlyCollection<string> kinds,
        TimeSpan lease,
        CancellationToken cancellationToken,
        bool inQueueOrder = false)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(kinds);
        await using var command = new NpgsqlCommand(inQueueOrder ? ClaimInQueueOrderSql : ClaimSql, connection);
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
            await reader.IsDBNullAsync(8, cancellationToken) ? null : reader.GetGuid(8),
            await reader.IsDBNullAsync(9, cancellationToken) ? null : reader.GetGuid(9),
            await reader.IsDBNullAsync(10, cancellationToken) ? null : reader.GetGuid(10),
            await reader.IsDBNullAsync(11, cancellationToken) ? null : reader.GetFieldValue<DateTimeOffset>(11),
            await reader.IsDBNullAsync(12, cancellationToken) ? null : reader.GetGuid(12),
            lease,
            await reader.IsDBNullAsync(13, cancellationToken) ? null : reader.GetGuid(13),
            QueuedBy(
                await reader.IsDBNullAsync(14, cancellationToken) ? null : reader.GetString(14),
                await reader.IsDBNullAsync(15, cancellationToken) ? null : reader.GetString(15)),
            await reader.IsDBNullAsync(16, cancellationToken) ? null : reader.GetGuid(16),
            await reader.IsDBNullAsync(17, cancellationToken) ? null : reader.GetInt64(17),
            await reader.IsDBNullAsync(18, cancellationToken) ? null : reader.GetInt32(18),
            await reader.IsDBNullAsync(19, cancellationToken) ? null : reader.GetGuid(19),
            TimeSpan.FromSeconds(reader.GetDouble(20)));
    }

    /// <summary>
    /// Reads, for each kind that has an item whose due time has passed, how many items there are and how many
    /// seconds the oldest has been due. A claimed item counts until it is done, so a hung item keeps aging.
    /// Time is the transaction time, which a test scenario's <c>axis.now</c> fixes, as it is for a claim.
    /// </summary>
    public static async Task<IReadOnlyList<WorkItemGauge>> ReadDueAsync(
        NpgsqlConnection connection,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var command = new NpgsqlCommand(ReadDueSql, connection);
        command.Parameters.AddWithValue("tenant", tenantId);
        var rows = new List<WorkItemGauge>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new WorkItemGauge(reader.GetString(0), reader.GetInt64(1), reader.GetDouble(2)));
        }

        return rows;
    }

    /// <summary>The span that queued an item, or <see langword="null"/> when the item stores none or an id that is not hex of the right length.</summary>
    private static ActivityContext? QueuedBy(string? traceId, string? spanId) =>
        traceId is { Length: 32 } && spanId is { Length: 16 } && IsHex(traceId) && IsHex(spanId)
            ? new ActivityContext(ActivityTraceId.CreateFromString(traceId), ActivitySpanId.CreateFromString(spanId), ActivityTraceFlags.Recorded, isRemote: true)
            : null;

    private static bool IsHex(string text) => text.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

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
            $"""
            UPDATE axis.process_work_items
            SET due_at = {SqlTranslator.NowSql} + @delay, claim_token = NULL, lease_expires_at = NULL
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
    /// Keeps the item and makes it due at <paramref name="dueAt"/>, in the transaction, if the claim
    /// is still current. The claim and its lease are released, so any worker can claim the item once
    /// it is due. Returns <see langword="false"/> when another worker has claimed it since, so the
    /// caller must roll back.
    /// </summary>
    public static async Task<bool> RescheduleAtAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ClaimedWorkItem item,
        DateTimeOffset dueAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(item);
        await using var command = new NpgsqlCommand(
            """
            UPDATE axis.process_work_items
            SET due_at = @due, claim_token = NULL, lease_expires_at = NULL
            WHERE id = @id AND claim_token = @token
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("due", dueAt.ToUniversalTime());
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("token", item.ClaimToken);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>
    /// Stores <paramref name="cursor"/> as the last record the item handled and makes the item due
    /// at once, in the transaction, if the claim is still current. The claim and its lease are
    /// released, so any worker can claim the item. Returns <see langword="false"/> when another
    /// worker has claimed it since, so the caller must roll back.
    /// </summary>
    public static async Task<bool> AdvanceCursorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ClaimedWorkItem item,
        Guid cursor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(item);
        await using var command = new NpgsqlCommand(
            $"""
            UPDATE axis.process_work_items
            SET cursor = @cursor, due_at = {SqlTranslator.NowSql}, claim_token = NULL, lease_expires_at = NULL
            WHERE id = @id AND claim_token = @token
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("cursor", cursor);
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
