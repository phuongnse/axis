namespace Axis.Configuration.Resources;

/// <summary>
/// An <c>event</c> resource: a business event about a record of a root entity, with typed payload
/// fields. The schema checks its shape and that each field type is a known type name. Its entity,
/// its field names and whether each field type is scalar are checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record EventResource : Resource
{
    /// <summary>The name of the root entity the event is about.</summary>
    public required string Entity { get; init; }

    /// <summary>The payload fields, in declaration order. The list may be empty.</summary>
    public required IReadOnlyList<EventFieldDefinition> Fields { get; init; }
}
