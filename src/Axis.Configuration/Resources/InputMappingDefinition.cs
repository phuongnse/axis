namespace Axis.Configuration.Resources;

/// <summary>
/// The value of one input of a <c>callConnector</c> step: an <see cref="Expression"/>, or for an
/// <c>object</c> or <c>list</c> input a nested mapping of <see cref="Fields"/>. Exactly one of
/// <see cref="Expression"/> and <see cref="Fields"/> is set. Whether the shape fits the input is
/// checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record InputMappingDefinition
{
    /// <summary>The expression over the subject record that gives the input, or null for a nested mapping.</summary>
    public string? Expression { get; init; }

    /// <summary>The child collection of the subject a <c>list</c> input maps from, or null.</summary>
    public string? From { get; init; }

    /// <summary>The nested inputs of an <c>object</c> or <c>list</c> input, each to a mapping, in file order, or null for an expression.</summary>
    public IReadOnlyDictionary<string, InputMappingDefinition>? Fields { get; init; }
}
