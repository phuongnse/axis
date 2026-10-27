using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Core.Telemetry;
using Axis.Data.Audit;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Delivers the event occurrence an <c>event.deliver</c> work item names to the <c>async</c> event
/// triggers on its event in the application's active release, then wakes the process instances that
/// wait for it. A first transaction takes the record's event lock, then reads the occurrence, the
/// active release with its triggers, and the instances <c>waiting</c> in a <c>waitEvent</c> step for
/// the event on the occurrence's record. With no occurrence, or with neither a trigger nor a waiting
/// instance, the item is deleted with no other write. A missing active release gives no trigger and
/// still wakes the waiting instances. Each trigger then gets its own transaction,
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
/// <c>deadLettered</c> receipt instead, so the trigger is not tried again. After the triggers, each
/// waiting instance gets its own transaction, which checks the claim and extends the lease, reloads
/// the instance and skips it unless it still waits in the same step at the same revision. It then
/// evaluates the step's <c>when</c> on the instance's own release, records the match in
/// <c>axis.process_event_matches</c> and resumes the instance at the step's <c>next</c>, with the
/// history row, the <c>process.stepCompleted</c> audit record and the instance's next
/// <c>process.step</c> item. A match that already exists skips the wake, so a delivery run again wakes
/// nothing twice. An instance whose timeout resumed it first is no longer waiting and is skipped. A
/// failed wake is not counted: its exception leaves the item claimed until its lease expires, and
/// the item is then tried again. A last transaction
/// deletes the item. When a trigger failed below the limit it instead makes the item due again
/// after the shortest backoff among the failed triggers of the pass. An item that names a trigger,
/// queued by an operator's redeliver of a dead letter, delivers to that trigger alone, wakes no
/// instance, and is deleted when the active release no longer has it.
/// </summary>
internal sealed partial class EventDeliveryHandler(ReleaseModelCache models, DeliverySettings settings, ILogger<EventDeliveryHandler> logger) : IDeliveryWorkItemHandler
{
    /// <summary>Lists the instances waiting on a record that were created at or before an occurrence was raised.</summary>
    internal const string WaitingSql =
        """
        SELECT i.id, i.process_id, i.release_id, i.step, i.revision
        FROM axis.process_instances i
        WHERE i.application_id = @application
          AND i.subject_entity_id = @entity
          AND i.subject_id = @subject
          AND i.state = @waiting
          AND i.created_at <= (SELECT e.raised_at FROM axis.events e WHERE e.id = @event)
        ORDER BY i.created_at, i.id
        """;

    public string Kind => ProcessStarts.EventDeliverWorkItemKind;

    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(connection);

        StoredEvent? occurrence;
        EventTriggers? delivery = null;
        IReadOnlyList<WaitingInstance> waiting = [];
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            occurrence = item.EventId is { } eventId ? await ProcessEvents.LoadAsync(transaction, eventId, cancellationToken) : null;
            if (occurrence is null)
            {
                LogNoEvent(item.Id, item.EventId);
            }
            else
            {
                // An item that names a trigger is a redeliver to that trigger, so it wakes no instance.
                if (item.TriggerId is null)
                {
                    // The lock is the one a waitEvent step takes before it looks for an occurrence, so this
                    // read sees the waiting instance or the step sees the occurrence.
                    await RecordEventLock.TakeAsync(transaction, occurrence.SubjectId, cancellationToken);
                    waiting = await FindWaitingAsync(new WorkItemContext(item, connection, transaction), occurrence, cancellationToken);
                }

                delivery = await FindDeliveryAsync(item, connection, transaction, occurrence, waiting.Count > 0, cancellationToken);
            }

            if (delivery is null && waiting.Count == 0)
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
        foreach (var trigger in delivery?.Triggers ?? [])
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

        // A wake is not counted when it fails: its exception leaves the item claimed, and the item is tried again after its lease.
        foreach (var instance in waiting)
        {
            if (!await WakeAsync(item, connection, occurrence!, instance, cancellationToken))
            {
                return WorkItemOutcome.LostClaim;
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
    /// <see langword="null"/> when there is no active release or no such trigger. A missing active
    /// release is logged unless an instance <paramref name="anyWaiting"/> for the event.
    /// </summary>
    private async Task<EventTriggers?> FindDeliveryAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StoredEvent occurrence,
        bool anyWaiting,
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
            if (!anyWaiting)
            {
                LogNoActiveRelease(occurrence.Id, occurrence.ApplicationId);
            }

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
    /// Finds the instances that wait in a <c>waitEvent</c> step for the occurrence's event on its
    /// record, earliest first. A step counts when its event is the occurrence's, ignoring letter case,
    /// in the release the instance is pinned to. Only an instance created at or before the
    /// occurrence's <c>raised_at</c> counts, as a <c>waitEvent</c> step uses only occurrences raised
    /// at or after the instance's creation.
    /// </summary>
    private async Task<IReadOnlyList<WaitingInstance>> FindWaitingAsync(WorkItemContext context, StoredEvent occurrence, CancellationToken cancellationToken)
    {
        List<WaitingInstance> candidates = [];
        await using (var command = new NpgsqlCommand(WaitingSql, context.Connection, context.Transaction))
        {
            command.Parameters.AddWithValue("application", occurrence.ApplicationId);
            command.Parameters.AddWithValue("entity", occurrence.EntityId);
            command.Parameters.AddWithValue("subject", occurrence.SubjectId);
            command.Parameters.AddWithValue("waiting", ProcessStarts.Waiting);
            command.Parameters.AddWithValue("event", occurrence.Id);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add(new WaitingInstance(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetInt64(4)));
            }
        }

        List<WaitingInstance> waiting = [];
        foreach (var candidate in candidates)
        {
            var model = await models.GetAsync(context, candidate.ReleaseId, cancellationToken);
            if (model.Processes.FirstOrDefault(process => process.Id == candidate.ProcessId) is { } process
                && process.TryGetStep(candidate.Step, out var step)
                && step is WaitEventStepModel waitStep
                && string.Equals(waitStep.Event, occurrence.Event, StringComparison.OrdinalIgnoreCase))
            {
                waiting.Add(candidate);
            }
        }

        return waiting;
    }

    /// <summary>
    /// Wakes one waiting instance in its own transaction. Returns <see langword="false"/> when another
    /// worker holds the item now, so nothing was written.
    /// </summary>
    private async Task<bool> WakeAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        StoredEvent occurrence,
        WaitingInstance waiting,
        CancellationToken cancellationToken)
    {
        string? skipped;
        string? next;
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, item.Lease, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, item.EventId);
                return false;
            }

            (skipped, next) = await ResumeAsync(new WorkItemContext(item, connection, transaction), occurrence, waiting, cancellationToken);
            if (skipped is null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                // A skip can follow the match insert, so it rolls back the whole transaction.
                await transaction.RollbackAsync(cancellationToken);
            }
        }

        if (skipped is null)
        {
            LogWoke(waiting.Id, waiting.Step, next!, occurrence.Id);
        }
        else
        {
            LogWakeSkipped(waiting.Id, waiting.Step, occurrence.Id, skipped);
        }

        return true;
    }

    /// <summary>
    /// Reloads the instance and, while it still waits in the listed step at the listed revision, evaluates the
    /// step's <c>when</c> on the instance's release, records the match and resumes the instance at the
    /// step's <c>next</c>. Returns the reason the wake was skipped, or the step it resumed at. A skip may leave writes
    /// in the transaction, which the caller rolls back.
    /// </summary>
    private async Task<(string? Skipped, string? Next)> ResumeAsync(
        WorkItemContext context,
        StoredEvent occurrence,
        WaitingInstance waiting,
        CancellationToken cancellationToken)
    {
        var instance = await ProcessSteps.LoadAsync(context.Transaction, waiting.Id, cancellationToken);
        if (instance is not { State: ProcessStarts.Waiting } || instance.Step != waiting.Step || instance.Revision != waiting.Revision)
        {
            return ("the instance no longer waits in the step", null);
        }

        var model = await models.GetAsync(context, instance.ReleaseId, cancellationToken);
        if (model.Processes.FirstOrDefault(candidate => candidate.Id == instance.ProcessId) is not { } process
            || !process.TryGetStep(instance.Step, out var step)
            || step is not WaitEventStepModel waitStep)
        {
            return ("the step is not a waitEvent step", null);
        }

        if (waitStep.When is { } when)
        {
            var awaited = model.Events.FirstOrDefault(candidate => string.Equals(candidate.Name, waitStep.Event, StringComparison.OrdinalIgnoreCase));
            if (awaited is null)
            {
                return ("the release has no such event", null);
            }

            if (await EventTriggerStarts.EvaluateWhenAsync(context.Connection, model.TimeZone, awaited, occurrence, when, cancellationToken) is { } notTrue)
            {
                return (notTrue, null);
            }
        }

        if (!await ProcessEvents.TryInsertMatchAsync(context.Transaction, instance.Id, occurrence.Id, instance.Step, cancellationToken))
        {
            return ("the instance already used the occurrence", null);
        }

        if (!await ProcessSteps.TryResumeCallAsync(context.Transaction, instance.Id, instance.Step, instance.Revision, waitStep.Next, cancellationToken))
        {
            return ("the instance moved on", null);
        }

        await ProcessSteps.InsertHistoryAsync(
            context.Transaction,
            new StepOccurrence(
                instance.Id,
                instance.Step,
                instance.Revision,
                new JsonObject { ["subjectId"] = instance.SubjectId, ["subjectVersion"] = null },
                new JsonObject { ["next"] = waitStep.Next, ["eventId"] = occurrence.Id },
                null,
                null,
                instance.Now),
            cancellationToken);
        await AuditRecords.AppendAsync(
            context.Transaction,
            new AuditEntry(
                AuditActors.System,
                AuditActions.ProcessStepCompleted,
                instance.ApplicationId,
                instance.SubjectEntityId,
                instance.SubjectId,
                instance.Id,
                new JsonObject { ["step"] = instance.Step, ["next"] = waitStep.Next }),
            cancellationToken);
        await WorkItemQueue.EnqueueAsync(
            context.Connection,
            context.Transaction,
            context.Item.TenantId,
            ProcessStarts.StepWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            processInstanceId: instance.Id);
        return (null, waitStep.Next);
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

    [LoggerMessage(LogLevel.Information, "Event {EventId} woke process instance {InstanceId} waiting in step {Step}, which went on to step {Next}.")]
    private partial void LogWoke(Guid instanceId, string step, string next, Guid eventId);

    [LoggerMessage(LogLevel.Information, "Event {EventId} did not wake process instance {InstanceId} waiting in step {Step}: {Reason}.")]
    private partial void LogWakeSkipped(Guid instanceId, string step, Guid eventId, string reason);

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

/// <summary>An instance <c>waiting</c> in a <c>waitEvent</c> step for an event, with the step and revision it waits at.</summary>
internal sealed record WaitingInstance(Guid Id, Guid ProcessId, Guid ReleaseId, string Step, long Revision);
