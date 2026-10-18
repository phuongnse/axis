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
/// files come back under <c>modules/&lt;id&gt;/</c>, ready to be stored in the release. The entities,
/// sequences, seeds, events, data sources and forms of a pinned module come back as resources named
/// <c>&lt;id&gt;/&lt;name&gt;</c>, keeping the ids written in the files. Its rules come back by module,
/// under their plain names, for <see cref="RuleChecker.CheckModules"/>. Inside a module, a field's
/// <c>target</c> or <c>sequence</c>, an event's, data source's or form's <c>entity</c> and a data source
/// parameter's <c>target</c> name a resource of the same module without <c>/</c>, and are rewritten to
/// the qualified name.
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
    /// problems, in the order written, the canonical files of the pinned versions that are valid and
    /// the resources read from those files.
    /// </summary>
    public static (IReadOnlyList<PinnedModuleModel> Pins, IReadOnlyList<ResourceContent> Files, ModuleResources Resources) Check(
        ApplicationManifest application,
        IReadOnlyCollection<InstalledModule> installed,
        List<Diagnostic> diagnostics,
        ReferenceIndexBuilder references)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, application.File, path, application.Id));

        var pins = new List<PinnedModuleModel>();
        var files = new List<ResourceContent>();
        var resources = new ModuleResourceCollector();
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
            CheckFiles(pinnedModule, files, resources, diagnostics);
        }

        return (pins, files, resources.ToResources());
    }

    /// <summary>Checks each file of a pinned module and adds the valid ones, in canonical form, under <c>modules/&lt;id&gt;/</c>.</summary>
    private static void CheckFiles(
        InstalledModule module, List<ResourceContent> files, ModuleResourceCollector resources, List<Diagnostic> diagnostics)
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
                if (file.Path != ModuleCatalog.ManifestFileName)
                {
                    if (!CheckKind(path, root, resourceId, diagnostics))
                    {
                        resources.AddUnloaded(module.Manifest.Id, root);
                        continue;
                    }

                    resources.Add(module.Manifest.Id, path, root, diagnostics);
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

    /// <summary>Collects the entities, sequences, seeds, rules, events, data sources, forms and texts of the pinned modules, in the order of the pins and then by path.</summary>
    private sealed class ModuleResourceCollector
    {
        private readonly List<EntityResource> _entities = [];
        private readonly List<SequenceResource> _sequences = [];
        private readonly List<SeedResource> _seeds = [];
        private readonly List<(string ModuleId, List<RuleResource> Rules)> _rules = [];
        private readonly List<EventResource> _events = [];
        private readonly List<DataSourceResource> _dataSources = [];
        private readonly List<FormResource> _forms = [];
        private readonly List<ModuleText> _texts = [];
        private readonly HashSet<string> _unloadedEntityNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unloadedSequenceNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unloadedEventNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unloadedDataSourceNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unloadedFormNames = new(StringComparer.OrdinalIgnoreCase);

        public ModuleResources ToResources() =>
            new(
                _entities,
                _sequences,
                _seeds,
                [.. _rules.Select(module => (module.ModuleId, (IReadOnlyList<RuleResource>)module.Rules))],
                _events,
                _dataSources,
                _forms,
                _texts,
                _unloadedEntityNames,
                _unloadedSequenceNames,
                _unloadedEventNames,
                _unloadedDataSourceNames,
                _unloadedFormNames);

        /// <summary>Notes the name of an entity, sequence, event, data source or form file that failed its schema, so a reference to it is not reported again.</summary>
        public void AddUnloaded(string moduleId, JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("name", out var name)
                || name.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var qualified = $"{moduleId}/{name.GetString()}";
            switch (kind.GetString())
            {
                case ResourceKinds.Entity:
                    _unloadedEntityNames.Add(qualified);
                    break;
                case ResourceKinds.Sequence:
                    _unloadedSequenceNames.Add(qualified);
                    break;
                case ResourceKinds.Event:
                    _unloadedEventNames.Add(qualified);
                    break;
                case ResourceKinds.DataSource:
                    _unloadedDataSourceNames.Add(qualified);
                    break;
                case ResourceKinds.Form:
                    _unloadedFormNames.Add(qualified);
                    break;
            }
        }

        /// <summary>Reads a file that passed its schema, if it is an entity, a sequence, a seed, a rule, an event, a data source, a form or a text.</summary>
        public void Add(string moduleId, string file, JsonElement root, List<Diagnostic> diagnostics)
        {
            switch (root.GetProperty("kind").GetString())
            {
                case ResourceKinds.Entity:
                    var entity = root.Deserialize<EntityResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    _entities.Add(Qualify(moduleId, entity, diagnostics));
                    break;
                case ResourceKinds.Sequence:
                    var sequence = root.Deserialize<SequenceResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    _sequences.Add(sequence with { Name = $"{moduleId}/{sequence.Name}" });
                    break;
                case ResourceKinds.Seed:
                    var seed = root.Deserialize<SeedResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    _seeds.Add(seed with { Name = $"{moduleId}/{seed.Name}", Entity = $"{moduleId}/{seed.Entity}" });
                    break;
                case ResourceKinds.Rule:
                    var rule = root.Deserialize<RuleResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    var module = _rules.Find(entry => entry.ModuleId == moduleId);
                    if (module.Rules is null)
                    {
                        module = (moduleId, []);
                        _rules.Add(module);
                    }

                    module.Rules.Add(rule);
                    break;
                case ResourceKinds.Event:
                    var @event = root.Deserialize<EventResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    _events.Add(@event with
                    {
                        Name = $"{moduleId}/{@event.Name}",
                        Entity = Rewrite(moduleId, @event.Entity, @event.File, @event.Id, "/entity", _unloadedEntityNames, diagnostics)!,
                    });
                    break;
                case ResourceKinds.DataSource:
                    var dataSource = root.Deserialize<DataSourceResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    _dataSources.Add(Qualify(moduleId, dataSource, diagnostics));
                    break;
                case ResourceKinds.Form:
                    var form = root.Deserialize<FormResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    _forms.Add(form with
                    {
                        Name = $"{moduleId}/{form.Name}",
                        Entity = Rewrite(moduleId, form.Entity, form.File, form.Id, "/entity", _unloadedEntityNames, diagnostics)!,
                    });
                    break;
                case ResourceKinds.Text:
                    var text = root.Deserialize<TextResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    _texts.Add(new ModuleText(moduleId, text));
                    break;
            }
        }

        /// <summary>
        /// Gives the entity its qualified name and rewrites each field's <c>target</c> and <c>sequence</c>
        /// to the qualified name of a resource of the same module. A value that already contains
        /// <c>/</c> is reported, and its rewritten name is left unresolved without a second report.
        /// </summary>
        private EntityResource Qualify(string moduleId, EntityResource entity, List<Diagnostic> diagnostics)
        {
            var fields = entity.Fields
                .Select((field, index) => field with
                {
                    Target = Rewrite(moduleId, field.Target, entity.File, entity.Id, $"/fields/{index}/target", _unloadedEntityNames, diagnostics),
                    Sequence = Rewrite(moduleId, field.Sequence, entity.File, entity.Id, $"/fields/{index}/sequence", _unloadedSequenceNames, diagnostics),
                })
                .ToList();
            return entity with { Name = $"{moduleId}/{entity.Name}", Fields = fields };
        }

        /// <summary>
        /// Gives the data source its qualified name and rewrites its <c>entity</c> and each parameter's
        /// <c>target</c> to the qualified name of an entity of the same module. A value that already
        /// contains <c>/</c> is reported, and its rewritten name is left unresolved without a second report.
        /// </summary>
        private DataSourceResource Qualify(string moduleId, DataSourceResource dataSource, List<Diagnostic> diagnostics)
        {
            var parameters = dataSource.Parameters
                .Select((parameter, index) => parameter with
                {
                    Target = Rewrite(moduleId, parameter.Target, dataSource.File, dataSource.Id, $"/parameters/{index}/target", _unloadedEntityNames, diagnostics),
                })
                .ToList();
            return dataSource with
            {
                Name = $"{moduleId}/{dataSource.Name}",
                Entity = Rewrite(moduleId, dataSource.Entity, dataSource.File, dataSource.Id, "/entity", _unloadedEntityNames, diagnostics)!,
                Parameters = parameters,
            };
        }

        /// <summary>
        /// Rewrites a reference to a resource of the same module to its qualified name. A value that
        /// already contains <c>/</c> is reported, and its rewritten name is added to
        /// <paramref name="unloaded"/> so it is left unresolved without a second report.
        /// </summary>
        private static string? Rewrite(
            string moduleId,
            string? value,
            string file,
            Guid id,
            string path,
            HashSet<string> unloaded,
            List<Diagnostic> diagnostics)
        {
            if (value is null)
            {
                return null;
            }

            var qualified = $"{moduleId}/{value}";
            if (value.Contains('/', StringComparison.Ordinal))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.QualifiedModuleReference,
                    $"A module resource names only resources of its own module, without '/'. Write '{value[(value.LastIndexOf('/') + 1)..]}', not '{value}'.",
                    file,
                    path,
                    id));
                unloaded.Add(qualified);
            }

            return qualified;
        }
    }
}

/// <summary>
/// The entities, sequences, seeds, events, data sources, forms and texts of the pinned modules, named <c>&lt;id&gt;/&lt;name&gt;</c> and
/// carrying the ids written in the module files. The rules come by module, in pin order, each in path
/// order and under its plain name. The unloaded names are those of module files that
/// failed their schema or whose reference was reported, so no second diagnostic names them. Both name
/// sets ignore letter case.
/// </summary>
internal sealed record ModuleResources(
    IReadOnlyList<EntityResource> Entities,
    IReadOnlyList<SequenceResource> Sequences,
    IReadOnlyList<SeedResource> Seeds,
    IReadOnlyList<(string ModuleId, IReadOnlyList<RuleResource> Rules)> Rules,
    IReadOnlyList<EventResource> Events,
    IReadOnlyList<DataSourceResource> DataSources,
    IReadOnlyList<FormResource> Forms,
    IReadOnlyList<ModuleText> Texts,
    IReadOnlySet<string> UnloadedEntityNames,
    IReadOnlySet<string> UnloadedSequenceNames,
    IReadOnlySet<string> UnloadedEventNames,
    IReadOnlySet<string> UnloadedDataSourceNames,
    IReadOnlySet<string> UnloadedFormNames);

/// <summary>A text resource of a pinned module, with the id of that module.</summary>
internal sealed record ModuleText(string ModuleId, TextResource Text);
