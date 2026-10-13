using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Expressions.Evaluation;
using Axis.Processes.Work;
using Microsoft.EntityFrameworkCore;
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
/// occurrence, so a delivery run again after a crash starts nothing twice. The transaction then
/// evaluates the trigger's <c>when</c> on the stored payload, locks and reads the occurrence's
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
        Delivery? delivery = null;
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
            if (!await DeliverAsync(item, connection, delivery, occurrence!, trigger, cancellationToken))
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
    private async Task<Delivery?> FindDeliveryAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StoredEvent occurrence,
        CancellationToken cancellationToken)
    {
        ActiveRelease? active;
        await using (var configuration = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseNpgsql(connection)
            .Options))
        {
            await configuration.Database.UseTransactionAsync(transaction, cancellationToken);
            active = await new ActiveReleaseStore(configuration).FindByApplicationIdAsync(occurrence.ApplicationId, cancellationToken);
        }

        if (active is null)
        {
            LogNoActiveRelease(occurrence.Id, occurrence.ApplicationId);
            return null;
        }

        var model = await models.GetAsync(new WorkItemContext(item, connection, transaction), active.ReleaseId, cancellationToken);
        List<TriggerModel> triggers =
        [
            .. model.Triggers.Where(trigger =>
                trigger is { Type: TriggerType.Event, Mode: TriggerMode.Async, Event: { } @event }
                && string.Equals(@event.Name, occurrence.Event, StringComparison.OrdinalIgnoreCase)),
        ];
        var declared = model.Events.FirstOrDefault(candidate => string.Equals(candidate.Name, occurrence.Event, StringComparison.OrdinalIgnoreCase));
        return triggers.Count == 0 || declared is null ? null : new Delivery(active.ReleaseId, model, declared, triggers);
    }

    /// <summary>
    /// Delivers the occurrence to one trigger in its own transaction. Returns <see langword="false"/>
    /// when another worker holds the item now, so nothing was written.
    /// </summary>
    private async Task<bool> DeliverAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        Delivery delivery,
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

            skipped = await StartAsync(item, transaction, delivery, occurrence, trigger, cancellationToken);
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
        Delivery delivery,
        StoredEvent occurrence,
        TriggerModel trigger,
        CancellationToken cancellationToken)
    {
        var connection = transaction.Connection!;
        var source = occurrence.Id.ToString("D");

        // The receipt goes first, so a concurrent or repeated delivery waits on its key and then skips.
        if (!await TriggerStarts.TryInsertAsync(transaction, trigger.Id, source, occurrence.SubjectId, cancellationToken))
        {
            return "the trigger already handled the occurrence";
        }

        if (trigger.When is { } when && await EvaluateWhenAsync(connection, delivery.Event, occurrence, when, cancellationToken) is { } notTrue)
        {
            return notTrue;
        }

        var model = delivery.Model;
        var process = model.Processes.FirstOrDefault(candidate => candidate.Id == trigger.Process.Id)
            ?? throw new InvalidOperationException($"The release has no process '{trigger.Process.Name}'.");
        var entity = model.FindEntity(process.Entity.Id)
            ?? throw new InvalidOperationException($"The release has no entity '{process.Entity.Name}'.");

        // The lock makes the read below and the start condition see the latest version, and a
        // concurrent write of the record waits until this start commits.
        if (!await RecordCommands.LockAsync(connection, entity, occurrence.SubjectId, cancellationToken)
            || await RecordQueries.GetAsync(connection, model, entity, occurrence.SubjectId, cancellationToken) is not { } record)
        {
            return "the record no longer exists";
        }

        var instanceId = Guid.CreateVersion7();
        var started = await ProcessStarter.StartAsync(
            connection,
            transaction,
            item.TenantId,
            model,
            delivery.ReleaseId,
            process,
            entity,
            occurrence.SubjectId,
            record,
            instanceId,
            AuditActors.System,
            actorName: null,
            new JsonObject { ["trigger"] = trigger.Name, ["eventId"] = occurrence.Id },
            cancellationToken);
        switch (started.Outcome)
        {
            case ProcessStartOutcome.Started:
                await TriggerStarts.MarkStartedAsync(transaction, trigger.Id, source, occurrence.SubjectId, instanceId, cancellationToken);
                return null;
            case ProcessStartOutcome.AlreadyRunning:
                return "the process already runs for the record";
            default:
                return started.Error is { } error ? $"the start condition failed: {Clause(error)}" : "the start condition is not true";
        }
    }

    /// <summary>
    /// Evaluates the trigger's <c>when</c> on the stored payload, read with the field types of the
    /// active release's event. Returns why it is not true, or <see langword="null"/> when it is. A
    /// payload value that does not fit its type counts as a run-time error.
    /// </summary>
    private static async Task<string?> EvaluateWhenAsync(
        NpgsqlConnection connection,
        EventModel @event,
        StoredEvent occurrence,
        ExpressionModel when,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in @event.Fields)
        {
            // The payload was written under the release that raised the event, so a field it lacks reads as null.
            var node = occurrence.Payload.FirstOrDefault(entry => string.Equals(entry.Key, field.Name, StringComparison.OrdinalIgnoreCase)).Value;
            bool exact;
            object? value;
            try
            {
                exact = RecordClrValues.TryFromStored(field.Type, node, out value);
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentOutOfRangeException)
            {
                return $"the when failed: the payload field '{field.Name}' does not hold a value of its type";
            }

            if (!exact)
            {
                return $"the when failed: the payload field '{field.Name}' holds a decimal that cannot be read without rounding";
            }

            values[field.Name] = value;
        }

        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var result = ExpressionInterpreter.Evaluate(when.Syntax, when.Check, new ExpressionValues(values), null, now);
        if (result.Error is { } error)
        {
            return $"the when failed: {Clause(error.Message)}";
        }

        return result.Value is true ? null : "the when is not true";
    }

    /// <summary>An error message without its closing period, to fit inside the log line.</summary>
    private static string Clause(string message) => message.TrimEnd('.');

    [LoggerMessage(LogLevel.Information, "Trigger {Trigger} skipped the start of process {Process} for record {SubjectId} of event {EventId}: {Reason}.")]
    private partial void LogSkipped(string trigger, string process, Guid subjectId, Guid eventId, string reason);

    [LoggerMessage(LogLevel.Warning, "Event delivery work item {ItemId} names no stored event {EventId}, so it was discarded.")]
    private partial void LogNoEvent(Guid itemId, Guid? eventId);

    [LoggerMessage(LogLevel.Information, "Event {EventId} was delivered to no trigger: application {ApplicationId} has no active release.")]
    private partial void LogNoActiveRelease(Guid eventId, Guid applicationId);

    [LoggerMessage(LogLevel.Warning, "Event delivery work item {ItemId} for event {EventId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid? eventId);

    /// <summary>What an occurrence is delivered with: the active release, its model, its declaration of the event and the triggers on it.</summary>
    private sealed record Delivery(Guid ReleaseId, ApplicationModel Model, EventModel Event, IReadOnlyList<TriggerModel> Triggers);
}
