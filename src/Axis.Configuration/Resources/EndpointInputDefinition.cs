namespace Axis.Configuration.Resources;

/// <summary>
/// An input field of an endpoint as written: its name, its field type and the constraints of that
/// type. The schema refuses the field properties that mean nothing for a request body.
/// </summary>
public sealed record EndpointInputDefinition
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool? Required { get; init; }

    public int? MaxLength { get; init; }

    public int? Precision { get; init; }

    public int? Scale { get; init; }

    public IReadOnlyList<string>? Values { get; init; }

    public string? Target { get; init; }
}
