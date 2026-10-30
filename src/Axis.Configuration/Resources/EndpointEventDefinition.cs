namespace Axis.Configuration.Resources;

/// <summary>
/// The event an endpoint names as written: the event's name, and the payload it carries. Each key
/// of <see cref="Fields"/> names a payload field of the event and each value is an expression over
/// the endpoint's input fields.
/// </summary>
public sealed record EndpointEventDefinition
{
    public required string Name { get; init; }

    public required IReadOnlyDictionary<string, string> Fields { get; init; }
}
