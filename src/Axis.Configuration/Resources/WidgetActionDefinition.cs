namespace Axis.Configuration.Resources;

/// <summary>
/// An action as written in the page file. A <c>startProcess</c> action, in a <c>form</c> widget's
/// <c>actions</c> or a table's <c>rowActions</c>, is a button that starts the named process for the
/// form's record or the row's record. A <c>navigate</c> action, in a form widget's <c>actions</c>, a
/// table's <c>rowActions</c> and <c>toolbarActions</c> or a kpi's <c>navigate</c>, opens the
/// named page and gives each of its parameters an expression. A <c>delete</c> action, in a table's
/// <c>rowActions</c>, is a button that deletes the row's record after a confirmation. Whether the
/// process or page exists, and whether the parameters fit, is checked by
/// <see cref="Compilation.ApplicationCompiler"/>, not by the loader.
/// </summary>
public sealed record WidgetActionDefinition
{
    /// <summary>The kind of action: <c>startProcess</c>, <c>navigate</c> or <c>delete</c>.</summary>
    public required string Type { get; init; }

    /// <summary>The name that a layer finds the action by. It is unique in its own list, ignoring letter case. The schema refuses it on a kpi's <c>navigate</c>.</summary>
    public string? Name { get; init; }

    /// <summary>The process a <c>startProcess</c> action starts for the form's or row's record. The schema requires it there and refuses it on a <c>navigate</c> or <c>delete</c> action.</summary>
    public string? Process { get; init; }

    /// <summary>The page a <c>navigate</c> action opens. The schema requires it there and refuses it on a <c>startProcess</c> or <c>delete</c> action.</summary>
    public string? Page { get; init; }

    /// <summary>The text key of the button's label.</summary>
    public required TextReference Label { get; init; }

    /// <summary>The expression of each page parameter a <c>navigate</c> action gives, by parameter name, or null when it gives none.</summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>The expression of the id of the record a <c>navigate</c> action opens in a form page, or null when it names none. The schema refuses it on a <c>startProcess</c> or <c>delete</c> action.</summary>
    public string? Record { get; init; }

    /// <summary>The boolean expression over the row that decides whether the button shows, or null when it always shows. The schema allows it only in a table's <c>rowActions</c>.</summary>
    public string? VisibleWhen { get; init; }
}
