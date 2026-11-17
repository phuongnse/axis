using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Model;
using Axis.Data.Records;

namespace Axis.Data.DataSources;

/// <summary>
/// The <c>filter</c> of a data source rows request: the record API's filter over the columns of the data source.
/// The columns are the group fields of a grouped data source, otherwise every projected field. A bucketed group
/// field is a date. The rules are in the data sources reference, under
/// <see href="docs/reference/data-sources.md#query-parameters">query parameters</see>. The filter is parsed by
/// <see cref="RecordListFilter"/> over a stand-in entity of the columns, so there is one parser.
/// </summary>
public static class DataSourceListFilter
{
    /// <summary>The columns a request filter compares: the group fields of a grouped data source, otherwise every projected field.</summary>
    public static IReadOnlyList<DataSourceFieldModel> Columns(DataSourceModel dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        return dataSource.Aggregate is { } aggregate ? aggregate.GroupBy : dataSource.Fields;
    }

    /// <summary>
    /// Parses <paramref name="json"/> against the columns of <paramref name="dataSource"/>, which match by name exactly.
    /// Every projected field is already readable by the caller, so the filter checks no read access. A filter that ends
    /// empty succeeds with a null <paramref name="filter"/>. <paramref name="error"/> never holds text of <paramref name="json"/>.
    /// </summary>
    public static bool TryParse(string json, DataSourceModel dataSource, out RecordListFilter? filter, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        return RecordListFilter.TryParse(json, Entity(dataSource), _ => true, out filter, out error);
    }

    // Each column as a field named by its data source name, of the type its row value has, so a bucket is a date.
    private static EntityModel Entity(DataSourceModel dataSource) => new()
    {
        Id = dataSource.Entity.Id,
        Name = dataSource.Name,
        File = dataSource.File,
        Fields = [.. Columns(dataSource).Select(column => column.Field with { Name = column.Name, Type = column.ValueType })],
    };
}
