using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Runs a <c>trigger.schedule</c> work item when its schedule trigger's next occurrence is due. In
/// one transaction it locks the trigger's state row, records the occurrence, queues a
/// <c>trigger.fanOut</c> item for it, and moves the state and the item to the next occurrence after
/// now. The state changes only while the stored next occurrence is still the one this item ran, so
/// one worker runs each occurrence. A trigger with no state row, or that its release no longer
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
        var next = trigger.Schedule!.NextAfter(now, trigger.TimeZone!);
        await using (var update = new NpgsqlCommand(
            """
            UPDATE axis.trigger_schedules SET last_occurrence_at = @occurrence, next_occurrence_at = @next
            WHERE trigger_id = @trigger AND next_occurrence_at = @occurrence
            """,
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("occurrence", occurrence.ToUniversalTime());
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
            occurrenceAt: occurrence);
        if (!await WorkItemQueue.RescheduleAtAsync(connection, transaction, item, next, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, triggerId);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
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

    [LoggerMessage(LogLevel.Information, "Schedule work item {ItemId} was discarded: trigger {TriggerId} {Reason}.")]
    private partial void LogEnded(Guid itemId, Guid? triggerId, string reason);

    [LoggerMessage(LogLevel.Warning, "Schedule work item {ItemId} for trigger {TriggerId} was claimed by another worker or its occurrence already ran, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid? triggerId);
}
