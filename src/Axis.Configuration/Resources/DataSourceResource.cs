namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>dataSource</c> resource: a read-only projection of one root entity's fields, with an
/// optional filter, typed parameters, an optional aggregate, a default sort and page size. Its
/// entity, field paths, parameters, filter, aggregate and sort are checked by
/// <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record DataSourceResource : Resource
{
    public required string Entity { get; init; }

    /// <summary>The projection, in file order.</summary>
    public required IReadOnlyList<DataSourceFieldDefinition> Fields { get; init; }

    /// <summary>The typed inputs of the filter, in file order.</summary>
    public IReadOnlyList<DataSourceParameterDefinition> Parameters { get; init; } = [];

    /// <summary>
    /// A boolean expression over the root entity's fields and the parameters. Rows where it is not
    /// true are left out.
    /// </summary>
    public string? Filter { get; init; }

    /// <summary>The grouping of the filtered rows, or null when each row is one root record.</summary>
    public DataSourceAggregateDefinition? Aggregate { get; init; }

    /// <summary>
    /// The default order: a projected name, or a group field or measure name when the data source is
    /// grouped, optionally preceded by <c>-</c> for descending order.
    /// </summary>
    public string? Sort { get; init; }

    /// <summary>The default page size, from 1 to 100.</summary>
    public int? PageSize { get; init; }
}
