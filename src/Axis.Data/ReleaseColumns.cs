using System.Text.Json;
using Axis.Configuration.Resources;
using Axis.Configuration.Storage;

namespace Axis.Data;

/// <summary>
/// What a stored release says about the columns it uses. Activation and the retry of a process instance
/// share these, so both ask the same question: does the release use a changed column.
/// </summary>
public static class ReleaseColumns
{
    /// <summary>
    /// The field names of each entity of the release's stored resources by entity id, ignoring letter case.
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlySet<string>> FieldsByEntityId(Release release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var fields = new Dictionary<Guid, IReadOnlySet<string>>();
        foreach (var resource in release.Resources)
        {
            using var document = JsonDocument.Parse(resource.Content);
            var root = document.RootElement;
            if (root.TryGetProperty("kind", out var kind) && kind.GetString() == ResourceKinds.Entity
                && root.TryGetProperty("id", out var entityId) && Guid.TryParse(entityId.GetString(), out var parsedId))
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("fields", out var entityFields) && entityFields.ValueKind == JsonValueKind.Array)
                {
                    foreach (var field in entityFields.EnumerateArray())
                    {
                        if (field.ValueKind == JsonValueKind.Object
                            && field.TryGetProperty("name", out var name) && name.GetString() is { } fieldName)
                        {
                            names.Add(fieldName);
                        }
                    }
                }

                fields[parsedId] = names;
            }
        }

        return fields;
    }

    /// <summary>The ids of the migration files the release holds.</summary>
    public static IReadOnlySet<Guid> MigrationIds(Release release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var ids = new HashSet<Guid>();
        foreach (var resource in release.Resources)
        {
            using var document = JsonDocument.Parse(resource.Content);
            var root = document.RootElement;
            if (root.TryGetProperty("kind", out var kind) && kind.GetString() == ResourceKinds.Migration
                && root.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var migrationId))
            {
                ids.Add(migrationId);
            }
        }

        return ids;
    }

    /// <summary>
    /// Whether the release uses the column. <paramref name="fieldsByEntityId"/> is <see langword="null"/> when the
    /// stored release was not found, and then the release uses every column. A <see langword="null"/>
    /// <paramref name="field"/> names the whole table, which the release uses when it has the entity.
    /// </summary>
    public static bool Uses(IReadOnlyDictionary<Guid, IReadOnlySet<string>>? fieldsByEntityId, Guid entityId, string? field) =>
        fieldsByEntityId is not { } entities
        || (entities.TryGetValue(entityId, out var fields) && (field is null || fields.Contains(field)));
}
