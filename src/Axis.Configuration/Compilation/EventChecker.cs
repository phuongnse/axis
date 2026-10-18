using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the events of an application and builds the checked ones. An event names a loaded root
/// entity. Its field names are unique, ignoring letter case, and each field type is scalar: text,
/// integer, decimal, boolean, date or date-time. The schema already refuses a type name that is not
/// a field type. Every problem of an event is reported in one pass.
/// </summary>
internal static class EventChecker
{
    /// <summary>
    /// Checks <paramref name="events"/>, which come in path order. <paramref name="ownerOf"/> names
    /// the owner of a child entity, or gives null for a root entity.
    /// </summary>
    public static void Check(
        IReadOnlyList<EventResource> events,
        Func<string, EntityResource?> findEntity,
        Func<string, string?> ownerOf,
        IReadOnlySet<string> unloadedEntityNames,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        foreach (var @event in events)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, @event.File, path, @event.Id));

            var entity = findEntity(@event.Entity);
            if (entity is null)
            {
                // An entity file that was not loaded because of its own errors is not reported again.
                if (!unloadedEntityNames.Contains(@event.Entity))
                {
                    Report(
                        DiagnosticCodes.UnknownEventEntity,
                        $"The entity '{@event.Entity}' was not found. No loaded entity has that name.",
                        "/entity");
                }
            }
            else
            {
                references.Add(@event, "/entity", ReferenceRelations.EventEntity, ResourceNode.Of(entity));
            }

            if (entity is not null && ownerOf(entity.Name) is { } owner)
            {
                Report(
                    DiagnosticCodes.EventOverChildEntity,
                    $"The entity '{entity.Name}' is a child entity owned by '{owner}'. An event is about a record of a root entity.",
                    "/entity");
            }

            var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < @event.Fields.Count; index++)
            {
                var field = @event.Fields[index];
                if (!firstIndexByName.TryAdd(field.Name, index))
                {
                    Report(
                        DiagnosticCodes.DuplicateEventFieldName,
                        $"The field name '{field.Name}' is already used by field '{@event.Fields[firstIndexByName[field.Name]].Name}' of this event.",
                        $"/fields/{index}/name");
                }

                if (!IsScalar(FieldTypes.Parse(field.Type)))
                {
                    Report(
                        DiagnosticCodes.InvalidEventFieldType,
                        $"The field type '{field.Type}' is not allowed. An event field is text, integer, decimal, boolean, date or date-time.",
                        $"/fields/{index}/type");
                }
            }
        }
    }

    /// <summary>Builds a checked event. Its entity is resolved to the loaded root entity.</summary>
    public static EventModel Build(EventResource @event, Func<string, EntityResource?> findEntity)
    {
        var entity = findEntity(@event.Entity)
            ?? throw new ArgumentException("Only a checked event can be built.", nameof(@event));
        return new EventModel
        {
            Id = @event.Id,
            Name = @event.Name,
            File = @event.File,
            Entity = new EntityReference(entity.Id, entity.Name),
            Fields = @event.Fields.Select(field => new EventFieldModel(field.Name, FieldTypes.Parse(field.Type))).ToList(),
        };
    }

    /// <summary>Whether <paramref name="type"/> is a type an event field may have.</summary>
    internal static bool IsScalar(FieldType type) =>
        type is FieldType.Text or FieldType.Integer or FieldType.Decimal or FieldType.Boolean or FieldType.Date or FieldType.DateTime;
}
