using System.Collections.ObjectModel;
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
/// Checks the <c>modules</c> of an application manifest and of the modules it reaches against the
/// installed module versions, resolves them flat, pins each one and collects the pinned version's
/// files. A module id appears at most once in a list, matched exactly. A version range has the forms
/// of <see cref="VersionRange"/>. A module with no installed version is unknown. Each module id the
/// application or a pinned module asks for gets one version, the highest installed version that
/// meets every range that asks for it. When none does, one range gives
/// <see cref="DiagnosticCodes.NoModuleVersionInRange"/> and several give
/// <see cref="DiagnosticCodes.ModuleRangeConflict"/>. A cycle between modules, a module that
/// depends on itself included, gives <see cref="DiagnosticCodes.ModuleDependencyCycle"/>. Each
/// pinned module's files are checked as JSON, for a kind a module may hold and against the schema
/// of that kind. The files come back under <c>modules/&lt;id&gt;/</c>, ready to be stored in the
/// release, once for each module. The entities,
/// sequences, seeds, events, data sources, forms, pages, processes, policies and triggers of a pinned module come back as resources named
/// <c>&lt;id&gt;/&lt;name&gt;</c>, keeping the ids written in the files. Its rules come back by module,
/// under their plain names, for <see cref="RuleChecker.CheckModules"/>. Inside a module, a field's
/// <c>target</c> or <c>sequence</c>, an event's, data source's or form's <c>entity</c> and a data source
/// parameter's <c>target</c> name a resource of the same module without <c>/</c>, and are rewritten to
/// the qualified name. All of them but a <c>sequence</c> may instead name a resource of a module the
/// module declares in its <c>modules</c> as <c>&lt;id&gt;/&lt;name&gt;</c>, which is kept as written. A
/// page's widgets, actions and parameters name only the module's own resources, never a module it
/// declares, so each <c>entity</c>, <c>dataSource</c>, <c>form</c>, <c>process</c> and navigate
/// <c>page</c> and each parameter <c>target</c> is rewritten to the qualified name, and any other
/// value that contains <c>/</c> is <see cref="DiagnosticCodes.QualifiedModuleReference"/>. A
/// process's subject <c>entity</c>, and its steps' <c>form</c>, <c>event</c>, sub-process <c>process</c> and
/// create-record <c>entity</c>, are rewritten the same way. Only an <c>entity</c> and an <c>event</c> may name a
/// declared module, a <c>form</c> and a <c>process</c> never do. A trigger's <c>process</c> is rewritten like a
/// sub-process, and its <c>event</c> and <c>entity</c> like a process's. A module may hold a trigger of type
/// <c>event</c> or <c>dataChange</c>, and a <c>schedule</c> trigger is <see cref="DiagnosticCodes.ModuleKindNotAllowed"/>
/// at <c>/type</c>. A
/// policy's grants name only the module's own resources, so each grant's <c>entity</c> and
/// <c>dataSource</c> is qualified with the module id as written, and <see cref="PolicyChecker"/> reports
/// one that is not a plain name. The <c>roles</c> of an entry must map each role slot of the pinned version to
/// a role of the application, and the pin keeps that map. The <c>connectors</c> of an entry must map
/// each connector slot of the pinned version to a connector of the application that fits it, see
/// <see cref="ConnectorChecker.CheckSlot"/>, and the pin keeps that map too.
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
        ResourceKinds.Page,
        ResourceKinds.Process,
        ResourceKinds.Event,
        ResourceKinds.Text,
        ResourceKinds.Seed,
        ResourceKinds.Policy,
        ResourceKinds.Trigger,
    };

    /// <summary>
    /// Checks <paramref name="application"/>'s modules and the modules they depend on, and returns
    /// the pins of the modules without problems, the application's entries as written and then each
    /// dependency in the order it is first reached, the canonical files of the pinned versions that
    /// are valid and the resources read from those files.
    /// </summary>
    public static (IReadOnlyList<PinnedModuleModel> Pins, IReadOnlyList<ResourceContent> Files, ModuleResources Resources) Check(
        ApplicationManifest application,
        IReadOnlyCollection<InstalledModule> installed,
        List<Diagnostic> diagnostics,
        ReferenceIndexBuilder references,
        IReadOnlyList<RoleResource> roles,
        IReadOnlySet<string> unloadedRoleNames,
        IReadOnlyList<ConnectorResource> connectors,
        IReadOnlySet<string> unloadedConnectorNames)
    {
        var versionsById = installed
            .GroupBy(candidate => candidate.Manifest.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(candidate => candidate.Manifest.Version)
                    .Distinct(StringComparer.Ordinal)
                    .Order(Comparer<string>.Create(PackageVersions.Compare))
                    .ToList(),
                StringComparer.Ordinal);

        var applicationRequests = ReadEntries(
            application.Modules, application.File, application.Id, null, versionsById, diagnostics);

        // The entries of each module version are read once. Their problems are reported only for the
        // versions that end up pinned, because a version picked in an earlier round may be replaced.
        var entriesByVersion = new Dictionary<(string Id, string Version), (List<ModuleRequest> Requests, List<Diagnostic> Problems)>();
        (List<ModuleRequest> Requests, List<Diagnostic> Problems) EntriesOf(string id, string version)
        {
            if (!entriesByVersion.TryGetValue((id, version), out var entries))
            {
                var module = installed.First(candidate =>
                    string.Equals(candidate.Manifest.Id, id, StringComparison.Ordinal)
                    && string.Equals(candidate.Manifest.Version, version, StringComparison.Ordinal));
                var problems = new List<Diagnostic>();
                var requests = ReadEntries(
                    module.Manifest.Modules, $"{ApplicationLoader.ModulesFolderPrefix}{id}/{ModuleCatalog.ManifestFileName}", null, id, versionsById, problems);
                entriesByVersion[(id, version)] = entries = (requests, problems);
            }

            return entries;
        }

        // Each round walks the graph with the versions picked so far and picks again from every range
        // that asks. It stops when no pick changes. There is no backtracking, so a graph whose picks
        // never settle stops at the guard, and each id still changing is reported as a conflict.
        var picks = new Dictionary<string, string>(StringComparer.Ordinal);
        var unstable = new HashSet<string>(StringComparer.Ordinal);
        List<string> order;
        Dictionary<string, List<ModuleRequest>> requestsById;
        var maxRounds = installed.Count + 2;
        for (var round = 1; ; round++)
        {
            order = [];
            requestsById = new Dictionary<string, List<ModuleRequest>>(StringComparer.Ordinal);
            void Ask(ModuleRequest request)
            {
                if (!requestsById.TryGetValue(request.Id, out var asks))
                {
                    requestsById[request.Id] = asks = [];
                    order.Add(request.Id);
                }

                asks.Add(request);
            }

            foreach (var request in applicationRequests)
            {
                Ask(request);
            }

            for (var position = 0; position < order.Count; position++)
            {
                if (picks.TryGetValue(order[position], out var picked))
                {
                    foreach (var request in EntriesOf(order[position], picked).Requests)
                    {
                        Ask(request);
                    }
                }
            }

            var next = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var id in order)
            {
                var pick = versionsById[id].LastOrDefault(version => requestsById[id].All(request => request.Range.Matches(version)));
                if (pick is not null)
                {
                    next[id] = pick;
                }
            }

            if (next.Count == picks.Count && next.All(pair => picks.TryGetValue(pair.Key, out var current) && current == pair.Value))
            {
                break;
            }

            if (round >= maxRounds)
            {
                unstable.UnionWith(next.Where(pair => !picks.TryGetValue(pair.Key, out var current) || current != pair.Value).Select(pair => pair.Key));
                unstable.UnionWith(picks.Keys.Where(id => !next.ContainsKey(id)));
                foreach (var id in unstable)
                {
                    next.Remove(id);
                }

                picks = next;
                break;
            }

            picks = next;
        }

        foreach (var id in order)
        {
            if (picks.TryGetValue(id, out var picked))
            {
                diagnostics.AddRange(EntriesOf(id, picked).Problems);
            }
            else
            {
                ReportUnresolved(id, requestsById[id], versionsById[id], unstable.Contains(id), application, diagnostics);
            }
        }

        ReportCycles(applicationRequests, picks, EntriesOf, diagnostics);

        var pins = new List<PinnedModuleModel>();
        var files = new List<ResourceContent>();
        var resources = new ModuleResourceCollector();
        var declaredByModule = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        var slotsByModule = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        var manifests = new Dictionary<string, ModuleManifest>(StringComparer.Ordinal);
        foreach (var id in order)
        {
            if (!picks.TryGetValue(id, out var picked))
            {
                continue;
            }

            var first = requestsById[id][0];
            var pinnedModule = installed.First(candidate =>
                string.Equals(candidate.Manifest.Id, id, StringComparison.Ordinal)
                && string.Equals(candidate.Manifest.Version, picked, StringComparison.Ordinal));
            if (first.Asker is null)
            {
                references.Add(application, $"/modules/{first.Index}", ReferenceRelations.Module, new ModuleNode(id));
                var slotRoles = CheckRoleSlots(application, first.Index, application.Modules[first.Index], pinnedModule.Manifest, roles, unloadedRoleNames, diagnostics);
                var slotConnectors = CheckConnectorSlots(application, first.Index, application.Modules[first.Index], pinnedModule.Manifest, connectors, unloadedConnectorNames, diagnostics);
                pins.Add(new PinnedModuleModel(id, first.RangeText, picked) { Roles = slotRoles, Connectors = slotConnectors });
            }
            else
            {
                pins.Add(new PinnedModuleModel(id, first.RangeText, picked));
            }

            manifests[id] = pinnedModule.Manifest;
            var declared = new HashSet<string>(pinnedModule.Manifest.Modules.Select(module => module.Id), StringComparer.Ordinal);
            declaredByModule[id] = declared;
            slotsByModule[id] = pinnedModule.Manifest.Roles.Select(slot => slot.Name).ToHashSet(StringComparer.Ordinal);
            CheckFiles(pinnedModule, declared, files, resources, diagnostics);
        }

        // The entries of a pinned module for a pinned module send the slots of the dependency to its own.
        var dependencies = order
            .Where(manifests.ContainsKey)
            .SelectMany(asker => EntriesOf(asker, picks[asker]).Requests
                .Where(request => manifests.ContainsKey(request.Id))
                .Select(request => new ModuleSlotChains.ModuleDependency(asker, request.Id, request.Index)))
            .ToList();
        var chained = new ModuleSlotChains(manifests, dependencies, connectors, diagnostics).Apply(pins);

        return (chained, files, resources.ToResources(declaredByModule, slotsByModule));
    }

    /// <summary>
    /// Checks a <c>modules</c> list: a module id appears once, its range is valid and the module has an
    /// installed version. Returns the entries without problems, in the order written.
    /// </summary>
    private static List<ModuleRequest> ReadEntries(
        IReadOnlyList<ApplicationModuleDefinition> entries,
        string file,
        Guid? resourceId,
        string? asker,
        Dictionary<string, List<string>> versionsById,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, file, path, resourceId));

        var requests = new List<ModuleRequest>();
        var firstIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Count; index++)
        {
            var module = entries[index];
            var path = $"/modules/{index}";
            if (!firstIndexById.TryAdd(module.Id, index))
            {
                Report(
                    DiagnosticCodes.DuplicateApplicationModule,
                    $"The module '{module.Id}' is already listed at '/modules/{firstIndexById[module.Id]}'. List each module once.",
                    $"{path}/id");
                continue;
            }

            if (!VersionRange.TryParse(module.Version, out var range))
            {
                Report(
                    DiagnosticCodes.InvalidModuleRange,
                    $"The version range '{module.Version}' is not valid. Use an exact version such as '1.2.0', '^1.2.0' for any later version with the same major version, or '~1.2.0' for any later version with the same minor version.",
                    $"{path}/version");
            }

            if (!versionsById.ContainsKey(module.Id))
            {
                Report(
                    DiagnosticCodes.UnknownModule,
                    $"The module '{module.Id}' is not installed. No version of it is installed.",
                    $"{path}/id");
                continue;
            }

            if (range is not null)
            {
                requests.Add(new ModuleRequest(module.Id, asker, file, resourceId, index, module.Version, range));
            }
        }

        return requests;
    }

    /// <summary>
    /// Reports a module that no installed version can be pinned for. One range gives
    /// <see cref="DiagnosticCodes.NoModuleVersionInRange"/> at its entry. Several give
    /// <see cref="DiagnosticCodes.ModuleRangeConflict"/>, naming every asker, at the application's
    /// entry when it asks and otherwise at the first asking module.
    /// </summary>
    private static void ReportUnresolved(
        string id,
        List<ModuleRequest> requests,
        List<string> versions,
        bool unstable,
        ApplicationManifest application,
        List<Diagnostic> diagnostics)
    {
        var installedVersions = string.Join(", ", versions.Select(version => $"'{version}'"));
        if (requests.Count == 1 && !unstable)
        {
            var only = requests[0];
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.NoModuleVersionInRange,
                $"No installed version of module '{id}' is in the range '{only.RangeText}'. The installed versions are {installedVersions}.",
                only.File,
                $"/modules/{only.Index}/version",
                only.ResourceId));
            return;
        }

        var askers = string.Join(
            ", ",
            requests.Select(request => request.Asker is null
                ? $"the application asks for '{request.RangeText}'"
                : $"module '{request.Asker}' asks for '{request.RangeText}'"));
        var at = requests.Find(request => request.Asker is null) ?? requests[0];
        diagnostics.Add(new Diagnostic(
            DiagnosticCodes.ModuleRangeConflict,
            $"No installed version of module '{id}' meets every range that asks for it: {askers}. The installed versions are {installedVersions}.",
            at.File,
            $"/modules/{at.Index}/version",
            at.ResourceId));
    }

    /// <summary>
    /// Reports each cycle of the pinned modules once, at the entry that closes it, in the
    /// <c>module.json</c> of the module that holds the entry. A module that depends on itself is a
    /// cycle of one.
    /// </summary>
    private static void ReportCycles(
        List<ModuleRequest> applicationRequests,
        Dictionary<string, string> picks,
        Func<string, string, (List<ModuleRequest> Requests, List<Diagnostic> Problems)> entriesOf,
        List<Diagnostic> diagnostics)
    {
        const int onStack = 1;
        const int done = 2;
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<string>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string id)
        {
            state[id] = onStack;
            stack.Add(id);
            foreach (var entry in entriesOf(id, picks[id]).Requests)
            {
                if (!picks.ContainsKey(entry.Id))
                {
                    continue;
                }

                if (!state.TryGetValue(entry.Id, out var seen))
                {
                    Visit(entry.Id);
                }
                else if (seen == onStack)
                {
                    var cycle = stack.Skip(stack.IndexOf(entry.Id)).ToList();
                    if (reported.Add(string.Join('\n', cycle.Order(StringComparer.Ordinal))))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.ModuleDependencyCycle,
                            $"The modules depend on each other in a cycle: {string.Join(" → ", cycle.Append(entry.Id).Select(name => $"'{name}'"))}.",
                            entry.File,
                            $"/modules/{entry.Index}/id"));
                    }
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[id] = done;
        }

        foreach (var request in applicationRequests)
        {
            if (picks.ContainsKey(request.Id) && !state.ContainsKey(request.Id))
            {
                Visit(request.Id);
            }
        }
    }

    /// <summary>One <c>modules</c> entry without problems, with where it is written. <c>Asker</c> is null for the application.</summary>
    private sealed record ModuleRequest(string Id, string? Asker, string File, Guid? ResourceId, int Index, string RangeText, VersionRange Range);

    /// <summary>
    /// Checks the <c>roles</c> mapping of a module entry against the role slots of its pinned
    /// version, and the prefix of each slot label. Returns the slots mapped to a role, by slot name.
    /// Names are matched exactly. A mapped role that failed its own schema is not reported again.
    /// </summary>
    private static IReadOnlyDictionary<string, string> CheckRoleSlots(
        ApplicationManifest application,
        int index,
        ApplicationModuleDefinition entry,
        ModuleManifest manifest,
        IReadOnlyList<RoleResource> roles,
        IReadOnlySet<string> unloadedRoleNames,
        List<Diagnostic> diagnostics)
    {
        var path = $"/modules/{index}";
        void Report(string code, string message, string at) =>
            diagnostics.Add(new Diagnostic(code, message, application.File, at, application.Id));

        for (var slotIndex = 0; slotIndex < manifest.Roles.Count; slotIndex++)
        {
            var textKey = manifest.Roles[slotIndex].Label.TextKey;
            if (!textKey.StartsWith($"{manifest.Id}.", StringComparison.Ordinal))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ModuleTextKeyWithoutPrefix,
                    $"The text key '{textKey}' lacks the prefix '{manifest.Id}.'. Start each text key of the module with it.",
                    $"modules/{manifest.Id}/{ModuleCatalog.ManifestFileName}",
                    $"/roles/{slotIndex}/label/textKey"));
            }
        }

        var slotNames = manifest.Roles.Select(slot => slot.Name).ToHashSet(StringComparer.Ordinal);
        var roleNames = roles.Select(role => role.Name).ToHashSet(StringComparer.Ordinal);
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        var rolesPath = entry.Roles.Count > 0 ? $"{path}/roles" : path;
        foreach (var slot in manifest.Roles)
        {
            if (!entry.Roles.ContainsKey(slot.Name))
            {
                Report(
                    DiagnosticCodes.UnmappedModuleRoleSlot,
                    $"The role slot '{slot.Name}' of module '{manifest.Id}' {manifest.Version} is not mapped. Map it to a role of the application under 'roles'.",
                    rolesPath);
            }
        }

        foreach (var (key, roleName) in entry.Roles)
        {
            var keyPath = $"{path}/roles/{Escape(key)}";
            if (!slotNames.Contains(key))
            {
                Report(
                    DiagnosticCodes.UnknownModuleRoleSlot,
                    slotNames.Count == 0
                        ? $"The module '{manifest.Id}' {manifest.Version} has no role slots, so '{key}' maps nothing."
                        : $"The module '{manifest.Id}' {manifest.Version} has no role slot '{key}'. Its slots are {string.Join(", ", manifest.Roles.Select(slot => $"'{slot.Name}'"))}.",
                    keyPath);
            }
            else if (roleNames.Contains(roleName))
            {
                mapped[key] = roleName;
            }
            else if (!unloadedRoleNames.Contains(roleName))
            {
                Report(
                    DiagnosticCodes.UnknownModuleSlotRole,
                    $"The role slot '{key}' is mapped to '{roleName}', but the application declares no role with that name.",
                    keyPath);
            }
        }

        return mapped.Count == 0 ? ReadOnlyDictionary<string, string>.Empty : mapped;
    }

    /// <summary>
    /// Checks the <c>connectors</c> mapping of a module entry against the connector slots of its
    /// pinned version, and the prefix of each slot label. Returns the slots mapped to a connector, by
    /// slot name, to the connector's declared name. Slot names are matched exactly and connector names
    /// ignoring letter case. A mapped connector that failed its own schema is not reported again.
    /// </summary>
    private static IReadOnlyDictionary<string, string> CheckConnectorSlots(
        ApplicationManifest application,
        int index,
        ApplicationModuleDefinition entry,
        ModuleManifest manifest,
        IReadOnlyList<ConnectorResource> connectors,
        IReadOnlySet<string> unloadedConnectorNames,
        List<Diagnostic> diagnostics)
    {
        var path = $"/modules/{index}";
        void Report(string code, string message, string at) =>
            diagnostics.Add(new Diagnostic(code, message, application.File, at, application.Id));

        for (var slotIndex = 0; slotIndex < manifest.Connectors.Count; slotIndex++)
        {
            var textKey = manifest.Connectors[slotIndex].Label.TextKey;
            if (!textKey.StartsWith($"{manifest.Id}.", StringComparison.Ordinal))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ModuleTextKeyWithoutPrefix,
                    $"The text key '{textKey}' lacks the prefix '{manifest.Id}.'. Start each text key of the module with it.",
                    $"modules/{manifest.Id}/{ModuleCatalog.ManifestFileName}",
                    $"/connectors/{slotIndex}/label/textKey"));
            }
        }

        var slotsByName = manifest.Connectors.ToDictionary(slot => slot.Name, StringComparer.Ordinal);
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
        var connectorsPath = entry.Connectors.Count > 0 ? $"{path}/connectors" : path;
        foreach (var slot in manifest.Connectors)
        {
            if (!entry.Connectors.ContainsKey(slot.Name))
            {
                Report(
                    DiagnosticCodes.UnmappedModuleConnectorSlot,
                    $"The connector slot '{slot.Name}' of module '{manifest.Id}' {manifest.Version} is not mapped. Map it to a connector of the application under 'connectors'.",
                    connectorsPath);
            }
        }

        foreach (var (key, connectorName) in entry.Connectors)
        {
            var keyPath = $"{path}/connectors/{Escape(key)}";
            if (!slotsByName.TryGetValue(key, out var slot))
            {
                Report(
                    DiagnosticCodes.UnknownModuleConnectorSlot,
                    slotsByName.Count == 0
                        ? $"The module '{manifest.Id}' {manifest.Version} has no connector slots, so '{key}' maps nothing."
                        : $"The module '{manifest.Id}' {manifest.Version} has no connector slot '{key}'. Its slots are {string.Join(", ", manifest.Connectors.Select(candidate => $"'{candidate.Name}'"))}.",
                    keyPath);
            }
            else if (connectors.FirstOrDefault(candidate => string.Equals(candidate.Name, connectorName, StringComparison.OrdinalIgnoreCase)) is { } connector)
            {
                ConnectorChecker.CheckSlot(slot, connector, (code, message) => Report(code, message, keyPath));
                mapped[key] = connector.Name;
            }
            else if (!unloadedConnectorNames.Contains(connectorName))
            {
                Report(
                    DiagnosticCodes.UnknownModuleSlotConnector,
                    $"The connector slot '{key}' is mapped to '{connectorName}', but the application declares no connector with that name.",
                    keyPath);
            }
        }

        return mapped.Count == 0 ? ReadOnlyDictionary<string, string>.Empty : mapped;
    }

    /// <summary>Escapes a key as an RFC 6901 JSON Pointer segment.</summary>
    internal static string Escape(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    /// <summary>Checks each file of a pinned module and adds the valid ones, in canonical form, under <c>modules/&lt;id&gt;/</c>.</summary>
    private static void CheckFiles(
        InstalledModule module,
        IReadOnlySet<string> declared,
        List<ResourceContent> files,
        ModuleResourceCollector resources,
        List<Diagnostic> diagnostics)
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

                    resources.Add(module.Manifest.Id, declared, path, root, diagnostics);
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

        if (!SchemaDiagnostics.Validate(schema, root, DiagnosticCodes.SchemaViolation, path, resourceId, diagnostics))
        {
            return false;
        }

        // A schedule runs in the application's time zone, D103, which a module schedule trigger cannot use yet.
        if (kindElement.GetString() == ResourceKinds.Trigger
            && root.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "schedule")
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.ModuleKindNotAllowed,
                "A module may hold a trigger of type 'event' or 'dataChange'. A module schedule trigger is not supported yet.",
                path,
                "/type",
                resourceId));
            return false;
        }

        return true;
    }

    private static Guid? ReadResourceId(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
        && Guid.TryParse(id.GetString(), out var value)
            ? value
            : null;

    /// <summary>Collects the entities, sequences, seeds, rules, events, data sources, forms, pages, processes, policies, triggers and texts of the pinned modules, in the order of the pins and then by path.</summary>
    private sealed class ModuleResourceCollector
    {
        private readonly List<EntityResource> _entities = [];
        private readonly List<SequenceResource> _sequences = [];
        private readonly List<SeedResource> _seeds = [];
        private readonly List<(string ModuleId, List<RuleResource> Rules)> _rules = [];
        private readonly List<EventResource> _events = [];
        private readonly List<DataSourceResource> _dataSources = [];
        private readonly List<FormResource> _forms = [];
        private readonly List<PageResource> _pages = [];
        private readonly List<ProcessResource> _processes = [];
        private readonly List<PolicyResource> _policies = [];
        private readonly List<TriggerResource> _triggers = [];
        private readonly List<ModuleText> _texts = [];
        private readonly HashSet<string> _unloadedEntityNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unloadedSequenceNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unloadedEventNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unloadedDataSourceNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unloadedFormNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unloadedPageNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unloadedProcessNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _noModules = [];

        public ModuleResources ToResources(
            IReadOnlyDictionary<string, IReadOnlySet<string>> declaredModules,
            IReadOnlyDictionary<string, IReadOnlySet<string>> roleSlots) =>
            new(
                _entities,
                _sequences,
                _seeds,
                [.. _rules.Select(module => (module.ModuleId, (IReadOnlyList<RuleResource>)module.Rules))],
                _events,
                _dataSources,
                _forms,
                _pages,
                _policies,
                _processes,
                _triggers,
                _texts,
                _unloadedEntityNames,
                _unloadedSequenceNames,
                _unloadedEventNames,
                _unloadedDataSourceNames,
                _unloadedFormNames,
                _unloadedPageNames,
                _unloadedProcessNames,
                declaredModules,
                roleSlots);

        /// <summary>Notes the name of an entity, sequence, event, data source, form, page or process file that failed its schema, so a reference to it is not reported again.</summary>
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
                case ResourceKinds.Page:
                    _unloadedPageNames.Add(qualified);
                    break;
                case ResourceKinds.Process:
                    _unloadedProcessNames.Add(qualified);
                    break;
            }
        }

        /// <summary>Reads a file that passed its schema, if it is an entity, a sequence, a seed, a rule, an event, a data source, a form, a page, a process, a policy, a trigger or a text.</summary>
        public void Add(string moduleId, IReadOnlySet<string> declared, string file, JsonElement root, List<Diagnostic> diagnostics)
        {
            switch (root.GetProperty("kind").GetString())
            {
                case ResourceKinds.Entity:
                    var entity = root.Deserialize<EntityResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    _entities.Add(Qualify(moduleId, declared, entity, diagnostics));
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
                        Entity = Rewrite(moduleId, declared, @event.Entity, @event.File, @event.Id, "/entity", _unloadedEntityNames, diagnostics)!,
                    });
                    break;
                case ResourceKinds.DataSource:
                    var dataSource = root.Deserialize<DataSourceResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    _dataSources.Add(Qualify(moduleId, declared, dataSource, diagnostics));
                    break;
                case ResourceKinds.Form:
                    var form = root.Deserialize<FormResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    _forms.Add(form with
                    {
                        Name = $"{moduleId}/{form.Name}",
                        Entity = Rewrite(moduleId, declared, form.Entity, form.File, form.Id, "/entity", _unloadedEntityNames, diagnostics)!,
                    });
                    break;
                case ResourceKinds.Page:
                    var page = root.Deserialize<PageResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    _pages.Add(Qualify(moduleId, page, diagnostics));
                    break;
                case ResourceKinds.Process:
                    var process = root.Deserialize<ProcessResource>(ApplicationLoader.SerializerOptions)! with { File = file };
                    _processes.Add(Qualify(moduleId, declared, process, diagnostics));
                    break;
                case ResourceKinds.Trigger:
                    var trigger = root.Deserialize<TriggerResource>(ApplicationLoader.SerializerOptions)! with { File = file };

                    // The process is always the module's own. The event or entity is the module's own or a declared module's.
                    _triggers.Add(trigger with
                    {
                        Name = $"{moduleId}/{trigger.Name}",
                        Process = Rewrite(moduleId, _noModules, trigger.Process, trigger.File, trigger.Id, "/process", _unloadedProcessNames, diagnostics)!,
                        Event = Rewrite(moduleId, declared, trigger.Event, trigger.File, trigger.Id, "/event", _unloadedEventNames, diagnostics),
                        Entity = Rewrite(moduleId, declared, trigger.Entity, trigger.File, trigger.Id, "/entity", _unloadedEntityNames, diagnostics),
                    });
                    break;
                case ResourceKinds.Policy:
                    var policy = root.Deserialize<PolicyResource>(ApplicationLoader.SerializerOptions)! with { File = file };

                    // A grant names only a resource of the module, so every name is qualified with the
                    // module id, and PolicyChecker reports one that was not a plain name.
                    _policies.Add(policy with
                    {
                        Name = $"{moduleId}/{policy.Name}",
                        Grants = [.. policy.Grants.Select(grant => grant with
                        {
                            Entity = grant.Entity is null ? null : $"{moduleId}/{grant.Entity}",
                            DataSource = grant.DataSource is null ? null : $"{moduleId}/{grant.DataSource}",
                        })],
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
        /// to the qualified name of a resource of the same module. A <c>target</c> that names a module
        /// in <paramref name="declared"/> keeps its name. A sequence names only its own module's. Any other
        /// value that contains <c>/</c> is reported, and its rewritten name is left unresolved without a second report.
        /// </summary>
        private EntityResource Qualify(string moduleId, IReadOnlySet<string> declared, EntityResource entity, List<Diagnostic> diagnostics)
        {
            var fields = entity.Fields
                .Select((field, index) => field with
                {
                    Target = Rewrite(moduleId, declared, field.Target, entity.File, entity.Id, $"/fields/{index}/target", _unloadedEntityNames, diagnostics),
                    Sequence = Rewrite(moduleId, _noModules, field.Sequence, entity.File, entity.Id, $"/fields/{index}/sequence", _unloadedSequenceNames, diagnostics),
                })
                .ToList();
            return entity with { Name = $"{moduleId}/{entity.Name}", Fields = fields };
        }

        /// <summary>
        /// Gives the data source its qualified name and rewrites its <c>entity</c> and each parameter's
        /// <c>target</c> to the qualified name of an entity of the same module. A value that names a
        /// module in <paramref name="declared"/> keeps its name. Any other value that contains <c>/</c> is
        /// reported, and its rewritten name is left unresolved without a second report.
        /// </summary>
        private DataSourceResource Qualify(string moduleId, IReadOnlySet<string> declared, DataSourceResource dataSource, List<Diagnostic> diagnostics)
        {
            var parameters = dataSource.Parameters
                .Select((parameter, index) => parameter with
                {
                    Target = Rewrite(moduleId, declared, parameter.Target, dataSource.File, dataSource.Id, $"/parameters/{index}/target", _unloadedEntityNames, diagnostics),
                })
                .ToList();
            return dataSource with
            {
                Name = $"{moduleId}/{dataSource.Name}",
                Entity = Rewrite(moduleId, declared, dataSource.Entity, dataSource.File, dataSource.Id, "/entity", _unloadedEntityNames, diagnostics)!,
                Parameters = parameters,
            };
        }

        /// <summary>
        /// Gives the process its qualified name and rewrites its subject <c>entity</c> and its steps' <c>form</c>,
        /// <c>event</c>, sub-process <c>process</c> and create-record <c>entity</c> to the qualified name of a resource
        /// of the same module. An <c>entity</c> or <c>event</c> that names a module in <paramref name="declared"/>
        /// keeps its name. A <c>form</c> and a <c>process</c> name only the module's own. Any other value that
        /// contains <c>/</c> is reported, and its rewritten name is left unresolved without a second report.
        /// </summary>
        private ProcessResource Qualify(string moduleId, IReadOnlySet<string> declared, ProcessResource process, List<Diagnostic> diagnostics)
        {
            var steps = process.Steps
                .Select((step, index) => step with
                {
                    Form = Rewrite(moduleId, _noModules, step.Form, process.File, process.Id, $"/steps/{index}/form", _unloadedFormNames, diagnostics),
                    Event = Rewrite(moduleId, declared, step.Event, process.File, process.Id, $"/steps/{index}/event", _unloadedEventNames, diagnostics),
                    Process = Rewrite(moduleId, _noModules, step.Process, process.File, process.Id, $"/steps/{index}/process", _unloadedProcessNames, diagnostics),
                    Entity = Rewrite(moduleId, declared, step.Entity, process.File, process.Id, $"/steps/{index}/entity", _unloadedEntityNames, diagnostics),
                })
                .ToList();
            return process with
            {
                Name = $"{moduleId}/{process.Name}",
                Entity = Rewrite(moduleId, declared, process.Entity, process.File, process.Id, "/entity", _unloadedEntityNames, diagnostics),
                Steps = steps,
            };
        }

        /// <summary>
        /// Gives the page its qualified name and rewrites the resources its parameters and widgets name
        /// to the qualified names of resources of the same module. A page names no other module, so any
        /// value that contains <c>/</c> is reported, and its rewritten name is left unresolved without a
        /// second report.
        /// </summary>
        private PageResource Qualify(string moduleId, PageResource page, List<Diagnostic> diagnostics)
        {
            var parameters = page.Parameters
                .Select((parameter, index) => parameter with
                {
                    Target = Rewrite(moduleId, _noModules, parameter.Target, page.File, page.Id, $"/parameters/{index}/target", _unloadedEntityNames, diagnostics),
                })
                .ToList();
            return page with
            {
                Name = $"{moduleId}/{page.Name}",
                Parameters = parameters,
                Widgets = QualifyWidgets(moduleId, page, page.Widgets, "/widgets", diagnostics),
            };
        }

        private List<WidgetDefinition> QualifyWidgets(
            string moduleId,
            PageResource page,
            IReadOnlyList<WidgetDefinition> widgets,
            string listPath,
            List<Diagnostic> diagnostics) =>
            [.. widgets.Select((widget, index) => QualifyWidget(moduleId, page, widget, $"{listPath}/{index}", diagnostics))];

        private WidgetDefinition QualifyWidget(string moduleId, PageResource page, WidgetDefinition widget, string path, List<Diagnostic> diagnostics)
        {
            string? Name(string? value, string property, HashSet<string> unloaded) =>
                Rewrite(moduleId, _noModules, value, page.File, page.Id, $"{path}/{property}", unloaded, diagnostics);

            IReadOnlyList<WidgetActionDefinition>? Actions(IReadOnlyList<WidgetActionDefinition>? actions, string property) =>
                actions?.Select((action, index) => QualifyAction(moduleId, page, action, $"{path}/{property}/{index}", diagnostics)).ToList();

            return widget with
            {
                Entity = Name(widget.Entity, "entity", _unloadedEntityNames),
                DataSource = Name(widget.DataSource, "dataSource", _unloadedDataSourceNames),
                Form = Name(widget.Form, "form", _unloadedFormNames),
                Process = Name(widget.Process, "process", _unloadedProcessNames),
                Actions = Actions(widget.Actions, "actions"),
                RowActions = Actions(widget.RowActions, "rowActions"),
                ToolbarActions = Actions(widget.ToolbarActions, "toolbarActions"),
                Navigate = widget.Navigate is null ? null : QualifyAction(moduleId, page, widget.Navigate, $"{path}/navigate", diagnostics),
                Columns = widget.Columns?
                    .Select((column, index) => column with
                    {
                        Widgets = QualifyWidgets(moduleId, page, column.Widgets, $"{path}/columns/{index}/widgets", diagnostics),
                    })
                    .ToList(),
                Tabs = widget.Tabs?
                    .Select((tab, index) => tab with
                    {
                        Widgets = QualifyWidgets(moduleId, page, tab.Widgets, $"{path}/tabs/{index}/widgets", diagnostics),
                    })
                    .ToList(),
                Widgets = widget.Widgets is null ? null : QualifyWidgets(moduleId, page, widget.Widgets, $"{path}/widgets", diagnostics),
            };
        }

        private WidgetActionDefinition QualifyAction(string moduleId, PageResource page, WidgetActionDefinition action, string path, List<Diagnostic> diagnostics) =>
            action with
            {
                Process = Rewrite(moduleId, _noModules, action.Process, page.File, page.Id, $"{path}/process", _unloadedProcessNames, diagnostics),
                Page = Rewrite(moduleId, _noModules, action.Page, page.File, page.Id, $"{path}/page", _unloadedPageNames, diagnostics),
            };

        /// <summary>
        /// Rewrites a reference to a resource of the same module to its qualified name. A value whose
        /// module part, before the first <c>/</c>, is in <paramref name="declared"/> names a resource of
        /// that module and is returned as written. Any other value that contains <c>/</c> is reported,
        /// and its rewritten name is added to <paramref name="unloaded"/> so it is left unresolved
        /// without a second report.
        /// </summary>
        private static string? Rewrite(
            string moduleId,
            IReadOnlySet<string> declared,
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

            var slash = value.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0 && declared.Contains(value[..slash]))
            {
                return value;
            }

            var qualified = $"{moduleId}/{value}";
            if (slash >= 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.QualifiedModuleReference,
                    $"A module resource names its own resources without '/' and, except for a sequence, the resources of a module it declares as '<module id>/<name>'. '{value}' is neither. Write '{value[(value.LastIndexOf('/') + 1)..]}' for a resource of this module.",
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
/// The entities, sequences, seeds, events, data sources, forms, pages, processes, policies, triggers and texts of the pinned modules, named <c>&lt;id&gt;/&lt;name&gt;</c> and
/// carrying the ids written in the module files. The rules come by module, in pin order, each in path
/// order and under its plain name. <c>DeclaredModules</c> maps each pinned module to the ids in its
/// manifest's <c>modules</c>, and <c>RoleSlots</c> to the names of the slots in its manifest's <c>roles</c>,
/// matched exactly. A policy's <c>Roles</c> still name slots here, and a grant's <c>Entity</c> and
/// <c>DataSource</c> are the written name behind the module id. The unloaded names are those of module files that
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
    IReadOnlyList<PageResource> Pages,
    IReadOnlyList<PolicyResource> Policies,
    IReadOnlyList<ProcessResource> Processes,
    IReadOnlyList<TriggerResource> Triggers,
    IReadOnlyList<ModuleText> Texts,
    IReadOnlySet<string> UnloadedEntityNames,
    IReadOnlySet<string> UnloadedSequenceNames,
    IReadOnlySet<string> UnloadedEventNames,
    IReadOnlySet<string> UnloadedDataSourceNames,
    IReadOnlySet<string> UnloadedFormNames,
    IReadOnlySet<string> UnloadedPageNames,
    IReadOnlySet<string> UnloadedProcessNames,
    IReadOnlyDictionary<string, IReadOnlySet<string>> DeclaredModules,
    IReadOnlyDictionary<string, IReadOnlySet<string>> RoleSlots);

/// <summary>A text resource of a pinned module, with the id of that module.</summary>
internal sealed record ModuleText(string ModuleId, TextResource Text);
