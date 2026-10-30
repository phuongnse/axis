namespace Axis.Configuration.Resources;

/// <summary>
/// An <c>endpoint</c> resource: an inbound call that creates a record of an entity from typed
/// input fields and may start a process on it, and may name an event about that record with the
/// payload that <see cref="Event"/> gives. The schema checks its shape, that each input field
/// has only the properties an input takes, that it names a <see cref="Process"/>, an
/// <see cref="Event"/> or both, and that <see cref="Output"/> and <see cref="Wait"/> come
/// together and need a process. Its entity, process, event, input fields, mapping, output and wait are checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record EndpointResource : Resource
{
    /// <summary>The name of the root entity of the record the call creates.</summary>
    public required string Entity { get; init; }

    /// <summary>The name of the process the call starts on the new record, or null when it starts none.</summary>
    public string? Process { get; init; }

    /// <summary>The event the call is to raise about the new record, or null when it names none.</summary>
    public EndpointEventDefinition? Event { get; init; }

    /// <summary>The typed input fields of the call's body, in declaration order. The list may be empty.</summary>
    public required IReadOnlyList<EndpointInputDefinition> Input { get; init; }

    /// <summary>
    /// The mapping from input to the entity's fields: each key names a field, and each value is an
    /// expression over the input, or for a child collection the name of a child-collection input.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Values { get; init; }

    /// <summary>The field names a completed call returns, or null when the call does not wait.</summary>
    public IReadOnlyList<string>? Output { get; init; }

    /// <summary>The ISO 8601 duration the call waits for the instance to complete, or null.</summary>
    public string? Wait { get; init; }
}
