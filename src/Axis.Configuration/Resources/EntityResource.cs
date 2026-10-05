namespace Axis.Configuration.Resources;

/// <summary>An <c>entity</c> resource: a business record type and its fields.</summary>
public sealed record EntityResource : Resource
{
    public required IReadOnlyList<FieldDefinition> Fields { get; init; }
}
