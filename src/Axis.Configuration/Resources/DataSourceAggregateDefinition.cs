namespace Axis.Configuration.Resources;

/// <summary>
/// The <c>aggregate</c> of a data source file: the projected names its rows are grouped by and the
/// measures computed for each group. Its names are checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record DataSourceAggregateDefinition
{
    /// <summary>The projected names the rows are grouped by, in file order. Empty for one total row.</summary>
    public required IReadOnlyList<string> GroupBy { get; init; }

    /// <summary>The measures, in file order.</summary>
    public required IReadOnlyList<DataSourceMeasureDefinition> Measures { get; init; }
}
