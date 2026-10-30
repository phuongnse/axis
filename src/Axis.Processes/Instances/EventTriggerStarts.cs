using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.Records;
using Axis.Expressions.Evaluation;
using Axis.Processes.Work;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Starts the processes of event triggers for an event occurrence, in a transaction the caller owns.
/// Event delivery uses it for <c>async</c> triggers, and a <c>raiseEvent</c> step uses it for
/// <c>sync</c> triggers. The triggers and the process come from the application's active release,
/// and each start runs as <c>system</c>, pinned to that release.
/// </summary>
internal static class EventTriggerStarts
{
    /// <summary>
    /// Finds the active release of the application and its triggers of <paramref name="mode"/> on the
    /// event, ignoring letter case. Returns <see langword="null"/> when there is no active release. The
    /// triggers are empty when the release does not declare the event or no such trigger listens.
    /// </summary>
    internal static async Task<EventTriggers?> FindAsync(
        WorkItemContext context,
        ReleaseModelCache models,
        Guid applicationId,
        string eventName,
        TriggerMode mode,
        CancellationToken cancellationToken)
    {
        if (await models.FindActiveAsync(context, applicationId, cancellationToken) is not { } active)
        {
            return null;
        }

        return Find(active.ReleaseId, active.Model, eventName, mode);
    }

    /// <summary>
    /// Finds the triggers of <paramref name="mode"/> on the event in <paramref name="model"/>, ignoring letter
    /// case. The triggers are empty when the release does not declare the event or no such trigger listens.
    /// </summary>
    internal static EventTriggers Find(Guid releaseId, ApplicationModel model, string eventName, TriggerMode mode)
    {
        var declared = model.Events.FirstOrDefault(candidate => string.Equals(candidate.Name, eventName, StringComparison.OrdinalIgnoreCase));
        List<TriggerModel> triggers = declared is null
            ? []
            :
            [
                .. model.Triggers.Where(trigger =>
                    trigger.Type == TriggerType.Event
                    && trigger.Mode == mode
                    && trigger.Event is { } @event
                    && string.Equals(@event.Name, eventName, StringComparison.OrdinalIgnoreCase)),
            ];
        return new EventTriggers(releaseId, model, declared, triggers);
    }

    /// <summary>
    /// Evaluates the trigger's <c>when</c> on the occurrence's payload, then locks and reads the
    /// occurrence's record and starts the trigger's process for it as <c>system</c>. Its
    /// <c>process.started</c> audit record names the trigger and the event. Returns the new
    /// instance, or the reason the start was skipped. Any other error throws.
    /// </summary>
    internal static async Task<(Guid? InstanceId, string? Skipped)> StartAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel model,
        Guid releaseId,
        EventModel @event,
        StoredEvent occurrence,
        TriggerModel trigger,
        CancellationToken cancellationToken)
    {
        var connection = transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction));
        if (trigger.When is { } when && await EvaluateWhenAsync(connection, model.TimeZone, @event, occurrence, when, cancellationToken) is { } notTrue)
        {
            return (null, notTrue);
        }

        var process = model.Processes.FirstOrDefault(candidate => candidate.Id == trigger.Process.Id)
            ?? throw new InvalidOperationException($"The release has no process '{trigger.Process.Name}'.");
        // The compiler refuses a trigger that names a process without a subject.
        var subjectReference = process.Entity
            ?? throw new InvalidOperationException($"The process '{process.Name}' has no subject, so a trigger cannot start it.");
        var entity = model.FindEntity(subjectReference.Id)
            ?? throw new InvalidOperationException($"The release has no entity '{subjectReference.Name}'.");

        // The lock makes the read below and the start condition see the latest version, and a
        // concurrent write of the record waits until this start commits.
        if (!await RecordCommands.LockAsync(connection, entity, occurrence.SubjectId, cancellationToken)
            || await RecordQueries.GetAsync(connection, model, entity, occurrence.SubjectId, cancellationToken) is not { } record)
        {
            return (null, "the record no longer exists");
        }

        // The start condition reads the record with its pending computed fields computed by this release.
        if (process.StartCondition is not null)
        {
            record = await PendingComputedReads.ApplyAsync(connection, transaction, model, entity, record, cancellationToken);
        }

        var instanceId = Guid.CreateVersion7();
        var started = await ProcessStarter.StartAsync(
            connection,
            transaction,
            tenantId,
            model,
            releaseId,
            process,
            entity,
            occurrence.SubjectId,
            record,
            instanceId,
            AuditActors.System,
            actorName: null,
            new JsonObject { ["trigger"] = trigger.Name, ["eventId"] = occurrence.Id },
            cancellationToken);
        return started.Outcome switch
        {
            ProcessStartOutcome.Started => (instanceId, null),
            ProcessStartOutcome.AlreadyRunning => (null, "the process already runs for the record"),
            _ => (null, started.ErrorKind is { } kind ? $"the start condition failed: {kind}" : "the start condition is not true"),
        };
    }

    /// <summary>
    /// Evaluates the trigger's <c>when</c> on the stored payload, read with the field types of the
    /// active release's event. Returns why it is not true, or <see langword="null"/> when it is. A
    /// payload value that does not fit its type counts as a run-time error.
    /// </summary>
    internal static async Task<string?> EvaluateWhenAsync(
        NpgsqlConnection connection,
        TimeZoneInfo timeZone,
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
        var result = ExpressionInterpreter.Evaluate(when.Syntax, when.Check, new ExpressionValues(values), null, now, timeZone);
        if (result.Error is { } error)
        {
            return $"the when failed: {error.Kind}";
        }

        return result.Value is true ? null : "the when is not true";
    }
}

/// <summary>
/// The event triggers of one mode in the application's active release: the release, its model, its
/// declaration of the event, or null when it declares none, and the triggers on the event.
/// </summary>
internal sealed record EventTriggers(Guid ReleaseId, ApplicationModel Model, EventModel? Event, IReadOnlyList<TriggerModel> Triggers);
