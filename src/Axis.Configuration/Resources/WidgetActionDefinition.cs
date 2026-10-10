namespace Axis.Configuration.Resources;

/// <summary>
/// An action of a <c>form</c> widget as written in the page file: a button that starts the named
/// process for the form's record. Whether the process exists and is over the form's entity is
/// checked by <see cref="Compilation.ApplicationCompiler"/>, not by the loader.
/// </summary>
public sealed record WidgetActionDefinition
{
    /// <summary>The kind of action. Only <c>startProcess</c> exists.</summary>
    public required string Type { get; init; }

    /// <summary>The process the action starts for the form's record.</summary>
    public required string Process { get; init; }

    /// <summary>The text key of the button's label.</summary>
    public required TextReference Label { get; init; }
}
