using System.Collections.ObjectModel;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the <c>roles</c> and <c>connectors</c> maps of the <c>modules</c> entries of the pinned
/// modules and follows them. An entry of module M for a module D it depends on sends each slot of D
/// to a slot of M, matched exactly. Following the chain gives each slot of D the application role or
/// connector that the slot of M reaches. The application's own entry for D comes first, then the
/// entries of the depending modules in pin order. A slot that two of them map to different roles or
/// connectors is <see cref="DiagnosticCodes.ModuleSlotMappingConflict"/>, reported at the later
/// entry, and the earlier one stays. A chain that breaks gives the slot nothing, and the break is
/// reported where it is. A required input is checked against the connector at the end of each chain.
/// </summary>
internal sealed class ModuleSlotChains
{
    private static readonly IReadOnlyDictionary<string, string> _none = ReadOnlyDictionary<string, string>.Empty;

    private readonly IReadOnlyDictionary<string, ModuleManifest> _manifests;
    private readonly IReadOnlyList<ModuleDependency> _dependencies;
    private readonly IReadOnlyList<ConnectorResource> _connectors;
    private readonly List<Diagnostic> _diagnostics;
    private readonly SlotKind _roleKind;
    private readonly SlotKind _connectorKind;

    public ModuleSlotChains(
        IReadOnlyDictionary<string, ModuleManifest> manifests,
        IReadOnlyList<ModuleDependency> dependencies,
        IReadOnlyList<ConnectorResource> connectors,
        List<Diagnostic> diagnostics)
    {
        _manifests = manifests;
        _dependencies = dependencies;
        _connectors = connectors;
        _diagnostics = diagnostics;
        _roleKind = new SlotKind(
            "role",
            "roles",
            entry => entry.Roles,
            manifest => manifest.Roles.Select(slot => slot.Name).ToList(),
            DiagnosticCodes.UnmappedModuleRoleSlot,
            DiagnosticCodes.UnknownModuleSlotRole,
            DiagnosticCodes.UnknownModuleRoleSlot);
        _connectorKind = new SlotKind(
            "connector",
            "connectors",
            entry => entry.Connectors,
            manifest => manifest.Connectors.Select(slot => slot.Name).ToList(),
            DiagnosticCodes.UnmappedModuleConnectorSlot,
            DiagnosticCodes.UnknownModuleSlotConnector,
            DiagnosticCodes.UnknownModuleConnectorSlot);
    }

    /// <summary>One <c>modules</c> entry of a pinned module for a pinned module: <c>Index</c> is its place in the asker's list.</summary>
    public sealed record ModuleDependency(string Asker, string Target, int Index);

    /// <summary>What differs between roles and connectors: names, the entry's map, the slot names of a manifest and the three codes.</summary>
    private sealed record SlotKind(
        string Noun,
        string Segment,
        Func<ApplicationModuleDefinition, IReadOnlyDictionary<string, string>> Map,
        Func<ModuleManifest, IReadOnlyList<string>> Slots,
        string UnmappedCode,
        string UnknownValueCode,
        string UnknownKeyCode);

    /// <summary>
    /// Checks every entry and returns the pins with the maps the chains give. <paramref name="pins"/>
    /// come in pin order with the application's own maps.
    /// </summary>
    public List<PinnedModuleModel> Apply(IReadOnlyList<PinnedModuleModel> pins)
    {
        foreach (var dependency in _dependencies)
        {
            CheckEntry(dependency);
        }

        var roles = Resolve(_roleKind, pins, pin => pin.Roles);
        var connectors = Resolve(_connectorKind, pins, pin => pin.Connectors);
        return [.. pins.Select(pin => pin with { Roles = roles[pin.Id], Connectors = connectors[pin.Id] })];
    }

    /// <summary>Checks the maps of one entry against the slots of the module it depends on and of the module that holds it.</summary>
    private void CheckEntry(ModuleDependency dependency)
    {
        var asker = _manifests[dependency.Asker];
        var target = _manifests[dependency.Target];
        var entry = asker.Modules[dependency.Index];
        var file = $"modules/{asker.Id}/{ModuleCatalog.ManifestFileName}";
        var path = $"/modules/{dependency.Index}";
        void Report(string code, string message, string at) =>
            _diagnostics.Add(new Diagnostic(code, message, file, at, null));

        foreach (var kind in new[] { _roleKind, _connectorKind })
        {
            var map = kind.Map(entry);
            var targetSlots = kind.Slots(target);
            var askerSlots = kind.Slots(asker).ToHashSet(StringComparer.Ordinal);
            var mapPath = map.Count > 0 ? $"{path}/{kind.Segment}" : path;
            foreach (var slot in targetSlots.Where(slot => !map.ContainsKey(slot)))
            {
                Report(
                    kind.UnmappedCode,
                    $"The {kind.Noun} slot '{slot}' of module '{target.Id}' {target.Version} is not mapped. Map it to a {kind.Noun} slot of module '{asker.Id}' under '{kind.Segment}'.",
                    mapPath);
            }

            foreach (var (key, value) in map)
            {
                var keyPath = $"{path}/{kind.Segment}/{ModuleChecker.Escape(key)}";
                if (!targetSlots.Contains(key))
                {
                    Report(
                        kind.UnknownKeyCode,
                        targetSlots.Count == 0
                            ? $"The module '{target.Id}' {target.Version} has no {kind.Noun} slots, so '{key}' maps nothing."
                            : $"The module '{target.Id}' {target.Version} has no {kind.Noun} slot '{key}'. Its slots are {string.Join(", ", targetSlots.Select(slot => $"'{slot}'"))}.",
                        keyPath);
                }
                else if (!askerSlots.Contains(value))
                {
                    Report(
                        kind.UnknownValueCode,
                        $"The {kind.Noun} slot '{key}' is mapped to '{value}', but module '{asker.Id}' has no {kind.Noun} slot '{value}'.",
                        keyPath);
                }
                else if (ReferenceEquals(kind, _connectorKind))
                {
                    ConnectorChecker.CheckSlotAgainstSlot(
                        target.Connectors.First(slot => slot.Name == key),
                        target.Id,
                        asker.Connectors.First(slot => slot.Name == value),
                        asker.Id,
                        (code, message) => Report(code, message, keyPath));
                }
            }
        }
    }

    /// <summary>Gives the final map of every pin, from the application's own map and then the chains in pin order.</summary>
    private Dictionary<string, IReadOnlyDictionary<string, string>> Resolve(
        SlotKind kind, IReadOnlyList<PinnedModuleModel> pins, Func<PinnedModuleModel, IReadOnlyDictionary<string, string>> own)
    {
        var applicationMaps = pins.ToDictionary(pin => pin.Id, own, StringComparer.Ordinal);
        var resolved = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);

        IReadOnlyDictionary<string, string> ResolveOne(string id)
        {
            if (resolved.TryGetValue(id, out var done))
            {
                return done;
            }

            // A cycle is reported elsewhere, and a module inside it gets nothing through itself.
            if (!visiting.Add(id))
            {
                return _none;
            }

            var result = new Dictionary<string, string>(applicationMaps[id], StringComparer.Ordinal);
            var through = result.ToDictionary(pair => pair.Key, _ => "the application", StringComparer.Ordinal);
            var target = _manifests[id];
            foreach (var dependency in _dependencies.Where(candidate => candidate.Target == id))
            {
                var asker = _manifests[dependency.Asker];
                var map = kind.Map(asker.Modules[dependency.Index]);
                var reached = ResolveOne(dependency.Asker);
                foreach (var slot in kind.Slots(target))
                {
                    if (!map.TryGetValue(slot, out var askerSlot) || !reached.TryGetValue(askerSlot, out var value))
                    {
                        continue;
                    }

                    var how = $"module '{asker.Id}' slot '{askerSlot}'";
                    var at = $"/modules/{dependency.Index}/{kind.Segment}/{ModuleChecker.Escape(slot)}";
                    if (ReferenceEquals(kind, _connectorKind))
                    {
                        CheckRequiredInputs(target, asker, slot, value, at);
                    }

                    if (result.TryAdd(slot, value))
                    {
                        through[slot] = how;
                    }
                    else if (!string.Equals(result[slot], value, StringComparison.Ordinal))
                    {
                        _diagnostics.Add(new Diagnostic(
                            DiagnosticCodes.ModuleSlotMappingConflict,
                            $"The {kind.Noun} slot '{slot}' of module '{target.Id}' maps to '{result[slot]}' through {through[slot]}, but to '{value}' through {how}. Both mappings must give the same {kind.Noun}.",
                            $"modules/{asker.Id}/{ModuleCatalog.ManifestFileName}",
                            at,
                            null));
                    }
                }
            }

            visiting.Remove(id);
            return resolved[id] = result.Count == 0 ? _none : result;
        }

        foreach (var pin in pins)
        {
            ResolveOne(pin.Id);
        }

        return resolved;
    }

    /// <summary>Checks the required inputs of the connector at the end of a chain against the slot of <paramref name="target"/>.</summary>
    private void CheckRequiredInputs(ModuleManifest target, ModuleManifest asker, string slot, string connectorName, string at)
    {
        if (_connectors.FirstOrDefault(candidate => string.Equals(candidate.Name, connectorName, StringComparison.OrdinalIgnoreCase)) is { } connector)
        {
            ConnectorChecker.CheckRequiredInputs(
                target.Connectors.First(candidate => candidate.Name == slot),
                connector,
                (code, message) => _diagnostics.Add(new Diagnostic(code, message, $"modules/{asker.Id}/{ModuleCatalog.ManifestFileName}", at, null)));
        }
    }
}
