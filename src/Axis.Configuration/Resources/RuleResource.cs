namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>rule</c> resource: a named expression with typed parameters and a result type, which
/// validations, data source filters and process conditions call like a function. Its parameters, body and calls are checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record RuleResource : Resource
{
    /// <summary>The parameters, in call order.</summary>
    public required IReadOnlyList<RuleParameterDefinition> Parameters { get; init; }

    /// <summary>The scalar field type the expression must give.</summary>
    public required string ResultType { get; init; }

    /// <summary>The body, which sees only the parameters.</summary>
    public required string Expression { get; init; }
}
