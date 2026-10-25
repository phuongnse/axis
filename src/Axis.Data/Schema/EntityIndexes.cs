using Axis.Configuration.Model;
using Axis.Data.Naming;

namespace Axis.Data.Schema;

/// <summary>An index an entity's table should have. <see cref="Declared"/> is the position of the author's declaration in the entity's <c>indexes</c>, or null for an index only the platform adds.</summary>
internal sealed record ExpectedIndex(string Name, IReadOnlyList<string> Columns, bool Unique, int? Declared);

/// <summary>The indexes that the planner gives an entity's table.</summary>
internal static class EntityIndexes
{
    /// <summary>
    /// The indexes of <paramref name="entity"/>'s table, in the order they are created: the owner column of a child table,
    /// each reference column in field order, then each declared index in declaration order. An index of the same columns
    /// has the same name, so a declared index over one reference column merges with the automatic one, and is unique when the declared one is.
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

        return indexes;
    }
}
