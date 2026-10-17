using Axis.Configuration.Model;
using Axis.Core.Telemetry;
using Axis.Data.Records;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the state of schedule triggers in <c>axis.trigger_schedules</c> and their
/// <c>trigger.schedule</c> work items over a connection to a tenant database. Activating a release
/// calls it once the release is active.
/// </summary>
public static class TriggerSchedules
{
    /// <summary>
    /// In one transaction, stores for each schedule trigger of <paramref name="model"/> its state
    /// row and its <c>trigger.schedule</c> work item, both at the trigger's next occurrence, and
    /// releases a claim on the item. A new or changed trigger gets its next occurrence after the
    /// transaction's time, and a trigger with the same <c>schedule</c> and <c>timeZone</c> keeps its
    /// stored one, even a past one, so the worker catches up after downtime. A trigger the application had
    /// before and <paramref name="model"/> no longer has loses its row and its item. The row keeps
    /// the trigger's latest run occurrence. Triggers of other applications are untouched.
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
}
