using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Model;

namespace Axis.Data.DataSources;

/// <summary>
/// The order of a data source page, ascending or descending: one projected field that is not a
/// reference, or for a grouped data source one group field that is not a reference or one measure.
/// Exactly one of <see cref="Field"/> and <see cref="Measure"/> is set.
/// </summary>
public sealed record DataSourceSort(DataSourceFieldModel? Field, bool Descending, DataSourceMeasureModel? Measure = null)
{
    /// <summary>
    /// Parses a name, optionally preceded by one <c>-</c> for descending order. The name is matched
    /// exactly. It names a projected field, or a group field or a measure when the data source is
    /// grouped. A reference or child collection field cannot be sorted by.
    /// </summary>
    public static bool TryParse(string text, DataSourceModel dataSource, [NotNullWhen(true)] out DataSourceSort? sort)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dataSource);

        var descending = text.StartsWith('-');
        sort = Find(dataSource, descending ? text[1..] : text, descending);
        return sort is not null;
    }

    /// <summary>The data source's default order, or <see langword="null"/> when it declares none.</summary>
    public static DataSourceSort? Default(DataSourceModel dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        // The compiler has checked that the default sort names a field or measure that can be sorted by.
        return dataSource.Sort is { } sort ? Find(dataSource, sort.FieldName, sort.Descending) : null;
    }

    private static DataSourceSort? Find(DataSourceModel dataSource, string name, bool descending)
    {
        if (dataSource.TryGetMeasure(name, out var measure))
        {
            return new DataSourceSort(null, descending, measure);
        }

        // A grouped row holds only its group fields and measures.
        var field = dataSource.Aggregate is { } aggregate
            ? aggregate.GroupBy.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal))
            : dataSource.TryGetField(name, out var projected) ? projected : null;
        return field is not null && field.Field.HasColumn && field.Field.Type != FieldType.Reference
            ? new DataSourceSort(field, descending)
            : null;
    }
}
