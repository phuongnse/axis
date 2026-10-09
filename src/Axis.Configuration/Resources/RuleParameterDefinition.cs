namespace Axis.Configuration.Resources;

/// <summary>A rule parameter as written in the rule file: its <c>name</c> and scalar field <c>type</c>.</summary>
public sealed record RuleParameterDefinition
{
    public required string Name { get; init; }

    public required string Type { get; init; }
}
