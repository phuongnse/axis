using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Raises an event occurrence in a transaction the caller owns. A <c>raiseEvent</c> step of the worker
/// and an inbound endpoint call with an <c>event</c> both use it, so both raise events the same way:
/// the occurrence, the <c>sync</c> trigger starts, the <c>event.deliver</c> work item and the staged
/// webhook deliveries are written together, or not at all.
/// </summary>
public static partial class EventRaiser
{
    /// <summary>
    /// Writes the occurrence of <paramref name="event"/> about <paramref name="subjectId"/>, raised under
    /// <paramref name="releaseId"/>, and starts the process of each <c>sync</c> trigger on the event in
    /// <paramref name="active"/>, in model order, as <c>system</c>. It then queues <c>event.deliver</c>, which
    /// starts the <c>async</c> triggers later, and stages a delivery for each webhook of <paramref name="model"/>.
    /// A skipped trigger start is logged and lets the transaction commit. Any other error throws, so the
    /// caller rolls back with the event. <paramref name="processInstanceId"/> is the instance that raises the
    /// event, or <see langword="null"/> for an inbound endpoint that starts no process. <paramref name="active"/> is the active release of the application,
    /// or <see langword="null"/> when it has none, in which case no <c>sync</c> trigger starts.
    /// </summary>
    public static async Task RaiseAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel model,
        Guid releaseId,
        EventModel @event,
        Guid occurrenceId,
        Guid subjectId,
        JsonObject payload,
        Guid? processInstanceId,
        (Guid ReleaseId, ApplicationModel Model)? active,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(logger);
        var connection = transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
        var applicationId = model.Manifest.Id;
        var occurrence = new NewEvent(occurrenceId, applicationId, @event.Name, releaseId, @event.Entity.Id, subjectId, payload, processInstanceId);
        await ProcessEvents.InsertAsync(transaction, occurrence, cancellationToken);

        if (active is { } current
            && EventTriggerStarts.Find(current.ReleaseId, current.Model, @event.Name, TriggerMode.Sync) is { Event: { } declared, Triggers: { Count: > 0 } triggers })
        {
            var stored = new StoredEvent(occurrenceId, applicationId, @event.Name, @event.Entity.Id, subjectId, payload);
            foreach (var trigger in triggers)
            {
                var (_, skipped) = await EventTriggerStarts.StartAsync(
                    transaction, tenantId, current.Model, current.ReleaseId, declared, stored, trigger, cancellationToken);
                if (skipped is not null)
                {
                    LogSyncSkipped(logger, trigger.Name, trigger.Process.Name, subjectId, occurrenceId, skipped);
                }
            }
        }

        // The occurrence is delivered even when no async trigger listens now, because the
        // triggers come from the release that is active at delivery.
        await WorkItemQueue.EnqueueAsync(
            connection,
            transaction,
            tenantId,
            ProcessStarts.EventDeliverWorkItemKind,
            DateTimeOffset.UtcNow,
            cancellationToken,
            eventId: occurrenceId);

        // The webhooks are those of the release that raised the event, which compiled the raise and checked
        // that no sensitive value reaches a payload. A transaction that rolls back stages none.
        await WebhookDeliveries.StageAsync(transaction, tenantId, model, @event.Id, occurrenceId, applicationId, releaseId, cancellationToken);
    }

    /// <summary>
    /// The payload of an occurrence from the values of its fields, in the record API's JSON form. A field that
    /// gives null is stored as null.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value has no JSON form.</exception>
    public static JsonObject Payload(IEnumerable<KeyValuePair<string, object?>> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var payload = new JsonObject();
        foreach (var (name, value) in fields)
        {
            payload[name] = ProcessNotifications.TryToJson(value, out var json)
                ? json
                : throw new InvalidOperationException($"The payload field '{name}' gave a value of type '{value!.GetType().Name}'.");
        }

        return payload;
    }

    [LoggerMessage(LogLevel.Information, "Trigger {Trigger} skipped the start of process {Process} for record {SubjectId} of event {EventId}: {Reason}.")]
    private static partial void LogSyncSkipped(ILogger logger, string trigger, string process, Guid subjectId, Guid eventId, string reason);
}
