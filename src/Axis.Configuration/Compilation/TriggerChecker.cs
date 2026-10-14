using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Parsing;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the triggers of an application and builds the checked ones. A trigger names a loaded
/// process. An event trigger, in async or sync mode, names a loaded event about the process's
/// subject entity, and its <c>when</c> is a boolean that sees only the event's payload fields. A data
/// change trigger names the process's subject entity, any <c>fields</c> are non-computed fields of it,
/// and its <c>when</c> is a boolean over the watched record with the scope of a process condition.
/// Every problem of a trigger is reported in one pass. A process, event or entity whose file did
/// not load is not reported again, the entity match is not checked when either entity is unknown,
/// and <c>when</c> is not checked without the event or the entity.
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
        IReadOnlySet<string> unloadedEntityNames,
        IEnumerable<ExpressionRule> rules,
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

            if (trigger.Type == "dataChange")
            {
                CheckDataChange(trigger, process, findEntity, unloadedEntityNames, rules, Report);
                continue;
            }

            // The schema requires an event for an event trigger.
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
    /// Checks a data change trigger. The schema requires its entity and <c>on</c>. An unknown
    /// entity, or one that is not the process's subject, stops the check of the trigger's fields
    /// and condition.
    /// </summary>
    private static void CheckDataChange(
        TriggerResource trigger,
        ProcessResource? process,
        Func<string, EntityResource?> findEntity,
        IReadOnlySet<string> unloadedEntityNames,
        IEnumerable<ExpressionRule> rules,
        Action<string, string, string> report)
    {
        var entityName = trigger.Entity!;
        var entity = findEntity(entityName);
        if (entity is null)
        {
            if (!unloadedEntityNames.Contains(entityName))
            {
                report(
                    DiagnosticCodes.UnknownTriggerEntity,
                    $"The entity '{entityName}' was not found. No loaded entity has that name.",
                    "/entity");
            }

            return;
        }

        // An unknown entity has a diagnostic of its own on the process.
        if (process is not null
            && findEntity(process.Entity) is { } subject
            && !ReferenceEquals(entity, subject))
        {
            report(
                DiagnosticCodes.TriggerEntityNotProcessSubject,
                $"The trigger watches the entity '{entity.Name}', but the process '{process.Name}' is over '{subject.Name}'. A trigger starts the process for the record that changed.",
                "/entity");
        }

        var fields = trigger.Fields ?? [];
        for (var index = 0; index < fields.Count; index++)
        {
            var field = FindField(entity, fields[index]);
            if (field is null)
            {
                report(
                    DiagnosticCodes.InvalidTriggerField,
                    $"The field '{fields[index]}' names no field of the entity '{entity.Name}'.",
                    $"/fields/{index}");
            }
            else if (field.Expression is not null)
            {
                report(
                    DiagnosticCodes.InvalidTriggerField,
                    $"The field '{field.Name}' is a computed field, which no write changes.",
                    $"/fields/{index}");
            }
        }

        if (trigger.When is { } when)
        {
            var parsed = ExpressionParser.Parse(when);
            var problem = parsed.Succeeded
                ? ExpressionTypeChecker.Check(parsed.Expression!, ExpressionScopes.ForProcess(entity.Fields, rules, findEntity), ExpressionType.Boolean).Diagnostic
                : parsed.Diagnostic;
            if (problem is not null)
            {
                report(problem.Code, problem.Message, "/when");
            }
        }
    }

    private static FieldDefinition? FindField(EntityResource entity, string name) =>
        entity.Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Builds a checked trigger. Its event or entity and process are resolved and named as they
    /// declare themselves. Its <c>when</c> is compiled over the event's payload fields, or for a
    /// data change trigger over the watched record with the scope of a process condition.
    /// </summary>
    public static TriggerModel Build(
        TriggerResource trigger,
        Func<string, EntityResource?> findEntity,
        Func<string, EventResource?> findEvent,
        Func<string, ProcessResource?> findProcess,
        IEnumerable<ExpressionRule> rules)
    {
        if (trigger.Type == "dataChange")
        {
            return BuildDataChange(trigger, findEntity, findProcess, rules);
        }

        var @event = findEvent(trigger.Event!)
            ?? throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
        var process = findProcess(trigger.Process)
            ?? throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
        return new TriggerModel
        {
            Id = trigger.Id,
            Name = trigger.Name,
            File = trigger.File,

            // A trigger without a mode is async.
            Type = TriggerType.Event,
            Mode = trigger.Mode == "sync" ? TriggerMode.Sync : TriggerMode.Async,
            Event = new EventReference(@event.Id, @event.Name),
            Process = new ProcessReference(process.Id, process.Name),
            When = trigger.When is { } when
                ? ExpressionModel.Compile(when, ExpressionScopes.ForEventPayload(@event.Fields), ExpressionType.Boolean)
                : null,
        };
    }

    private static TriggerModel BuildDataChange(
        TriggerResource trigger,
        Func<string, EntityResource?> findEntity,
        Func<string, ProcessResource?> findProcess,
        IEnumerable<ExpressionRule> rules)
    {
        var entity = findEntity(trigger.Entity!)
            ?? throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
        var process = findProcess(trigger.Process)
            ?? throw new ArgumentException("Only a checked trigger can be built.", nameof(trigger));
        var on = DataChangeKinds.None;
        foreach (var kind in trigger.On!)
        {
            on |= kind == "create" ? DataChangeKinds.Create : DataChangeKinds.Update;
        }

        return new TriggerModel
        {
            Id = trigger.Id,
            Name = trigger.Name,
            File = trigger.File,
            Type = TriggerType.DataChange,

            // The mode applies to event triggers only. A data change trigger is async.
            Mode = TriggerMode.Async,
            Entity = new EntityReference(entity.Id, entity.Name),
            On = on,
            Fields = trigger.Fields?.Select(name => FindField(entity, name)!.Name).ToList(),
            Process = new ProcessReference(process.Id, process.Name),
            When = trigger.When is { } when
                ? ExpressionModel.Compile(when, ExpressionScopes.ForProcess(entity.Fields, rules, findEntity), ExpressionType.Boolean)
                : null,
        };
    }
}
