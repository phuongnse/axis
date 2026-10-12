namespace Axis.Configuration.Resources;

/// <summary>A payload field of an event as written: its name and its field type.</summary>
public sealed record EventFieldDefinition
{
    public required string Name { get; init; }

    public required string Type { get; init; }
}
