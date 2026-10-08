using System.Diagnostics.CodeAnalysis;
using Axis.Configuration.Model;

namespace Axis.Data.DataSources;

/// <summary>The order of a data source page: one projected field that is not a reference, ascending or descending.</summary>
public sealed record DataSourceSort(DataSourceFieldModel Field, bool Descending)
{
    /// <summary>
    /// Parses a projected field name, optionally preceded by one <c>-</c> for descending order.
    /// The name is matched exactly. A projected reference or child collection field cannot be sorted by.
    /// </summary>
    public static bool TryParse(string text, DataSourceModel dataSource, [NotNullWhen(true)] out DataSourceSort? sort)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(dataSource);

        var descending = text.StartsWith('-');
        var name = descending ? text[1..] : text;
        sort = dataSource.TryGetField(name, out var field) && field.Field.HasColumn && field.Field.Type != FieldType.Reference
            ? new DataSourceSort(field, descending)
            : null;
        return sort is not null;
    }

    /// <summary>The data source's default order, or <see langword="null"/> when it declares none.</summary>
    public static DataSourceSort? Default(DataSourceModel dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        // The compiler has checked that the default sort names a projected field that is not a reference.
        return dataSource.Sort is { } sort && dataSource.TryGetField(sort.FieldName, out var field)
            ? new DataSourceSort(field, sort.Descending)
            : null;
    }
}
