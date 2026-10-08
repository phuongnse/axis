using System.Diagnostics.CodeAnalysis;

namespace Axis.Configuration.Model;

/// <summary>A compiled data source: a projection of its root entity's fields, with its default sort and page size.</summary>
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

/// <summary>A projected field: its key in a row and the root entity's field it reads.</summary>
public sealed record DataSourceFieldModel(string Name, FieldModel Field);

/// <summary>The default order of a data source: a projected field name, ascending or descending.</summary>
public sealed record DataSourceSortModel(string FieldName, bool Descending);
