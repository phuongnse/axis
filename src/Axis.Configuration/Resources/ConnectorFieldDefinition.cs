namespace Axis.Configuration.Resources;

/// <summary>An input or output field of a connector operation as written: its name, scalar field type and whether it is required.</summary>
public sealed record ConnectorFieldDefinition
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool Required { get; init; }
}
