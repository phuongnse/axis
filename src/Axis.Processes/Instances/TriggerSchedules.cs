using Axis.Configuration.Model;
using Axis.Core.Telemetry;
using Axis.Data.Records;
using Axis.Processes.Work;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the state of schedule triggers in <c>axis.trigger_schedules</c> and their
/// <c>trigger.schedule</c> work items over a connection to a tenant database. Activating a release
/// calls it once the release is active. The worker calls <see cref="SkipMissedAsync"/> when a read-only tenant is active again.
/// </summary>
public static class TriggerSchedules
{
    /// <summary>
    /// Reads the state rows of the schedule triggers over <paramref name="connection"/>: every row, or only the rows of
    /// <paramref name="applicationId"/> when it is given. The order is not defined.
    /// </summary>
    public static async Task<IReadOnlyList<TriggerScheduleEntry>> ListAsync(
        NpgsqlConnection connection,
        Guid? applicationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = new NpgsqlCommand(
            """
            SELECT trigger_id, application_id, schedule, time_zone, next_occurrence_at, last_occurrence_at
            FROM axis.trigger_schedules
            WHERE @application::uuid IS NULL OR application_id = @application
            """,
            connection);
        command.Parameters.Add(new NpgsqlParameter("application", NpgsqlDbType.Uuid) { Value = (object?)applicationId ?? DBNull.Value });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<TriggerScheduleEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new TriggerScheduleEntry(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return entries;
    }

    /// <summary>
    /// In one transaction, stores for each schedule trigger of <paramref name="model"/> its state
    /// row and its <c>trigger.schedule</c> work item, both at the trigger's next occurrence, and
    /// releases a claim on the item. A new or changed trigger gets its next occurrence after the
    /// transaction's time, and a trigger with the same <c>schedule</c> and <c>timeZone</c> keeps its
    /// stored one, even a past one, so the worker catches up after downtime. A trigger the application had
    /// before and <paramref name="model"/> no longer has loses its row and its item. The row keeps
    /// the trigger's latest run occurrence. Triggers of other applications are untouched. A trigger whose occurrence has
    /// already passed adds <paramref name="tenantId"/> to the open <see cref="WorkSignals"/> scope.
    /// </summary>
    public static async Task ActivateAsync(
        NpgsqlConnection connection,
        string tenantId,
        ApplicationModel model,
        Guid releaseId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(model);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var applicationId = model.Manifest.Id;
        var kept = new List<Guid>();
        foreach (var trigger in model.Triggers.Where(trigger => trigger.Type == TriggerType.Schedule))
        {
            var next = trigger.Schedule!.NextAfter(now, trigger.TimeZone!);
            kept.Add(trigger.Id);
            DateTimeOffset due;
            await using (var state = new NpgsqlCommand(
                """
                INSERT INTO axis.trigger_schedules (trigger_id, application_id, release_id, schedule, time_zone, next_occurrence_at)
                VALUES (@trigger, @application, @release, @schedule, @timeZone, @next)
                ON CONFLICT (trigger_id) DO UPDATE
                SET application_id = EXCLUDED.application_id, release_id = EXCLUDED.release_id,
                    schedule = EXCLUDED.schedule, time_zone = EXCLUDED.time_zone,
                    next_occurrence_at = CASE
                        WHEN axis.trigger_schedules.schedule = EXCLUDED.schedule AND axis.trigger_schedules.time_zone = EXCLUDED.time_zone
                        THEN axis.trigger_schedules.next_occurrence_at
                        ELSE EXCLUDED.next_occurrence_at END
                RETURNING next_occurrence_at
                """,
                connection,
                transaction))
            {
                state.Parameters.AddWithValue("trigger", trigger.Id);
                state.Parameters.AddWithValue("application", applicationId);
                state.Parameters.AddWithValue("release", releaseId);
                state.Parameters.AddWithValue("schedule", trigger.Schedule!.Expression);
                state.Parameters.AddWithValue("timeZone", trigger.TimeZone!.Id);
                state.Parameters.AddWithValue("next", next.ToUniversalTime());
                due = new DateTimeOffset((DateTime)(await state.ExecuteScalarAsync(cancellationToken))!);
            }

            await using var item = new NpgsqlCommand(
                """
                INSERT INTO axis.process_work_items (id, tenant_id, kind, due_at, trigger_id, trace_id, span_id)
                VALUES (@id, @tenant, @kind, @next, @trigger, @trace, @span)
                ON CONFLICT (trigger_id) WHERE kind = 'trigger.schedule' DO UPDATE
                SET due_at = EXCLUDED.due_at, claim_token = NULL, lease_expires_at = NULL,
                    trace_id = EXCLUDED.trace_id, span_id = EXCLUDED.span_id
                """,
                connection,
                transaction);
            item.Parameters.AddWithValue("id", Guid.CreateVersion7());
            item.Parameters.AddWithValue("tenant", tenantId);
            item.Parameters.AddWithValue("kind", ProcessStarts.TriggerScheduleWorkItemKind);
            item.Parameters.AddWithValue("next", due.ToUniversalTime());
            item.Parameters.AddWithValue("trigger", trigger.Id);
            item.Parameters.Add(new NpgsqlParameter("trace", NpgsqlDbType.Text) { Value = (object?)AxisTelemetry.CurrentTraceId() ?? DBNull.Value });
            item.Parameters.Add(new NpgsqlParameter("span", NpgsqlDbType.Text) { Value = (object?)AxisTelemetry.CurrentSpanId() ?? DBNull.Value });
            await item.ExecuteNonQueryAsync(cancellationToken);
            if (due <= now)
            {
                WorkSignals.Add(tenantId);
            }
        }

        var removed = new List<Guid>();
        await using (var delete = new NpgsqlCommand(
            "DELETE FROM axis.trigger_schedules WHERE application_id = @application AND NOT (trigger_id = ANY(@kept)) RETURNING trigger_id",
            connection,
            transaction))
        {
            delete.Parameters.AddWithValue("application", applicationId);
            delete.Parameters.AddWithValue("kept", NpgsqlDbType.Array | NpgsqlDbType.Uuid, kept.ToArray());
            await using var reader = await delete.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                removed.Add(reader.GetGuid(0));
            }
        }

        if (removed.Count > 0)
        {
            await using var items = new NpgsqlCommand(
                "DELETE FROM axis.process_work_items WHERE kind = @kind AND trigger_id = ANY(@removed)",
                connection,
                transaction);
            items.Parameters.AddWithValue("kind", ProcessStarts.TriggerScheduleWorkItemKind);
            items.Parameters.AddWithValue("removed", NpgsqlDbType.Array | NpgsqlDbType.Uuid, removed.ToArray());
            await items.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// In one transaction, moves every schedule trigger whose next occurrence has passed to its next occurrence after the
    /// transaction's time, and its <c>trigger.schedule</c> work item with it, so none of the passed occurrences runs.
    /// The worker calls it for a tenant that was read-only. The latest run occurrence stays. A row whose
    /// <c>schedule</c> or <c>timeZone</c> cannot be read is left alone.
    /// </summary>
    /// <returns>The triggers that were moved, with the number of occurrences that fell at or before the transaction's time.</returns>
    public static async Task<IReadOnlyList<SkippedSchedule>> SkipMissedAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var due = new List<(Guid TriggerId, string Schedule, string TimeZone, DateTimeOffset Occurrence)>();
        await using (var select = new NpgsqlCommand(
            """
            SELECT trigger_id, schedule, time_zone, next_occurrence_at
            FROM axis.trigger_schedules
            WHERE next_occurrence_at <= @now AND schedule IS NOT NULL AND time_zone IS NOT NULL
            FOR UPDATE
            """,
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("now", now.ToUniversalTime());
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                due.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3)));
            }
        }

        var skipped = new List<SkippedSchedule>();
        foreach (var (triggerId, expression, zoneId, occurrence) in due)
        {
            if (!CronSchedule.TryParse(expression, out var schedule, out _)
                || !TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone))
            {
                continue;
            }

            var latest = occurrence;
            var count = 1;
            for (var candidate = schedule.NextAfter(latest, zone); candidate <= now; candidate = schedule.NextAfter(latest, zone))
            {
                latest = candidate;
                count++;
            }

            var next = schedule.NextAfter(now, zone);
            await using (var state = new NpgsqlCommand(
                "UPDATE axis.trigger_schedules SET next_occurrence_at = @next WHERE trigger_id = @trigger",
                connection,
                transaction))
            {
                state.Parameters.AddWithValue("next", next.ToUniversalTime());
                state.Parameters.AddWithValue("trigger", triggerId);
                await state.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var item = new NpgsqlCommand(
                """
                UPDATE axis.process_work_items SET due_at = @next, claim_token = NULL, lease_expires_at = NULL
                WHERE kind = @kind AND trigger_id = @trigger
                """,
                connection,
                transaction))
            {
                item.Parameters.AddWithValue("next", next.ToUniversalTime());
                item.Parameters.AddWithValue("kind", ProcessStarts.TriggerScheduleWorkItemKind);
                item.Parameters.AddWithValue("trigger", triggerId);
                await item.ExecuteNonQueryAsync(cancellationToken);
            }

            skipped.Add(new SkippedSchedule(triggerId, count, next));
        }

        await transaction.CommitAsync(cancellationToken);
        return skipped;
    }
}

/// <summary>A schedule trigger that <see cref="TriggerSchedules.SkipMissedAsync"/> moved: how many occurrences it skipped and its next one.</summary>
public sealed record SkippedSchedule(Guid TriggerId, int Count, DateTimeOffset NextOccurrenceAt);

/// <summary>
/// The stored state of a schedule trigger: its <c>schedule</c> and <c>timeZone</c> as activation stored them, the next
/// occurrence and the latest occurrence that ran, which is null while none has.
/// </summary>
public sealed record TriggerScheduleEntry(
    Guid TriggerId,
    Guid ApplicationId,
    string? Schedule,
    string? TimeZone,
    DateTimeOffset NextOccurrenceAt,
    DateTimeOffset? LastOccurrenceAt);
