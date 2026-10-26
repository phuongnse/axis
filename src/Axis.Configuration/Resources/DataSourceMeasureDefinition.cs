namespace Axis.Configuration.Resources;

/// <summary>
/// A measure as written in the data source file: its <c>name</c> in a grouped row, its
/// <c>function</c>, which is <c>count</c>, <c>sum</c>, <c>min</c>, <c>max</c>, <c>avg</c> or
/// <c>countDistinct</c>, and the projected <c>field</c> it reads. <c>count</c> takes no field. An optional
/// <c>filter</c> keeps only the rows where a boolean expression is true.
/// </summary>
public sealed record DataSourceMeasureDefinition
{
    public required string Name { get; init; }

    public required string Function { get; init; }

    public string? Field { get; init; }

    public string? Filter { get; init; }
}
