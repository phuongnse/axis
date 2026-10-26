namespace Axis.Configuration.Resources;

/// <summary>
/// The page a <c>notify</c> step's notification opens, as written: the page's name and an expression
/// for each parameter it gives, in file order. Whether the page and the parameters exist is checked
/// by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record NotifyOpenDefinition
{
    public required string Page { get; init; }

    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}
