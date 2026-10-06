namespace Axis.Configuration.Resources;

/// <summary>An <c>entity</c> resource: a business record type and its fields.</summary>
public sealed record EntityResource : Resource
{
    public required IReadOnlyList<FieldDefinition> Fields { get; init; }

    /// <summary>
    /// The name of the required <c>text</c> field that gives a record its name. Whether it names
    /// such a field is checked by <see cref="Compilation.ApplicationCompiler"/>.
    /// </summary>
    public string? DisplayField { get; init; }
}
