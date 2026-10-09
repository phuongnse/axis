using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled data source: a projection of its root entity's fields and of fields reached through
/// reference fields, with typed parameters, an optional filter, an optional aggregate, its default
/// sort and page size.
/// </summary>
public sealed record DataSourceModel
{
    /// <summary>The page size when the data source declares none.</summary>
    public const int DefaultPageSize = 20;

    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The data source's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    /// <summary>The root entity.</summary>
    public required EntityReference Entity { get; init; }

    /// <summary>The projected fields, in declaration order.</summary>
    public required IReadOnlyList<DataSourceFieldModel> Fields { get; init; }

    /// <summary>The typed inputs of the filter, in declaration order.</summary>
    public IReadOnlyList<DataSourceParameterModel> Parameters { get; init; } = [];

    /// <summary>
    /// The boolean filter over the root entity's fields, the parameters and paths through reference
    /// fields, inside the SQL subset, or null when every row passes.
    /// </summary>
    public ExpressionModel? Filter { get; init; }

    /// <summary>
    /// The grouping of the filtered rows, or null when each row is one root record. When it is set,
    /// a row is one group and holds the group fields, then the measures.
    /// </summary>
    public DataSourceAggregateModel? Aggregate { get; init; }

    /// <summary>
    /// The default order, or null when rows are ordered by id alone, or by the group fields when the
    /// data source is grouped.
    /// </summary>
    public DataSourceSortModel? Sort { get; init; }

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Finds a projected field by name. The name matches exactly, as the query <c>sort</c> does.</summary>
    public bool TryGetField(string name, [NotNullWhen(true)] out DataSourceFieldModel? field)
    {
        field = Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        return field is not null;
    }

    /// <summary>Finds a measure by name. The name matches exactly, as the query <c>sort</c> does.</summary>
    public bool TryGetMeasure(string name, [NotNullWhen(true)] out DataSourceMeasureModel? measure)
    {
        measure = Aggregate?.Measures.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        return measure is not null;
    }
}

/// <summary>
/// A projected field: its key in a row and the path of fields it reads, starting at a field of the
/// root entity. Every field of the path before the last is a reference field.
/// </summary>
public sealed record DataSourceFieldModel(string Name, IReadOnlyList<FieldModel> Path)
{
    /// <summary>A projected field that reads a field of the root entity.</summary>
    public DataSourceFieldModel(string name, FieldModel field)
        : this(name, [field])
    {
    }

    /// <summary>The field the path ends at.</summary>
    public FieldModel Field => Path[^1];
}

/// <summary>
/// A typed input of the filter. The rows endpoint reads it from the query string under its exact
/// name. When it is not given, it is null in the filter.
/// </summary>
public sealed record DataSourceParameterModel(
    string Name, FieldType Type, bool Required, TextReference? Label, IReadOnlyList<string>? Values, EntityReference? Target);

/// <summary>
/// The default order of a data source: a projected field name, or a group field or measure name when
/// the data source is grouped, ascending or descending.
/// </summary>
public sealed record DataSourceSortModel(string FieldName, bool Descending);

/// <summary>
/// The grouping of a data source: the projected fields the filtered rows are grouped by, in
/// <c>groupBy</c> order, and the measures computed for each group. No group field means one total row.
/// </summary>
public sealed record DataSourceAggregateModel(IReadOnlyList<DataSourceFieldModel> GroupBy, IReadOnlyList<DataSourceMeasureModel> Measures);

/// <summary>The function of a measure.</summary>
public enum AggregateFunction
{
    /// <summary>The number of rows in the group. It takes no field.</summary>
    Count,

    /// <summary>The sum of an integer or decimal field, written like a decimal.</summary>
    Sum,

    /// <summary>The smallest value of an integer, decimal, date or date-time field.</summary>
    Min,

    /// <summary>The largest value of an integer, decimal, date or date-time field.</summary>
    Max,
}

/// <summary>
/// A measure of a grouped data source: its key in a grouped row, its function and the projected
/// field it reads, which is null for <see cref="AggregateFunction.Count"/>. A sum, min or max over
/// only null values is null.
/// </summary>
public sealed record DataSourceMeasureModel(string Name, AggregateFunction Function, DataSourceFieldModel? Field);
