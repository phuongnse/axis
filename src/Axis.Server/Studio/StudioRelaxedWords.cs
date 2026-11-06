using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Loading;
using Axis.Configuration.Modules;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;

namespace Axis.Server.Studio;

/// <summary>
/// Reads the relax entries of a stored release with their words, so a person can judge a relaxed point without its
/// technical name. The words come from the module files that the release stores: the module's label, the kind of point
/// (<see cref="Parts"/>), the entity's label and the point's label in each locale. A part without a label shows its name
/// in each locale of the module's texts, or in <c>en</c> when the module has none. A release without a copy of the module,
/// and a contract without the point, have no words, see <c>docs/reference/authoring.md#release-history</c>.
/// </summary>
internal static class StudioRelaxedWords
{
    /// <summary>The kinds of point a relax entry names, as the history answers them.</summary>
    internal static readonly string[] Parts = ["transition", "initial", "deletable", "validation", "effect", "ceiling"];

    private const string FallbackLocale = "en";

    /// <summary>
    /// The relax entries of each release that has any, in manifest order, with their words. The module copies are read only
    /// for the releases that need them.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, List<StudioRelaxedPoint>>> ReadAsync(
        ActiveReleaseStore store, IReadOnlyCollection<Guid> releaseIds, CancellationToken cancellationToken)
    {
        var manifests = await store.GetManifestsAsync(releaseIds, cancellationToken);
        var relaxing = manifests.Where(manifest => Entries(manifest.Value).Count > 0).Select(manifest => manifest.Key).ToList();
        IReadOnlyDictionary<Guid, IReadOnlyList<ResourceContent>> copies =
            relaxing.Count == 0 ? new Dictionary<Guid, IReadOnlyList<ResourceContent>>() : await store.GetModuleCopiesAsync(relaxing, cancellationToken);
        return relaxing.ToDictionary(id => id, id => Read(manifests[id], copies.GetValueOrDefault(id) ?? []));
    }

    /// <summary>
    /// The relax entries of a stored <c>application.json</c>, in manifest order, with the words that <paramref name="moduleCopies"/>
    /// give them. A missing manifest has no entries.
    /// </summary>
    public static List<StudioRelaxedPoint> Read(string? manifest, IReadOnlyList<ResourceContent> moduleCopies)
    {
        var entries = Entries(manifest);
        return entries.Count == 0 ? [] : ReadInstalled(manifest, ApplicationCompiler.ReadModuleCopies(moduleCopies, []));
    }

    /// <summary>
    /// The relax entries of an <c>application.json</c>, in manifest order, with the words that <paramref name="modules"/> give
    /// them. A module that is not among them leaves its entries without words.
    /// </summary>
    public static List<StudioRelaxedPoint> ReadInstalled(string? manifest, IReadOnlyList<InstalledModule> modules) =>
        [
            .. Entries(manifest).Select(entry => modules.FirstOrDefault(module => module.Manifest.Id == entry.Module) is { } installed
                ? Describe(entry.Module, entry.Point, entry.Reason, installed)
                : new StudioRelaxedPoint(entry.Module, entry.Point, entry.Reason, null, null, null, null)),
        ];

    /// <summary>
    /// The <c>id</c> written in the module's contract file for the entity that <paramref name="point"/> names, in the hyphenated
    /// lower-case form, or null when the module is not among <paramref name="modules"/> or has no contract for that entity.
    /// </summary>
    public static string? ContractId(IReadOnlyList<InstalledModule> modules, string module, string point)
    {
        if (modules.FirstOrDefault(item => item.Manifest.Id == module) is not { } installed
            || FindContract(installed, point.Split('/')[0]) is not { } contract)
        {
            return null;
        }

        using (contract)
        {
            return StudioReleaseChanges.StringProperty(contract.RootElement, "id") is { } id && Guid.TryParseExact(id, "D", out var guid)
                ? guid.ToString("D")
                : null;
        }
    }

    private static List<(string Module, string Point, string Reason)> Entries(string? manifest) =>
        manifest is not null && ApplicationLoader.Load([new ResourceContent("application.json", manifest)]).Application?.Modules is { } declared
            ? [.. declared.SelectMany(module => module.Relax.Select(entry => (Module: module.Id, entry.Point, Reason: entry.Reason ?? "")))]
            : [];

    private static StudioRelaxedPoint Describe(string moduleId, string point, string reason, InstalledModule module)
    {
        var moduleTitle = string.IsNullOrWhiteSpace(module.Manifest.Label) ? moduleId : module.Manifest.Label;
        var texts = StudioReleaseChanges.TextResources(module.Files.Select(file => file.Content));
        var locales = texts.Count == 0 ? [FallbackLocale] : texts.Select(text => text.Locale).Distinct(StringComparer.Ordinal).ToList();
        var segments = point.Split('/');
        var entityTitles = EntityTitles(module, segments[0], texts, locales);
        var part = PartOf(segments);
        if (part is null || FindContract(module, segments[0]) is not { } contract)
        {
            return new StudioRelaxedPoint(moduleId, point, reason, moduleTitle, null, entityTitles, null);
        }

        using (contract)
        {
            if (Find(module, contract.RootElement, part, segments) is not { } found)
            {
                return new StudioRelaxedPoint(moduleId, point, reason, moduleTitle, null, entityTitles, null);
            }

            var titles = found.Name is null ? null : Words(found.Key, found.Name, texts, locales);
            return new StudioRelaxedPoint(moduleId, point, reason, moduleTitle, part, entityTitles, titles);
        }
    }

    // The kind of point of `<entity>/<segment>/…`, or null for a point of no kind that is known.
    private static string? PartOf(string[] segments) => segments switch
    {
        [_, "transitions", _] => "transition",
        [_, "states", _, "initial"] => "initial",
        [_, "states", _, "deletable"] => "deletable",
        [_, "validations", _] => "validation",
        [_, "effects", _] => "effect",
        [_, "ceiling", _] => "ceiling",
        _ => null,
    };

    // The label key and name of the element that the point names, or null when the contract lacks it. The name is null for
    // the ceiling of roles mapped to no slot, which has no words.
    private static (string? Key, string? Name)? Find(InstalledModule module, JsonElement contract, string part, string[] segments)
    {
        var name = segments[^1];
        if (part == "ceiling")
        {
            if (!contract.TryGetProperty("ceiling", out var ceiling) || ceiling.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (name == "others")
            {
                return ceiling.TryGetProperty("others", out _) ? (null, null) : null;
            }

            return ceiling.TryGetProperty("slots", out var slots) && slots.ValueKind == JsonValueKind.Object && slots.TryGetProperty(name, out _)
                ? (module.Manifest.Roles.FirstOrDefault(role => role.Name == name)?.Label.TextKey, name)
                : null;
        }

        var (list, itemName) = part switch
        {
            "transition" => (Path(contract, "lifecycle", "transitions"), name),
            "initial" or "deletable" => (Path(contract, "lifecycle", "states"), segments[2]),
            "validation" => (Path(contract, "validations"), name),
            _ => (Path(contract, "effects"), name),
        };
        if (list is not { ValueKind: JsonValueKind.Array } items)
        {
            return null;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (StudioReleaseChanges.StringProperty(item, "name") == itemName)
            {
                return (StudioReleaseChanges.LabelKey(item), itemName);
            }
        }

        return null;
    }

    private static JsonElement? Path(JsonElement element, params string[] names)
    {
        var current = element;
        foreach (var name in names)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static JsonDocument? FindContract(InstalledModule module, string entity)
    {
        foreach (var file in module.Files)
        {
            var document = JsonDocument.Parse(file.Content);
            if (StudioReleaseChanges.StringProperty(document.RootElement, "kind") == ResourceKinds.Contract
                && StudioReleaseChanges.StringProperty(document.RootElement, "entity") == entity)
            {
                return document;
            }

            document.Dispose();
        }

        return null;
    }

    private static Dictionary<string, string>? EntityTitles(
        InstalledModule module, string entity, List<(string Locale, JsonElement Texts)> texts, List<string> locales)
    {
        foreach (var file in module.Files)
        {
            using var document = JsonDocument.Parse(file.Content);
            if (StudioReleaseChanges.StringProperty(document.RootElement, "kind") == ResourceKinds.Entity
                && StudioReleaseChanges.StringProperty(document.RootElement, "name") == entity)
            {
                return Words(StudioReleaseChanges.LabelKey(document.RootElement), entity, texts, locales);
            }
        }

        return null;
    }

    // The label's text in each locale that has it, else the name in each locale.
    private static Dictionary<string, string> Words(string? key, string? name, List<(string Locale, JsonElement Texts)> texts, List<string> locales)
    {
        var words = key is null ? [] : StudioReleaseChanges.Titles(key, texts);
        return words.Count > 0 || name is null ? words : locales.ToDictionary(locale => locale, _ => name, StringComparer.Ordinal);
    }
}
