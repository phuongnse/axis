namespace Axis.Configuration.Resources;

/// <summary>
/// A parameter as written in the data source file: a typed input of the filter. Its name and type
/// properties are checked by <see cref="Compilation.ApplicationCompiler"/>, by the entity field rules.
/// </summary>
public sealed record DataSourceParameterDefinition
{
    public required string Name { get; init; }

    public required string Type { get; init; }

    public bool? Required { get; init; }

    public TextReference? Label { get; init; }

    public IReadOnlyList<string>? Values { get; init; }

    public string? Target { get; init; }
}
