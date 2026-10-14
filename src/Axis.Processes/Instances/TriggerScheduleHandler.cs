using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Runs a <c>trigger.schedule</c> work item when its schedule trigger's next occurrence is due. In
/// one transaction it locks the trigger's state row and finds the latest occurrence at or before
/// now. When the worker was down for several occurrences, only that one runs and the skipped ones
/// are logged. It records the occurrence, queues a <c>trigger.fanOut</c> item for it, and moves the
/// state and the item to the next occurrence after now. The state changes only while the stored next
/// occurrence is still the one this item read, so one worker runs each occurrence. A trigger with no state row, or that its release no longer
/// has, ends the item.
/// </summary>
internal sealed partial class TriggerScheduleHandler(ReleaseModelCache models, ILogger<TriggerScheduleHandler> logger) : IExternalWorkItemHandler
{
    public string Kind => ProcessStarts.TriggerScheduleWorkItemKind;

    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(connection);

        var triggerId = item.TriggerId ?? throw new InvalidOperationException($"The work item '{item.Id}' names no trigger.");
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid releaseId;
        DateTimeOffset occurrence;
        await using (var select = new NpgsqlCommand(
            "SELECT release_id, next_occurrence_at FROM axis.trigger_schedules WHERE trigger_id = @trigger FOR UPDATE",
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("trigger", triggerId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.CloseAsync();
                return await EndAsync(item, connection, transaction, "has no schedule state", cancellationToken);
            }

            releaseId = reader.GetGuid(0);
            occurrence = reader.GetFieldValue<DateTimeOffset>(1);
        }

        var model = await models.GetAsync(new WorkItemContext(item, connection, transaction), releaseId, cancellationToken);
        var trigger = model.Triggers.FirstOrDefault(candidate => candidate.Id == triggerId && candidate.Type == TriggerType.Schedule);
        if (trigger is null)
        {
            return await EndAsync(item, connection, transaction, "is not a schedule trigger of its release", cancellationToken);
        }

        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var schedule = trigger.Schedule!;
        var timeZone = trigger.TimeZone!;
        var latest = occurrence;
        var skipped = 0;
        for (var candidate = schedule.NextAfter(latest, timeZone); candidate <= now; candidate = schedule.NextAfter(latest, timeZone))
        {
            latest = candidate;
            skipped++;
        }

        var next = schedule.NextAfter(now, timeZone);
        await using (var update = new NpgsqlCommand(
            """
            UPDATE axis.trigger_schedules SET last_occurrence_at = @latest, next_occurrence_at = @next
            WHERE trigger_id = @trigger AND next_occurrence_at = @occurrence
            """,
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("occurrence", occurrence.ToUniversalTime());
            update.Parameters.AddWithValue("latest", latest.ToUniversalTime());
            update.Parameters.AddWithValue("next", next.ToUniversalTime());
            update.Parameters.AddWithValue("trigger", triggerId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, triggerId);
                return WorkItemOutcome.LostClaim;
            }
        }

        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            item.TenantId,
            ProcessStarts.TriggerFanOutWorkItemKind,
            now,
            cancellationToken,
            triggerId: triggerId,
            releaseId: releaseId,
            occurrenceAt: latest);
        if (!await WorkItemQueue.RescheduleAtAsync(connection, transaction, item, next, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, triggerId);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
        if (skipped > 0)
        {
            LogSkippedOccurrences(trigger.Name, skipped, latest);
        }

        return WorkItemOutcome.Rescheduled;
    }

    /// <summary>Deletes the item because its trigger has nothing left to run, and logs why.</summary>
    private async Task<WorkItemOutcome> EndAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, item.TriggerId);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
        LogEnded(item.Id, item.TriggerId, reason);
        return WorkItemOutcome.Completed;
    }

    [LoggerMessage(LogLevel.Information, "Trigger {Trigger} skipped {Count} missed occurrences and ran the latest, {OccurrenceAt:o}.")]
    private partial void LogSkippedOccurrences(string trigger, int count, DateTimeOffset occurrenceAt);

    [LoggerMessage(LogLevel.Information, "Schedule work item {ItemId} was discarded: trigger {TriggerId} {Reason}.")]
    private partial void LogEnded(Guid itemId, Guid? triggerId, string reason);

    [LoggerMessage(LogLevel.Warning, "Schedule work item {ItemId} for trigger {TriggerId} was claimed by another worker or its occurrence already ran, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid? triggerId);
}
