using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Core.Telemetry;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Delivers the event occurrence an <c>event.deliver</c> work item names to the <c>async</c> event
/// triggers on its event in the application's active release. A first transaction reads the
/// occurrence, the active release and its triggers. With no occurrence, no active release or no
/// trigger, the item is deleted with no other write. Each trigger then gets its own transaction,
/// which checks the claim and extends the lease, and inserts the trigger's receipt in
/// <c>axis.trigger_starts</c> first. A receipt already there means the trigger handled the
/// occurrence, so a delivery run again after a crash starts nothing twice. The transaction first
/// takes the application's start lock shared and reads the active release again, so it pins the
/// release it finds under the lock. A trigger that this release no longer has is skipped with no
/// receipt, and a trigger that only this release has is not delivered for the occurrence. The
/// transaction then evaluates the trigger's <c>when</c> on the stored payload, locks and reads the occurrence's
/// record, and starts the process for it as <c>system</c>, pinned to the active release. A start
/// marks the receipt <c>started</c>. A skip leaves it <c>skipped</c> and is logged with its reason.
/// A trigger whose transaction fails does not stop the others. After the failed transaction has
/// rolled back, a transaction of its own counts the failure in <c>axis.delivery_failures</c> for the
/// trigger and the event, storing only the exception type and SQLSTATE. At the limit of
/// <see cref="DeliverySettings.Attempts"/> failures it writes an <c>open</c> dead letter and a
/// <c>deadLettered</c> receipt instead, so the trigger is not tried again. A last transaction
/// deletes the item. When a trigger failed below the limit it instead makes the item due again
/// after the shortest backoff among the failed triggers of the pass. An item that names a trigger,
/// queued by an operator's redeliver of a dead letter, delivers to that trigger alone, and is deleted
/// when the active release no longer has it.
/// </summary>
internal sealed partial class EventDeliveryHandler(ReleaseModelCache models, DeliverySettings settings, ILogger<EventDeliveryHandler> logger) : IDeliveryWorkItemHandler
{
    public string Kind => ProcessStarts.EventDeliverWorkItemKind;

    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(connection);

        StoredEvent? occurrence;
        EventTriggers? delivery = null;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            occurrence = item.EventId is { } eventId ? await ProcessEvents.LoadAsync(transaction, eventId, cancellationToken) : null;
            if (occurrence is null)
            {
                LogNoEvent(item.Id, item.EventId);
            }
            else
            {
                delivery = await FindDeliveryAsync(item, connection, transaction, occurrence, cancellationToken);
            }

            if (delivery is null)
            {
                if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    LogLostClaim(item.Id, item.EventId);
                    return WorkItemOutcome.LostClaim;
                }

                await transaction.CommitAsync(cancellationToken);
                return WorkItemOutcome.Completed;
            }

            // The read wrote nothing.
            await transaction.RollbackAsync(cancellationToken);
        }

        TimeSpan? backoff = null;
        foreach (var trigger in delivery.Triggers)
        {
            try
            {
                if (!await DeliverAsync(item, connection, occurrence!, trigger, cancellationToken))
                {
                    return WorkItemOutcome.LostClaim;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The failed transaction is disposed, so it has rolled back and the connection is free.
                var (held, delay) = await CountFailureAsync(item, connection, occurrence!, trigger, exception, cancellationToken);
                if (!held)
                {
                    return WorkItemOutcome.LostClaim;
                }

                if (delay is { } wait && (backoff is not { } shortest || wait < shortest))
                {
                    backoff = wait;
                }
            }
        }

        await using var completion = await connection.BeginTransactionAsync(cancellationToken);
        var settled = backoff is { } due
            ? await WorkItemQueue.RescheduleAsync(connection, completion, item, due, cancellationToken)
            : await WorkItemQueue.CompleteAsync(connection, completion, item, cancellationToken);
        if (!settled)
        {
            await completion.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, item.EventId);
            return WorkItemOutcome.LostClaim;
        }

        await completion.CommitAsync(cancellationToken);
        return backoff is null ? WorkItemOutcome.Completed : WorkItemOutcome.Rescheduled;
    }

    /// <summary>
    /// Counts a failed start of one trigger in a transaction of its own. Below the limit it returns
    /// the backoff after which the item is due again. At the limit it writes the dead letter and the
    /// <c>deadLettered</c> receipt and returns no backoff. Returns <c>Held</c> as <see langword="false"/>
    /// when another worker holds the item now, so nothing was written.
    /// </summary>
    private async Task<(bool Held, TimeSpan? Delay)> CountFailureAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        StoredEvent occurrence,
        TriggerModel trigger,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // Only the type and SQLSTATE are kept: the message of the exception can quote a payload value.
        var error = DeliveryFailures.ErrorText(exception);
        var target = $"{trigger.Id:D}/{occurrence.Id:D}";
        int attempts;
        Guid? deadLetterId = null;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, item.Lease, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, item.EventId);
                return (false, null);
            }

            attempts = await DeliveryFailures.CountAsync(transaction, DeliveryFailures.EventDeliverKind, target, error, cancellationToken);
            if (attempts >= settings.Attempts)
            {
                deadLetterId = await DeliveryFailures.InsertDeadLetterAsync(
                    transaction, occurrence.ApplicationId, trigger.Process.Id, occurrence.Id, trigger.Id, attempts, error, cancellationToken);
                await TriggerStarts.InsertDeadLetteredAsync(transaction, trigger.Id, occurrence.Id.ToString("D"), occurrence.SubjectId, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        if (deadLetterId is { } deadLetter)
        {
            AxisMetrics.RecordDeadLetter(item.TenantId, DeliveryFailures.EventDeliverKind);
            LogDeadLettered(trigger.Id, occurrence.Id, attempts, deadLetter, DeliveryFailures.EventDeliverKind);
            return (true, null);
        }

        var delay = DeliveryFailures.Backoff(attempts);
        LogDeliveryFailed(trigger.Name, occurrence.Id, error, attempts, settings.Attempts, delay);
        return (true, delay);
    }

    /// <summary>
    /// Finds the active release of the occurrence's application and its <c>async</c> triggers on the
    /// event, ignoring letter case. An item that names a trigger keeps that trigger only. Returns
    /// <see langword="null"/> when there is no active release or no such trigger.
    /// </summary>
    private async Task<EventTriggers?> FindDeliveryAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StoredEvent occurrence,
        CancellationToken cancellationToken)
    {
        var found = await EventTriggerStarts.FindAsync(
            new WorkItemContext(item, connection, transaction),
            models,
            occurrence.ApplicationId,
            occurrence.Event,
            TriggerMode.Async,
            cancellationToken);
        if (found is null)
        {
            LogNoActiveRelease(occurrence.Id, occurrence.ApplicationId);
            return null;
        }

        if (item.TriggerId is { } triggerId)
        {
            found = found with { Triggers = [.. found.Triggers.Where(trigger => trigger.Id == triggerId)] };
            if (found.Triggers.Count == 0)
            {
                LogNamedTriggerGone(item.Id, triggerId, occurrence.Id);
            }
        }

        return found.Triggers.Count == 0 ? null : found;
    }

    /// <summary>
    /// Delivers the occurrence to one trigger in its own transaction. Returns <see langword="false"/>
    /// when another worker holds the item now, so nothing was written.
    /// </summary>
    private async Task<bool> DeliverAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        StoredEvent occurrence,
        TriggerModel trigger,
        CancellationToken cancellationToken)
    {
        string? skipped;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, item.Lease, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, item.EventId);
                return false;
            }

            // The start lock comes before any entity table is read, so a migrating activation cannot
            // deadlock with this start. The active release is read again under it: an activation may
            // have committed since the triggers were found, and a start pins the release it finds now.
            await StartLock.TakeSharedAsync(transaction, occurrence.ApplicationId, cancellationToken);
            var current = await EventTriggerStarts.FindAsync(
                new WorkItemContext(item, connection, transaction),
                models,
                occurrence.ApplicationId,
                occurrence.Event,
                TriggerMode.Async,
                cancellationToken);
            var found = current?.Event is null ? null : current.Triggers.FirstOrDefault(candidate => candidate.Id == trigger.Id);
            skipped = found is null
                ? "the trigger is not in the active release"
                : await StartAsync(item, transaction, current!, occurrence, found, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        if (skipped is not null)
        {
            LogSkipped(trigger.Name, trigger.Process.Name, occurrence.SubjectId, occurrence.Id, skipped);
        }

        return true;
    }

    /// <summary>
    /// Inserts the trigger's receipt, then starts its process for the occurrence's record. Returns the
    /// reason the start was skipped, or <see langword="null"/> when it happened.
    /// </summary>
    private static async Task<string?> StartAsync(
        ClaimedWorkItem item,
        NpgsqlTransaction transaction,
        EventTriggers delivery,
        StoredEvent occurrence,
        TriggerModel trigger,
        CancellationToken cancellationToken)
    {
        var source = occurrence.Id.ToString("D");

        // The receipt goes first, so a concurrent or repeated delivery waits on its key and then skips.
        if (!await TriggerStarts.TryInsertAsync(transaction, trigger.Id, source, occurrence.SubjectId, cancellationToken))
        {
            return "the trigger already handled the occurrence";
        }

        // A delivery has a trigger only when the active release declares the event.
        var (instanceId, skipped) = await EventTriggerStarts.StartAsync(
            transaction, item.TenantId, delivery.Model, delivery.ReleaseId, delivery.Event!, occurrence, trigger, cancellationToken);
        if (instanceId is { } started)
        {
            await TriggerStarts.MarkStartedAsync(transaction, trigger.Id, source, occurrence.SubjectId, started, cancellationToken);
        }

        return skipped;
    }

    [LoggerMessage(LogLevel.Information, "Trigger {Trigger} skipped the start of process {Process} for record {SubjectId} of event {EventId}: {Reason}.")]
    private partial void LogSkipped(string trigger, string process, Guid subjectId, Guid eventId, string reason);

    [LoggerMessage(LogLevel.Warning, "Trigger {Trigger} failed to start for event {EventId} with {Error}, failure {Attempts} of {Limit}. Delivery is due again in {Delay}.")]
    private partial void LogDeliveryFailed(string trigger, Guid eventId, string error, int attempts, int limit, TimeSpan delay);

    [LoggerMessage(LogLevel.Warning, "Event delivery to trigger {TriggerId} for event {EventId} failed {Attempts} times and became dead letter {DeadLetterId} of kind {Kind}.")]
    private partial void LogDeadLettered(Guid triggerId, Guid eventId, int attempts, Guid deadLetterId, string kind);

    [LoggerMessage(LogLevel.Warning, "Event delivery work item {ItemId} names no stored event {EventId}, so it was discarded.")]
    private partial void LogNoEvent(Guid itemId, Guid? eventId);

    [LoggerMessage(LogLevel.Information, "Event {EventId} was delivered to no trigger: application {ApplicationId} has no active release.")]
    private partial void LogNoActiveRelease(Guid eventId, Guid applicationId);

    [LoggerMessage(LogLevel.Warning, "Event delivery work item {ItemId} names trigger {TriggerId}, which the active release does not have as an async trigger on event {EventId}, so it was discarded.")]
    private partial void LogNamedTriggerGone(Guid itemId, Guid triggerId, Guid eventId);

    [LoggerMessage(LogLevel.Warning, "Event delivery work item {ItemId} for event {EventId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid? eventId);
}
