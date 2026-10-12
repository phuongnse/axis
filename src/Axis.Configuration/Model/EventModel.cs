namespace Axis.Configuration.Model;

/// <summary>A compiled event: a business event about a record of a root entity, with typed payload fields.</summary>
public sealed record EventModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The event's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    /// <summary>The root entity the event is about.</summary>
    public required EntityReference Entity { get; init; }

    /// <summary>The payload fields, in declaration order.</summary>
    public required IReadOnlyList<EventFieldModel> Fields { get; init; }
}

/// <summary>A payload field of a compiled event, with a scalar field type.</summary>
public sealed record EventFieldModel(string Name, FieldType Type);
