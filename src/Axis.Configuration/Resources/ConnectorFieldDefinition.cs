namespace Axis.Configuration.Resources;

/// <summary>
/// An input or output field of a connector operation as written: its name, scalar field type and
/// whether it is required. An input also says where a call sends it, in <see cref="In"/>.
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
}
