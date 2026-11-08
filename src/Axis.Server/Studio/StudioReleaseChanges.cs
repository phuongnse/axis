using System.Text.Json;
using Axis.Configuration.Loading;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Studio;

namespace Axis.Server.Studio;

/// <summary>
/// Compares the files of an application folder with the resources of its active release, file by file, as canonical
/// JSON, without compiling the folder. Scenarios are left out because a release never stores them, and so are the
/// modules' copies under <c>modules/</c> on both sides, which Studio shows as module rows.
/// </summary>
internal static class StudioReleaseChanges
{
    private const string Added = "added";
    private const string Changed = "changed";
    private const string Removed = "removed";

    /// <summary>
    /// The files that were added, changed or removed since the release, sorted by path. A folder file that is not valid
    /// JSON is compared by its raw text. A removed file carries its kind, name and titles from the release and its
    /// text resources, and its locale when it is a text resource. The other entries carry only the path and the change.
    /// </summary>
    public static IReadOnlyList<StudioReleaseChange> Compare(
        IReadOnlyList<StudioSnapshotFile> folder, IReadOnlyList<ResourceContent> release) =>
        Compare([.. folder.Select(file => new ResourceContent(file.File, file.Content))], release);

    /// <summary>
    /// The files of <paramref name="current"/> that were added, changed or removed since <paramref name="baseline"/>, as
    /// for a folder. A release compares with another release this way.
    /// </summary>
    public static IReadOnlyList<StudioReleaseChange> Compare(
        IReadOnlyList<ResourceContent> current, IReadOnlyList<ResourceContent> baseline)
    {
        var currentTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in current)
        {
            if (!IsModuleCopy(file.Path) && Canonical(file.Content) is { } text)
            {
                currentTexts[file.Path] = text;
            }
        }

        var live = baseline
            .Where(resource => !IsModuleCopy(resource.Path))
            .ToDictionary(resource => resource.Path, resource => resource.Content, StringComparer.Ordinal);
        var texts = TextResources(live.Values);
        var changes = new List<StudioReleaseChange>();
        foreach (var (path, text) in currentTexts)
        {
            if (!live.TryGetValue(path, out var stored))
            {
                changes.Add(new StudioReleaseChange(path, Added, null, null, null, null));
            }
            else if (!string.Equals(text, stored, StringComparison.Ordinal))
            {
                changes.Add(new StudioReleaseChange(path, Changed, null, null, null, null));
            }
        }

        foreach (var (path, stored) in live)
        {
            if (!currentTexts.ContainsKey(path))
            {
                changes.Add(Removal(path, stored, texts));
            }
        }

        changes.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return changes;
    }

    private static bool IsModuleCopy(string path) => path.StartsWith(ApplicationLoader.ModulesFolderPrefix, StringComparison.Ordinal);

    // The canonical text of a folder file, its raw text when it is not valid JSON, or null for a scenario.
    internal static string? Canonical(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return StringProperty(document.RootElement, "kind") == ResourceKinds.Scenario
                ? null
                : JsonCanonicalizer.Canonicalize(document.RootElement);
        }
        catch (JsonException)
        {
            return content;
        }
    }

    // The texts of every text resource of the release, keyed by locale.
    internal static List<(string Locale, JsonElement Texts)> TextResources(IEnumerable<string> contents)
    {
        var texts = new List<(string, JsonElement)>();
        foreach (var content in contents)
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (StringProperty(root, "kind") == ResourceKinds.Text
                && StringProperty(root, "locale") is { } locale
                && root.TryGetProperty("texts", out var values)
                && values.ValueKind == JsonValueKind.Object)
            {
                texts.Add((locale, values.Clone()));
            }
        }

        return texts;
    }

    private static StudioReleaseChange Removal(string path, string stored, List<(string Locale, JsonElement Texts)> texts)
    {
        using var document = JsonDocument.Parse(stored);
        var root = document.RootElement;
        var kind = StringProperty(root, "kind");
        var key = LabelKey(root);
        var titles = key is null ? null : Titles(key, texts);

        return new StudioReleaseChange(
            path, Removed, kind, StringProperty(root, "name"), titles,
            kind == ResourceKinds.Text ? StringProperty(root, "locale") : null);
    }

    // The text key of the label of a resource or of one element of it, or null without a label.
    internal static string? LabelKey(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.Object
            ? StringProperty(label, "textKey")
            : null;

    // The text of a key in each locale that has it.
    internal static Dictionary<string, string> Titles(string key, List<(string Locale, JsonElement Texts)> texts)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (locale, values) in texts)
        {
            if (values.TryGetProperty(key, out var text) && text.ValueKind == JsonValueKind.String && text.GetString() is { } value)
            {
                titles[locale] = value;
            }
        }

        return titles;
    }

    internal static string? StringProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
}
