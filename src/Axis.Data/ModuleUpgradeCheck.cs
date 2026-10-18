using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Modules;
using Axis.Configuration.Resources;
using Axis.Configuration.Storage;

namespace Axis.Data;

/// <summary>
/// Compares the module copies of a release with those of the application's active release. A module
/// upgrade may only add entities and fields, because the module's tables already hold data. Dropping
/// an entity, dropping a field and changing a field's <c>type</c> are reported with
/// <see cref="DiagnosticCodes.NonAdditiveModuleUpgrade"/>. Entities match by the id written in the module
/// file, and fields by name, ignoring letter case. Removing a module counts as dropping each of its
/// entities. Other field properties are not compared here.
/// </summary>
public static class ModuleUpgradeCheck
{
    private const string NoVersion = "none";

    private const string Rule = "A module upgrade may only add entities and fields.";

    /// <summary>
    /// Reports what <paramref name="next"/> changes in the module copies of <paramref name="active"/> that
    /// is not additive. Files outside <c>modules/</c> are ignored. A release stored before module copies
    /// existed has none, and then nothing is reported.
    /// </summary>
    public static IReadOnlyList<Diagnostic> Check(IReadOnlyList<ReleaseResource> active, IReadOnlyList<ReleaseResource> next)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(next);

        var before = ReadModules(active);
        var after = ReadModules(next);
        var diagnostics = new List<Diagnostic>();
        foreach (var (moduleId, oldModule) in before)
        {
            after.TryGetValue(moduleId, out var newModule);
            var newVersion = newModule?.Version ?? NoVersion;
            foreach (var (entityId, oldEntity) in oldModule.Entities)
            {
                if (newModule is null || !newModule.Entities.TryGetValue(entityId, out var newEntity))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.NonAdditiveModuleUpgrade,
                        $"Version {newVersion} of the module '{moduleId}' drops the entity '{oldEntity.Name}' that version {oldModule.Version} has. {Rule}",
                        oldEntity.Path,
                        "",
                        oldEntity.Id));
                    continue;
                }

                foreach (var (fieldName, oldField) in oldEntity.Fields)
                {
                    if (!newEntity.Fields.TryGetValue(fieldName, out var newField))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.NonAdditiveModuleUpgrade,
                            $"Version {newVersion} of the module '{moduleId}' drops the field '{oldField.Name}' of the entity '{oldEntity.Name}' that version {oldModule.Version} has. {Rule}",
                            newEntity.Path,
                            "/fields",
                            newEntity.Id));
                    }
                    else if (!string.Equals(oldField.Type, newField.Type, StringComparison.Ordinal))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.NonAdditiveModuleUpgrade,
                            $"Version {newVersion} of the module '{moduleId}' changes the type of the field '{newField.Name}' of the entity '{oldEntity.Name}' from '{oldField.Type}' in version {oldModule.Version} to '{newField.Type}'. {Rule}",
                            newEntity.Path,
                            $"/fields/{newField.Index}/type",
                            newEntity.Id));
                    }
                }
            }
        }

        return DiagnosticOrder.Sort(diagnostics);
    }

    private static Dictionary<string, ModuleCopy> ReadModules(IReadOnlyList<ReleaseResource> resources)
    {
        var modules = new Dictionary<string, ModuleCopy>(StringComparer.Ordinal);
        foreach (var resource in resources.OrderBy(resource => resource.Path, StringComparer.Ordinal))
        {
            if (!resource.Path.StartsWith(ApplicationLoader.ModulesFolderPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var segments = resource.Path.Split('/');
            if (segments.Length < 3)
            {
                continue;
            }

            if (!modules.TryGetValue(segments[1], out var module))
            {
                modules[segments[1]] = module = new ModuleCopy();
            }

            using var document = JsonDocument.Parse(resource.Content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (segments.Length == 3 && segments[2] == ModuleCatalog.ManifestFileName)
            {
                module.Version = root.TryGetProperty("version", out var version) && version.GetString() is { } text ? text : module.Version;
            }
            else if (root.TryGetProperty("kind", out var kind) && kind.GetString() == ResourceKinds.Entity
                && root.TryGetProperty("id", out var id) && id.GetString() is { } entityId)
            {
                module.Entities[entityId] = ReadEntity(resource.Path, entityId, root);
            }
        }

        return modules;
    }

    private static EntityCopy ReadEntity(string path, string id, JsonElement root)
    {
        var name = root.TryGetProperty("name", out var nameElement) && nameElement.GetString() is { } text ? text : path;
        var fields = new Dictionary<string, FieldCopy>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("fields", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var field in array.EnumerateArray())
            {
                if (field.ValueKind == JsonValueKind.Object && field.TryGetProperty("name", out var fieldName) && fieldName.GetString() is { } fieldText)
                {
                    var type = field.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                    fields.TryAdd(fieldText, new FieldCopy(fieldText, index, type));
                }

                index++;
            }
        }

        return new EntityCopy(path, Guid.TryParse(id, out var parsed) ? parsed : null, name, fields);
    }

    private sealed class ModuleCopy
    {
        public string Version { get; set; } = "unknown";

        public Dictionary<string, EntityCopy> Entities { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record EntityCopy(string Path, Guid? Id, string Name, Dictionary<string, FieldCopy> Fields);

    private sealed record FieldCopy(string Name, int Index, string? Type);
}
