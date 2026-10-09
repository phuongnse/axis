namespace Axis.Configuration.Resources;

/// <summary>
/// A measure as written in the data source file: its <c>name</c> in a grouped row, its
/// <c>function</c>, which is <c>count</c>, <c>sum</c>, <c>min</c> or <c>max</c>, and the projected
/// <c>field</c> it reads. <c>count</c> takes no field.
/// </summary>
public sealed record DataSourceMeasureDefinition
{
    public required string Name { get; init; }

    public required string Function { get; init; }

    public string? Field { get; init; }
}
