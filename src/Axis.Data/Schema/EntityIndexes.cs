using Axis.Configuration.Model;
using Axis.Data.Naming;

namespace Axis.Data.Schema;

/// <summary>An index an entity's table should have. <see cref="Declared"/> is the position of the author's declaration in the entity's <c>indexes</c>, or null for an index only the platform adds. A <see cref="Trigram"/> index is a <c>pg_trgm</c> GIN index on <c>lower()</c> of its one column.</summary>
internal sealed record ExpectedIndex(string Name, IReadOnlyList<string> Columns, bool Unique, int? Declared, bool Trigram = false);

/// <summary>The indexes that the planner gives an entity's table.</summary>
internal static class EntityIndexes
{
    /// <summary>
    /// The indexes of <paramref name="entity"/>'s table, in the order they are created: the owner column of a child table,
    /// each reference column in field order, then each declared index in declaration order, then the trigram index of each text field that is searchable or is the display field.
    /// An index of the same columns has the same name, so a declared index over one reference column merges with the automatic one, and is unique when the declared one is.
    /// </summary>
    public static IReadOnlyList<ExpectedIndex> Expected(EntityModel entity, string table, bool isChild)
    {
        var indexes = new List<ExpectedIndex>();

        void Add(IReadOnlyList<string> columns, bool unique, int? declared)
        {
            var name = EntityNaming.Index(table, columns);
            var existing = indexes.FindIndex(index => index.Name == name);
            if (existing < 0)
            {
                indexes.Add(new ExpectedIndex(name, columns, unique, declared));
            }
            else
            {
                indexes[existing] = indexes[existing] with { Unique = indexes[existing].Unique || unique, Declared = indexes[existing].Declared ?? declared };
            }
        }

        if (isChild)
        {
            Add([EntityNaming.OwnerColumn], false, null);
        }

        foreach (var field in entity.Fields.Where(field => field.Type == FieldType.Reference))
        {
            Add([EntityNaming.Column(field.Name)], false, null);
        }

        for (var index = 0; index < entity.Indexes.Count; index++)
        {
            var declared = entity.Indexes[index];
            Add([.. declared.Fields.Select(EntityNaming.Column)], declared.Unique, index);
        }

        foreach (var field in entity.Fields.Where(field => field.Type == FieldType.Text && field.HasColumn))
        {
            var column = EntityNaming.Column(field.Name);
            var name = EntityNaming.TrigramIndex(table, column);
            if ((field.Searchable || string.Equals(field.Name, entity.DisplayField, StringComparison.OrdinalIgnoreCase)) && indexes.All(index => index.Name != name))
            {
                indexes.Add(new ExpectedIndex(name, [column], false, null, Trigram: true));
            }
        }

        return indexes;
    }

    /// <summary>
    /// The statement that creates <paramref name="index"/> on <paramref name="table"/>. With <paramref name="concurrently"/>
    /// it does not block writes to the table, and it cannot run in a transaction block.
    /// </summary>
    public static string CreateSql(string table, ExpectedIndex index, bool concurrently = false)
    {
        // A trigram index names its operator class with its schema, so the statement does not depend on the connection's search_path.
        var target = index.Trigram
            ? $"USING gin (lower({EntityNaming.Quote(index.Columns[0])}) public.gin_trgm_ops)"
            : $"({string.Join(", ", index.Columns.Select(EntityNaming.Quote))})";
        return $"CREATE {(index.Unique ? "UNIQUE " : "")}INDEX {(concurrently ? "CONCURRENTLY " : "")}{EntityNaming.Quote(index.Name)} ON {EntityNaming.QualifiedTable(table)} {target}";
    }

    /// <summary>The statement that drops the index <paramref name="name"/>. Concurrently, it cannot run in a transaction block.</summary>
    public static string DropSql(string name, bool concurrently = false) =>
        $"DROP INDEX {(concurrently ? "CONCURRENTLY " : "")}{EntityNaming.Quote(EntityNaming.Schema)}.{EntityNaming.Quote(name)}";
}
