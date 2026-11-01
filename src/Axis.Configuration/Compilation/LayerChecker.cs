using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the <c>layer</c> files of an application. A layer's <c>target</c> is
/// <c>&lt;module id&gt;/&lt;resource name&gt;</c>. The module id is matched exactly and must be one the
/// application's <c>modules</c> lists, and the name is matched ignoring letter case against the files of
/// the pinned version of that module. A target that names nothing there is
/// <see cref="DiagnosticCodes.UnknownLayerTarget"/>. A name that more than one kind of resource has is
/// <see cref="DiagnosticCodes.AmbiguousLayerTarget"/>, because the target does not say which is meant. A
/// <c>scenario</c> is <see cref="DiagnosticCodes.LayerTargetNotAllowed"/>, and so is <c>module</c> when the
/// module has no resource of that name, because it names <c>module.json</c>. A module that is listed but
/// has no pin already has its own diagnostic, so its layers get no target diagnostic. The file must be
/// <c>customizations/&lt;target&gt;.json</c> exactly, a target is named by one layer only, and
/// <c>changes</c> must not set <c>id</c>, <c>kind</c>, <c>name</c> or <c>formatVersion</c>.
/// </summary>
internal static class LayerChecker
{
    private const string ModuleTargetName = "module";

    private static readonly JsonDocumentOptions _documentOptions = new() { AllowDuplicateProperties = false };

    private static readonly string[] _identityProperties = ["id", "kind", "name", "formatVersion"];

    public static void Check(
        IReadOnlyList<LayerResource> layers,
        ApplicationManifest application,
        IReadOnlyList<PinnedModuleModel> pins,
        IReadOnlyCollection<InstalledModule> installed,
        List<Diagnostic> diagnostics)
    {
        if (layers.Count == 0)
        {
            return;
        }

        var declared = new HashSet<string>(application.Modules.Select(module => module.Id), StringComparer.Ordinal);
        var pinnedById = pins.ToDictionary(pin => pin.Id, StringComparer.Ordinal);
        var namesByModule = new Dictionary<string, ILookup<string, string>>(StringComparer.Ordinal);
        var firstFileByTarget = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var layer in layers)
        {
            CheckTarget(layer, declared, pinnedById, installed, namesByModule, diagnostics);

            var expectedFile = $"customizations/{layer.Target}.json";
            if (layer.File != expectedFile)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.MisplacedLayer,
                    $"A layer for '{layer.Target}' must be the file '{expectedFile}'.",
                    layer.File,
                    "/target",
                    layer.Id));
            }

            if (!firstFileByTarget.TryAdd(layer.Target, layer.File))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.DuplicateLayerTarget,
                    $"The target '{layer.Target}' is already named by '{firstFileByTarget[layer.Target]}'. A resource has one layer.",
                    layer.File,
                    "/target",
                    layer.Id));
            }

            if (layer.Changes.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in _identityProperties)
                {
                    if (layer.Changes.TryGetProperty(property, out _))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.LayerChangesIdentity,
                            $"A layer cannot change the '{property}' of its target.",
                            layer.File,
                            $"/changes/{property}",
                            layer.Id));
                    }
                }
            }
        }
    }

    private static void CheckTarget(
        LayerResource layer,
        HashSet<string> declared,
        Dictionary<string, PinnedModuleModel> pinnedById,
        IReadOnlyCollection<InstalledModule> installed,
        Dictionary<string, ILookup<string, string>> namesByModule,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message) =>
            diagnostics.Add(new Diagnostic(code, message, layer.File, "/target", layer.Id));

        var slash = layer.Target.IndexOf('/', StringComparison.Ordinal);
        var moduleId = layer.Target[..slash];
        var name = layer.Target[(slash + 1)..];
        if (!declared.Contains(moduleId))
        {
            Report(
                DiagnosticCodes.UnknownLayerTarget,
                pinnedById.ContainsKey(moduleId)
                    ? $"The module '{moduleId}' is not declared in the application's modules, so the target '{layer.Target}' cannot be changed. A module that the application only reaches through another module cannot be customized."
                    : $"The module '{moduleId}' is not declared in the application's modules, so the target '{layer.Target}' names nothing.");
            return;
        }

        if (!pinnedById.TryGetValue(moduleId, out var pin))
        {
            return;
        }

        if (!namesByModule.TryGetValue(moduleId, out var kindsByName))
        {
            var module = installed.First(candidate =>
                string.Equals(candidate.Manifest.Id, pin.Id, StringComparison.Ordinal)
                && string.Equals(candidate.Manifest.Version, pin.Version, StringComparison.Ordinal));
            namesByModule[moduleId] = kindsByName = KindsByName(module);
        }

        var kinds = kindsByName[name].Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (kinds.Count > 1)
        {
            Report(
                DiagnosticCodes.AmbiguousLayerTarget,
                $"The module '{moduleId}' has more than one resource named '{name}', of the kinds {string.Join(", ", kinds.Select(kind => $"'{kind}'"))}, so the target '{layer.Target}' does not say which one is meant.");
        }
        else if (kinds.Count == 1)
        {
            if (kinds[0] == ResourceKinds.Scenario)
            {
                Report(
                    DiagnosticCodes.LayerTargetNotAllowed,
                    $"The target '{layer.Target}' is a scenario of the module '{moduleId}'. A layer cannot change a module's scenario.");
            }
        }
        else if (string.Equals(name, ModuleTargetName, StringComparison.OrdinalIgnoreCase))
        {
            Report(
                DiagnosticCodes.LayerTargetNotAllowed,
                $"The target '{layer.Target}' is the file '{ModuleCatalog.ManifestFileName}' of the module '{moduleId}'. A layer cannot change a module's manifest.");
        }
        else
        {
            Report(
                DiagnosticCodes.UnknownLayerTarget,
                $"The module '{moduleId}' has no resource named '{name}', so the target '{layer.Target}' names nothing.");
        }
    }

    /// <summary>
    /// The kinds of the files of a module that have each name, matched ignoring letter case. A file that
    /// is not valid JSON, or has no <c>kind</c> or <c>name</c> text, is left out, because
    /// <see cref="ModuleChecker"/> reports it.
    /// </summary>
    private static ILookup<string, string> KindsByName(InstalledModule module)
    {
        var pairs = new List<(string Name, string Kind)>();
        foreach (var file in module.Files)
        {
            if (file.Path == ModuleCatalog.ManifestFileName)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(file.Content, _documentOptions);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String
                    && root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                {
                    pairs.Add((name.GetString()!, kind.GetString()!));
                }
            }
            catch (JsonException)
            {
            }
        }

        return pairs.ToLookup(pair => pair.Name, pair => pair.Kind, StringComparer.OrdinalIgnoreCase);
    }
}
