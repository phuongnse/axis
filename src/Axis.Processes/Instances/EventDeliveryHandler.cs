using Axis.Configuration.Model;
using Axis.Configuration.Releases;
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
/// A last transaction deletes the item.
/// </summary>
internal sealed partial class EventDeliveryHandler(ReleaseModelCache models, ILogger<EventDeliveryHandler> logger) : IExternalWorkItemHandler
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

        foreach (var trigger in delivery.Triggers)
        {
            if (!await DeliverAsync(item, connection, occurrence!, trigger, cancellationToken))
            {
                return WorkItemOutcome.LostClaim;
            }
        }

        await using var completion = await connection.BeginTransactionAsync(cancellationToken);
        if (!await WorkItemQueue.CompleteAsync(connection, completion, item, cancellationToken))
        {
            await completion.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, item.EventId);
            return WorkItemOutcome.LostClaim;
        }

        await completion.CommitAsync(cancellationToken);
        return WorkItemOutcome.Completed;
    }

    /// <summary>
    /// Finds the active release of the occurrence's application and its <c>async</c> triggers on the
    /// event, ignoring letter case. Returns <see langword="null"/> when there is no active release or
    /// no such trigger.
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

    [LoggerMessage(LogLevel.Warning, "Event delivery work item {ItemId} names no stored event {EventId}, so it was discarded.")]
    private partial void LogNoEvent(Guid itemId, Guid? eventId);

    [LoggerMessage(LogLevel.Information, "Event {EventId} was delivered to no trigger: application {ApplicationId} has no active release.")]
    private partial void LogNoActiveRelease(Guid eventId, Guid applicationId);

    [LoggerMessage(LogLevel.Warning, "Event delivery work item {ItemId} for event {EventId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid? eventId);
}
