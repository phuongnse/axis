namespace Axis.Configuration.Resources;

/// <summary>A process parameter as written in the process file: its <c>name</c> and scalar field <c>type</c>.</summary>
public sealed record ProcessParameterDefinition
{
    public required string Name { get; init; }

    public required string Type { get; init; }
}
