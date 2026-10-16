namespace Axis.Data.Schema;

/// <summary>The entity tables that exist in the tenant database, read from its catalog.</summary>
public sealed record CatalogSnapshot(IReadOnlyList<CatalogTable> Tables)
{
    public static CatalogSnapshot Empty { get; } = new([]);
}

/// <summary>An entity table. <see cref="Name"/> is the physical name without the schema.</summary>
public sealed record CatalogTable(string Name, IReadOnlyList<CatalogColumn> Columns, bool HasRows);

/// <summary>
/// A column of an entity table. <see cref="Type"/> is spelled as <c>format_type</c> renders it.
/// <see cref="Unique"/> is whether a single-column unique constraint covers it, and
/// <see cref="ReferencedTable"/> is the table its foreign key points to, without the schema.
/// <see cref="Filled"/> and <see cref="AddedByFill"/> are set only on the planner's copy of a column
/// that a pending <c>fill</c> migration covers: the fill leaves it with no nulls, or it also adds the column.
/// </summary>
public sealed record CatalogColumn(string Name, string Type, bool NotNull, bool Unique, string? ReferencedTable)
{
    public bool Filled { get; init; }

    public bool AddedByFill { get; init; }
}
