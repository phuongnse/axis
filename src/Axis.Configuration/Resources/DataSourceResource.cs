namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>dataSource</c> resource: a read-only projection of one root entity's fields, with a
/// default sort and page size. Its entity, field paths and sort are checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record DataSourceResource : Resource
{
    public required string Entity { get; init; }

    /// <summary>The projection, in file order.</summary>
    public required IReadOnlyList<DataSourceFieldDefinition> Fields { get; init; }

    /// <summary>The default order: a projected name, optionally preceded by <c>-</c> for descending order.</summary>
    public string? Sort { get; init; }

    /// <summary>The default page size, from 1 to 100.</summary>
    public int? PageSize { get; init; }
}
