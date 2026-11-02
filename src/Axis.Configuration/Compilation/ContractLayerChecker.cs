using System.Globalization;
using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// What a layer on a contract added: the added states with their index in the layer's <c>states</c> list, and
/// the names of the added transitions.
/// </summary>
internal sealed record ContractAdditions(IReadOnlyList<(string Name, int Index)> States, IReadOnlyList<string> Transitions);

/// <summary>
/// Checks that a layer on a module's <c>contract</c> only adds elements to <c>lifecycle.states</c> and
/// <c>lifecycle.transitions</c>. A change or a remove of a state or transition of the module, any other property,
/// a state that sets <c>protected</c> and a transition into a protected state of the module are problems. See
/// <c>docs/reference/customization-layers.md#extending-a-lifecycle</c>.
/// </summary>
internal static class ContractLayerChecker
{
    private const string LifecycleProperty = "lifecycle";
    private const string StatesProperty = "states";
    private const string TransitionsProperty = "transitions";

    private static readonly string[] _identityProperties = ["id", "kind", "name", "formatVersion"];

    /// <summary>
    /// Checks <paramref name="layer"/> against <paramref name="module"/>, the contract as the module wrote it.
    /// Returns what the layer adds, or null when anything was reported, so the layer is not applied.
    /// </summary>
    public static ContractAdditions? CheckAdditions(JsonElement module, LayerResource layer, List<Diagnostic> diagnostics)
    {
        var before = diagnostics.Count;
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, layer.File, path, layer.Id));

        var states = new List<(string Name, int Index)>();
        var transitions = new List<string>();
        if (layer.Changes.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in layer.Changes.EnumerateObject())
        {
            if (property.Name == LifecycleProperty || Array.IndexOf(_identityProperties, property.Name) >= 0)
            {
                continue;
            }

            Report(
                DiagnosticCodes.ContractLayerNotAddition,
                $"A layer on a contract only adds states and transitions to its lifecycle. It cannot change '{property.Name}'.",
                $"/changes/{ModuleChecker.Escape(property.Name)}");
        }

        if (!layer.Changes.TryGetProperty(LifecycleProperty, out var lifecycle))
        {
            return diagnostics.Count == before ? new ContractAdditions(states, transitions) : null;
        }

        if (lifecycle.ValueKind != JsonValueKind.Object)
        {
            Report(DiagnosticCodes.ContractLayerNotAddition, "The 'lifecycle' of a layer on a contract holds the lists 'states' and 'transitions'.", "/changes/lifecycle");
            return null;
        }

        if (!module.TryGetProperty(LifecycleProperty, out var moduleLifecycle) || moduleLifecycle.ValueKind != JsonValueKind.Object)
        {
            Report(DiagnosticCodes.ContractLayerNotAddition, "The contract of the module has no lifecycle, so a layer cannot add one.", "/changes/lifecycle");
            return null;
        }

        foreach (var property in lifecycle.EnumerateObject())
        {
            if (property.Name is not (StatesProperty or TransitionsProperty))
            {
                Report(
                    DiagnosticCodes.ContractLayerNotAddition,
                    $"A layer on a contract only adds states and transitions to its lifecycle. It cannot change '{property.Name}'.",
                    $"/changes/lifecycle/{ModuleChecker.Escape(property.Name)}");
            }
        }

        var moduleStates = ReadList(moduleLifecycle, StatesProperty);
        var protectedStates = moduleStates
            .Where(state => state.ValueKind == JsonValueKind.Object && state.TryGetProperty("protected", out var flag) && flag.ValueKind == JsonValueKind.True)
            .Select(NameOf)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var addedStates = new HashSet<string>(StringComparer.Ordinal);
        var stateElements = ReadList(lifecycle, StatesProperty);
        for (var index = 0; index < stateElements.Count; index++)
        {
            var element = stateElements[index];
            var path = $"/changes/lifecycle/states/{index.ToString(CultureInfo.InvariantCulture)}";
            if (CheckElement(element, "state", moduleStates, path, Report) is not { } name)
            {
                continue;
            }

            if (element.TryGetProperty("protected", out _))
            {
                Report(
                    DiagnosticCodes.ContractLayerNotAddition,
                    $"The state '{name}' sets 'protected'. Protection is the module's promise, so an added state cannot carry it.",
                    $"{path}/protected");
            }

            if (addedStates.Add(name))
            {
                states.Add((name, index));
            }
        }

        var moduleTransitions = ReadList(moduleLifecycle, TransitionsProperty);
        var transitionElements = ReadList(lifecycle, TransitionsProperty);
        for (var index = 0; index < transitionElements.Count; index++)
        {
            var element = transitionElements[index];
            var path = $"/changes/lifecycle/transitions/{index.ToString(CultureInfo.InvariantCulture)}";
            if (CheckElement(element, "transition", moduleTransitions, path, Report) is not { } name)
            {
                continue;
            }

            if (element.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.String && protectedStates.Contains(to.GetString()!))
            {
                Report(
                    DiagnosticCodes.AddedTransitionIntoProtectedState,
                    $"The transition '{name}' enters '{to.GetString()}', a protected state of the module. Only the module's own transitions enter it.",
                    $"{path}/to");
            }

            if (!transitions.Contains(name, StringComparer.Ordinal))
            {
                transitions.Add(name);
            }
        }

        return diagnostics.Count == before ? new ContractAdditions(states, transitions) : null;
    }

    /// <summary>
    /// Checks one element of a layer's list is an addition: it has a name that no module element has, ignoring
    /// letter case, and no <c>remove</c>. Returns the name of an addition, or null for any other element.
    /// </summary>
    private static string? CheckElement(
        JsonElement element,
        string what,
        IReadOnlyList<JsonElement> moduleElements,
        string path,
        Action<string, string, string> report)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            report(DiagnosticCodes.ContractLayerNotAddition, $"A {what} is an object with a name.", path);
            return null;
        }

        var name = NameOf(element);
        if (element.TryGetProperty("remove", out _))
        {
            report(
                DiagnosticCodes.ContractLayerNotAddition,
                $"A layer on a contract cannot remove a {what}. It only adds them.",
                path);
            return null;
        }

        if (name is not null && moduleElements.Any(candidate => string.Equals(NameOf(candidate), name, StringComparison.OrdinalIgnoreCase)))
        {
            report(
                DiagnosticCodes.ContractLayerNotAddition,
                $"The module has a {what} '{name}' already, ignoring letter case. A layer cannot change the module's {what}s, only add new ones.",
                path);
            return null;
        }

        return name;
    }

    private static List<JsonElement> ReadList(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray()]
            : [];

    private static string? NameOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;
}
