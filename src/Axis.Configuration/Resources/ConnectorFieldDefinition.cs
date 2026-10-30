namespace Axis.Configuration.Resources;

/// <summary>An input or output field of a connector operation as written: its name, scalar field type and whether it is required.</summary>
public sealed record ConnectorFieldDefinition
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool Required { get; init; }

    /// <summary>For an output only: a JSON Pointer into the response body, or null to read the top-level property of <see cref="Name"/>.</summary>
    public string? Source { get; init; }
}
