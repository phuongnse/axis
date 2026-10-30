using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// The checks that a <c>raiseEvent</c> step and an inbound endpoint's <c>event</c> share: the event
/// is loaded, it is about the raiser's entity, each <c>fields</c> key names a payload field once,
/// each expression fits the field's type, and every payload field has a key. Only the scope the
/// expressions see and the wording about the raiser differ.
/// </summary>
internal static class RaiseEventChecks
{
    /// <summary>
    /// Checks the event named <paramref name="eventName"/> and its payload <paramref name="fields"/>,
    /// reported at <paramref name="eventPath"/> and under <paramref name="fieldsPath"/>.
    /// <paramref name="raiser"/> is <c>step</c> or <c>endpoint</c>, and
    /// <paramref name="otherEntityRule"/> is the sentence that follows an event about another entity,
    /// with the raiser's entity as <c>{0}</c>. <paramref name="unknownReportedElsewhere"/> tells,
    /// for an unknown event name, that it needs no report here. Only when the event is found are its
    /// <c>fields</c> checked: each key names a payload field of the event, ignoring letter case,
    /// that no earlier key names, and each expression fits the field's type over
    /// <paramref name="scope"/>. Every payload field then has a key. The expressions and the event's
    /// entity are not checked when the raiser's entity is unknown, and the event's entity is not
    /// checked when the event's own entity is unknown. When <paramref name="webhookEvents"/> holds
    /// the event, <paramref name="sensitiveRead"/> must find no sensitive read in any expression.
    /// </summary>
    public static void Check(
        Resource owner,
        string eventName,
        IReadOnlyDictionary<string, string> fields,
        string eventPath,
        string fieldsPath,
        string relation,
        string raiser,
        string otherEntityRule,
        EntityResource? entity,
        ExpressionScope? scope,
        Func<string, EntityResource?> findEntity,
        Func<string, EventResource?> findEvent,
        Func<string, bool> unknownReportedElsewhere,
        IReadOnlySet<string>? webhookEvents,
        Func<ExpressionNode, ExpressionCheckResult, string?> sensitiveRead,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        if (findEvent(eventName) is not { } @event)
        {
            // An event file that was not loaded because of its own errors is not reported again.
            if (!unknownReportedElsewhere(eventName))
            {
                report(
                    DiagnosticCodes.UnknownStepEvent,
                    $"The event '{eventName}' was not found. No loaded event has that name.",
                    eventPath);
            }

            return;
        }

        references.Add(owner, eventPath, relation, ResourceNode.Of(@event));

        if (entity is not null && findEntity(@event.Entity) is { } eventEntity && !ReferenceEquals(eventEntity, entity))
        {
            report(
                DiagnosticCodes.StepEventOverOtherEntity,
                $"The event '{@event.Name}' is about the entity '{eventEntity.Name}'. {string.Format(otherEntityRule, entity.Name)}",
                eventPath);
        }

        // The key that first named each payload field, by the field's declared name.
        var namedBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, expression) in fields)
        {
            var keyPath = $"{fieldsPath}/{key}";
            var field = @event.Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                report(
                    DiagnosticCodes.UnknownEventPayloadField,
                    $"The field '{key}' was not found. The event '{@event.Name}' has no payload field with that name.",
                    keyPath);
                continue;
            }

            if (!namedBy.TryAdd(field.Name, key))
            {
                report(
                    DiagnosticCodes.UnknownEventPayloadField,
                    $"The field '{field.Name}' is already given by '{namedBy[field.Name]}' in this {raiser}.",
                    keyPath);
                continue;
            }

            // The expression cannot be checked without the entity, nor against a field type that
            // the event check already reports.
            if (scope is null || !EventChecker.IsScalar(FieldTypes.Parse(field.Type)))
            {
                continue;
            }

            var problem = ProcessChecker.CheckExpression(owner, expression, scope, ExpressionScopes.TypeOf(field.Type), keyPath, references, out var node, out var check);
            if (problem is not null)
            {
                report(problem.Code, problem.Message, keyPath);
            }
            else if (entity is not null
                && webhookEvents?.Contains(@event.Name) == true
                && sensitiveRead(node!, check!) is { } read)
            {
                report(
                    DiagnosticCodes.SensitiveWebhookEventValue,
                    $"The field '{key}' reads {read}. The event '{@event.Name}' has a webhook, so it never carries a sensitive value.",
                    keyPath);
            }
        }

        foreach (var field in @event.Fields)
        {
            if (!namedBy.ContainsKey(field.Name))
            {
                report(
                    DiagnosticCodes.MissingEventPayloadField,
                    $"The field '{field.Name}' is a payload field of the event '{@event.Name}', but the {raiser} does not give it.",
                    fieldsPath);
            }
        }
    }
}
