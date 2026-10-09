using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled data source: a projection of its root entity's fields and of fields reached through
/// reference fields, with typed parameters, an optional filter, its default sort and page size.
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

    /// <summary>The default order, or null when rows are ordered by id alone.</summary>
    public DataSourceSortModel? Sort { get; init; }

    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>Finds a projected field by name. The name matches exactly, as the query <c>sort</c> does.</summary>
    public bool TryGetField(string name, [NotNullWhen(true)] out DataSourceFieldModel? field)
    {
        field = Fields.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        return field is not null;
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

/// <summary>The default order of a data source: a projected field name, ascending or descending.</summary>
public sealed record DataSourceSortModel(string FieldName, bool Descending);
