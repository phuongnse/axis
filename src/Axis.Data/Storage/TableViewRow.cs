namespace Axis.Data.Storage;

/// <summary>
/// A row of <c>axis.table_views</c>: a view that a person saved for one table, see
/// <c>docs/reference/frontend.md#table-views-and-columns</c>. A person's views of a table have names that differ ignoring letter case.
/// </summary>
public sealed class TableViewRow
{
    public required Guid Id { get; init; }

    public required string UserId { get; init; }

    public required string TableKey { get; init; }

    public required string Name { get; init; }

    public string? Search { get; init; }

    public string? Filter { get; init; }

    public string? Sort { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
