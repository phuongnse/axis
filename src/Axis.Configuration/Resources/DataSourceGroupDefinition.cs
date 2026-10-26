namespace Axis.Configuration.Resources;

/// <summary>
/// One entry of <c>groupBy</c>: a projected name, written as a plain name or as an object with a
/// date <see cref="Bucket"/>. Its name is checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record DataSourceGroupDefinition
{
    /// <summary>The projected name the rows are grouped by.</summary>
    public required string Field { get; init; }

    /// <summary>The bucket a date or date-time field is grouped into: <c>day</c>, <c>week</c>, <c>month</c> or <c>year</c>. Null for the plain name.</summary>
    public string? Bucket { get; init; }
}
