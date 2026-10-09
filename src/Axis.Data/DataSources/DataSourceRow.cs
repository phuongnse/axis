using System.Text.Json.Nodes;

namespace Axis.Data.DataSources;

/// <summary>
/// A row of a data source. <see cref="Id"/> is the id of the root record. <see cref="Values"/>
/// holds every projected field under its projected name, in projection order; a SQL <c>NULL</c> is
/// a <see langword="null"/> value. <see cref="Labels"/> maps each projected reference field that is
/// not null to the display field value of the referenced record; it is empty when there is none.
/// </summary>
public sealed record DataSourceRow(
    Guid? Id,
    IReadOnlyDictionary<string, JsonValue?> Values,
    IReadOnlyDictionary<string, string> Labels);

/// <summary>One page of data source rows and the number of rows that pass the filter.</summary>
public sealed record DataSourceRowPage(IReadOnlyList<DataSourceRow> Items, long TotalCount);
