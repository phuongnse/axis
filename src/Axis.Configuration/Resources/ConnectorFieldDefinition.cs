namespace Axis.Configuration.Resources;

/// <summary>
/// An input or output field of a connector operation as written: its name, field type and
/// whether it is required. An output has a scalar type. An input may also be an <c>object</c> or a <c>list</c>. An input also says where a call sends it, in <see cref="In"/>.
/// </summary>
public sealed record ConnectorFieldDefinition
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool Required { get; init; }

    /// <summary>
    /// Where a call sends an input: <c>body</c> (the default), <c>path</c>, <c>query</c> or
    /// <c>header</c>. An output has no <c>in</c>.
    /// </summary>
    public string In { get; init; } = "body";

    /// <summary>For an output only: a JSON Pointer into the response body, or null to read the top-level property of <see cref="Name"/>.</summary>
    public string? Source { get; init; }

    /// <summary>
    /// The nested fields of an <c>object</c> input, or of the objects in a <c>list</c> input, or null for
    /// any other field. A nested field takes no <c>in</c>.
    /// </summary>
    public IReadOnlyList<ConnectorFieldDefinition>? Fields { get; init; }
}
