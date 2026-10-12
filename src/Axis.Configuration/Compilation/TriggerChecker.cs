using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the triggers of an application and builds the checked ones. For now the schema accepts
/// only an event trigger in async mode. A trigger names a loaded process and a loaded event, and
/// the event is about the process's subject entity. Its <c>when</c> is a boolean that sees only
/// the event's payload fields. Every problem of a trigger is reported in one pass. A process or
/// event whose file did not load is not reported again, the entity match is not checked when
/// either entity is unknown, and <c>when</c> is not checked without the event.
/// </summary>
internal static class TriggerChecker
{
    /// <summary>Checks <paramref name="triggers"/>, which come in path order.</summary>
    public static void Check(
        IReadOnlyList<TriggerResource> triggers,
        Func<string, EntityResource?> findEntity,
        Func<string, ProcessResource?> findProcess,
        IReadOnlySet<string> unloadedProcessNames,
        Func<string, EventResource?> findEvent,
        IReadOnlySet<string> unloadedEventNames,
        List<Diagnostic> diagnostics)
    {
        foreach (var trigger in triggers)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, trigger.File, path, trigger.Id));

            var process = findProcess(trigger.Process);
            if (process is null && !unloadedProcessNames.Contains(trigger.Process))
            {
                // A process file that was not loaded because of its own errors is not reported again.
                Report(
                    DiagnosticCodes.UnknownTriggerProcess,
                    $"The process '{trigger.Process}' was not found. No loaded process has that name.",
                    "/process");
            }

            // The schema requires an event for an event trigger, the only type for now.
            var eventName = trigger.Event!;
            var @event = findEvent(eventName);
            if (@event is null)
            {
                if (!unloadedEventNames.Contains(eventName))
                {
                    Report(
                        DiagnosticCodes.UnknownTriggerEvent,
                        $"The event '{eventName}' was not found. No loaded event has that name.",
                        "/event");
                }

                // The condition cannot be checked without the event's payload.
                continue;
            }

            // An unknown entity has a diagnostic of its own on the event or the process.
            if (process is not null
                && findEntity(@event.Entity) is { } eventEntity
                && findEntity(process.Entity) is { } subject
                && !ReferenceEquals(eventEntity, subject))
            {
                Report(
                    DiagnosticCodes.TriggerEventOverOtherEntity,
                    $"The event '{@event.Name}' is about the entity '{eventEntity.Name}', but the process '{process.Name}' is over '{subject.Name}'. A trigger starts the process for the record the event names.",
                    "/event");
            }

            if (trigger.When is { } when)
            {
                var parsed = ExpressionParser.Parse(when);
                var problem = parsed.Succeeded
                    ? ExpressionTypeChecker.Check(parsed.Expression!, ExpressionScopes.ForEventPayload(@event.Fields), ExpressionType.Boolean).Diagnostic
                    : parsed.Diagnostic;
                if (problem is not null)
                {
                    Report(problem.Code, problem.Message, "/when");
                }
            }
        }
    }

    /// <summary>
    /// Builds a checked trigger. Its event and process are resolved and named as they declare
    /// themselves, and its <c>when</c> is compiled over the event's payload fields.
    /// </summary>
    public static TriggerModel Build(
        TriggerResource trigger, Func<string, EventResource?> findEvent, Func<string, ProcessResource?> findProcess)
    {
        var @event = findEvent(trigger.Event!)
            ?? throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
        var process = findProcess(trigger.Process)
            ?? throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
        return new TriggerModel
        {
            Id = trigger.Id,
            Name = trigger.Name,
            File = trigger.File,

            // The schema accepts only an event trigger in async mode for now.
            Type = TriggerType.Event,
            Mode = TriggerMode.Async,
            Event = new EventReference(@event.Id, @event.Name),
            Process = new ProcessReference(process.Id, process.Name),
            When = trigger.When is { } when
                ? ExpressionModel.Compile(when, ExpressionScopes.ForEventPayload(@event.Fields), ExpressionType.Boolean)
                : null,
        };
    }
}
