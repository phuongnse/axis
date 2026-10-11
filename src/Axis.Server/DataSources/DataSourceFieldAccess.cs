using Axis.Configuration.Model;
using Axis.Data.DataSources;
using Axis.Policy;

namespace Axis.Server.DataSources;

/// <summary>
/// The reference labels a user may see in the rows of a data source they may read. A data source
/// is readable only when the user may read every field along the path of every projected field,
/// grouped or not: the first field on the root entity, and each later one on the target of the
/// reference before it. A reference label shows only when the user may also read the target
/// entity's display field. The target's record filters do not hide it.
/// </summary>
internal sealed class DataSourceFieldAccess
{
    private readonly HashSet<string> _labelled;

    private DataSourceFieldAccess(HashSet<string> labelled) => _labelled = labelled;

    /// <summary>
    /// What <paramref name="roles"/> may see of the rows of <paramref name="dataSource"/>, or null
    /// when any projected field passes through a field they may not read.
    /// </summary>
    public static DataSourceFieldAccess? Read(ApplicationModel application, IEnumerable<string> roles, DataSourceModel dataSource)
    {
        var held = roles.ToList();
        var readable = new Dictionary<Guid, IReadOnlySet<string>?>();
        bool CanRead(Guid entityId, string name)
        {
            if (!readable.TryGetValue(entityId, out var fields))
            {
                // An entity that is not in the model has no grant, so none of its fields is readable.
                fields = application.FindEntity(entityId) is { } entity
                    ? PolicyEvaluator.Fields(application, held, entity, PolicyAction.Read)
                    : new HashSet<string>();
                readable.Add(entityId, fields);
            }

            return fields is null || fields.Contains(name);
        }

        var labelled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in dataSource.Fields)
        {
            var entityId = dataSource.Entity.Id;
            foreach (var hop in field.Path)
            {
                if (!CanRead(entityId, hop.Name))
                {
                    return null;
                }

                entityId = hop.Target?.Id ?? Guid.Empty;
            }

            if (field.Field.Type == FieldType.Reference && CanRead(field.Field.Target!.Id, field.Field.TargetDisplayField!))
            {
                labelled.Add(field.Name);
            }
        }

        return new DataSourceFieldAccess(labelled);
    }

    /// <summary><paramref name="row"/> with only the labels the user may see.</summary>
    public DataSourceRow Visible(DataSourceRow row) =>
        row with
        {
            Labels = row.Labels
                .Where(label => _labelled.Contains(label.Key))
                .ToDictionary(label => label.Key, label => label.Value, StringComparer.Ordinal),
        };
}
