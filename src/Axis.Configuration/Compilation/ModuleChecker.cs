using System.Globalization;
using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the <c>modules</c> of an application manifest against the installed module versions,
/// pins each one and collects the pinned version's files. A module id appears at most once,
/// matched exactly. A version range has the forms of <see cref="VersionRange"/>. A module with no
/// installed version is unknown, and a known module with no installed version in its range cannot
/// be pinned. Otherwise the highest installed version in the range is pinned, and each of its
/// files is checked as JSON, for a kind a module may hold and against the schema of that kind. The
/// files come back under <c>modules/&lt;id&gt;/</c>, ready to be stored in the release.
/// </summary>
internal static class ModuleChecker
{
    private static readonly JsonDocumentOptions _documentOptions = new() { AllowDuplicateProperties = false };

    private static readonly HashSet<string> _allowedKinds = new(StringComparer.Ordinal)
    {
        ResourceKinds.Entity,
        ResourceKinds.DataSource,
        ResourceKinds.Rule,
        ResourceKinds.Sequence,
        ResourceKinds.Form,
        ResourceKinds.Event,
        ResourceKinds.Text,
        ResourceKinds.Seed,
    };

    /// <summary>
    /// Checks <paramref name="application"/>'s modules and returns the pins of the entries without
    /// problems, in the order written, and the canonical files of the pinned versions that are valid.
    /// </summary>
    public static (IReadOnlyList<PinnedModuleModel> Pins, IReadOnlyList<ResourceContent> Files) Check(
        ApplicationManifest application,
        IReadOnlyCollection<InstalledModule> installed,
        List<Diagnostic> diagnostics,
        ReferenceIndexBuilder references)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, application.File, path, application.Id));

        var pins = new List<PinnedModuleModel>();
        var files = new List<ResourceContent>();
        var firstIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < application.Modules.Count; index++)
        {
            var module = application.Modules[index];
            var path = $"/modules/{index}";
            if (!firstIndexById.TryAdd(module.Id, index))
            {
                Report(
                    DiagnosticCodes.DuplicateApplicationModule,
                    $"The module '{module.Id}' is already listed at '/modules/{firstIndexById[module.Id]}'. List each module once.",
                    $"{path}/id");
                continue;
            }

            var versions = installed
                .Where(candidate => string.Equals(candidate.Manifest.Id, module.Id, StringComparison.Ordinal))
                .Select(candidate => candidate.Manifest.Version)
                .Distinct(StringComparer.Ordinal)
                .Order(Comparer<string>.Create(PackageVersions.Compare))
                .ToList();
            if (!VersionRange.TryParse(module.Version, out var range))
            {
                Report(
                    DiagnosticCodes.InvalidModuleRange,
                    $"The version range '{module.Version}' is not valid. Use an exact version such as '1.2.0', '^1.2.0' for any later version with the same major version, or '~1.2.0' for any later version with the same minor version.",
                    $"{path}/version");
            }

            if (versions.Count == 0)
            {
                Report(
                    DiagnosticCodes.UnknownModule,
                    $"The module '{module.Id}' is not installed. No version of it is installed.",
                    $"{path}/id");
                continue;
            }

            if (range is null)
            {
                continue;
            }

            var pinned = versions.LastOrDefault(range.Matches);
            if (pinned is null)
            {
                Report(
                    DiagnosticCodes.NoModuleVersionInRange,
                    $"No installed version of module '{module.Id}' is in the range '{module.Version}'. The installed versions are {string.Join(", ", versions.Select(version => $"'{version}'"))}.",
                    $"{path}/version");
                continue;
            }

            pins.Add(new PinnedModuleModel(module.Id, module.Version, pinned));
            references.Add(application, path, ReferenceRelations.Module, new ModuleNode(module.Id));
            var pinnedModule = installed.First(candidate =>
                string.Equals(candidate.Manifest.Id, module.Id, StringComparison.Ordinal)
                && string.Equals(candidate.Manifest.Version, pinned, StringComparison.Ordinal));
            CheckFiles(pinnedModule, files, diagnostics);
        }

        return (pins, files);
    }

    /// <summary>Checks each file of a pinned module and adds the valid ones, in canonical form, under <c>modules/&lt;id&gt;/</c>.</summary>
    private static void CheckFiles(InstalledModule module, List<ResourceContent> files, List<Diagnostic> diagnostics)
    {
        foreach (var file in module.Files)
        {
            var path = $"modules/{module.Manifest.Id}/{file.Path}";
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(file.Content, _documentOptions);
            }
            catch (JsonException exception)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.InvalidJson,
                    exception.LineNumber is { } line
                        ? string.Create(CultureInfo.InvariantCulture, $"The file is not valid JSON (line {line + 1}, byte {exception.BytePositionInLine + 1}).")
                        : "The file is not valid JSON: a property appears more than once.",
                    path,
                    ""));
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                var resourceId = ReadResourceId(root);
                if (file.Path != ModuleCatalog.ManifestFileName && !CheckKind(path, root, resourceId, diagnostics))
                {
                    continue;
                }

                files.Add(new ResourceContent(path, JsonCanonicalizer.Canonicalize(root)));
            }
        }
    }

    /// <summary>Checks that a module file has a kind a module may hold and is valid against the schema of that kind.</summary>
    private static bool CheckKind(string path, JsonElement root, Guid? resourceId, List<Diagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("kind", out var kindElement)
            || kindElement.ValueKind != JsonValueKind.String
            || !_allowedKinds.Contains(kindElement.GetString()!)
            || !ResourceSchemas.TryGet(kindElement.GetString()!, out var schema))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.ModuleKindNotAllowed,
                $"A module file must be a JSON object whose 'kind' is one of '{string.Join("', '", _allowedKinds.Order(StringComparer.Ordinal))}'.",
                path,
                "/kind",
                resourceId));
            return false;
        }

        return SchemaDiagnostics.Validate(schema, root, DiagnosticCodes.SchemaViolation, path, resourceId, diagnostics);
    }

    private static Guid? ReadResourceId(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
        && Guid.TryParse(id.GetString(), out var value)
            ? value
            : null;
}
