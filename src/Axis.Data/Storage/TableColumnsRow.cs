namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.table_columns</c>: the keys of the columns a person shows in one table, in order, see
/// <c>docs/reference/frontend.md#table-views-and-columns</c>.
/// </summary>
public sealed class TableColumnsRow
{
    public required string UserId { get; init; }

    public required string TableKey { get; init; }

    public required string[] Columns { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
