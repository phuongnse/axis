using System.Text.Json;

namespace Axis.Server.Studio;

/// <summary>
/// Reads the label of each resource by locale from a folder's JSON files, without compiling the folder. An entity,
/// a role, an identity provider and the manifest name a text key in <c>label</c>, a page and a site in <c>title</c>,
/// and each text resource gives the text of that key for its locale.
/// </summary>
internal static class StudioResourceTitles
{
    private static readonly IReadOnlyDictionary<string, string> _noTitles = new Dictionary<string, string>();

    /// <summary>
    /// The titles and the locale of each file of <paramref name="files"/>, keyed by the file's id. The titles are
    /// keyed by locale, and a locale whose text resource has no string for the key is left out. They are empty for a
    /// kind that has no label or title, or a file that cannot be read. The locale is that of a text resource, else null.
    /// </summary>
    public static IReadOnlyDictionary<Guid, (IReadOnlyDictionary<string, string> Titles, string? Locale)> Read(
        IReadOnlyList<StudioFile> files)
    {
        var texts = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var documents = new List<JsonDocument>();
        try
        {
            var locales = new Dictionary<Guid, string>();
            foreach (var file in files.Where(file => file.Kind == "text"))
            {
                if (Parse(file, documents) is { ValueKind: JsonValueKind.Object } root
                    && root.TryGetProperty("locale", out var locale)
                    && locale.ValueKind == JsonValueKind.String
                    && locale.GetString() is { } tag)
                {
                    locales[file.Id] = tag;
                    if (root.TryGetProperty("texts", out var entries)
                        && entries.ValueKind == JsonValueKind.Object
                        && !texts.ContainsKey(tag))
                    {
                        texts[tag] = entries;
                    }
                }
            }

            var result = new Dictionary<Guid, (IReadOnlyDictionary<string, string>, string?)>();
            foreach (var file in files)
            {
                var titles = _noTitles;
                if (TitleProperty(file.Kind) is { } property && Parse(file, documents) is { ValueKind: JsonValueKind.Object } root)
                {
                    titles = Titles(root, property, texts);
                }

                result[file.Id] = (titles, locales.GetValueOrDefault(file.Id));
            }

            return result;
        }
        finally
        {
            foreach (var document in documents)
            {
                document.Dispose();
            }
        }
    }

    private static string? TitleProperty(string kind) => kind switch
    {
        "entity" or "role" or "identityProvider" or "application" => "label",
        "page" or "site" => "title",
        _ => null,
    };

    private static JsonElement? Parse(StudioFile file, List<JsonDocument> documents)
    {
        try
        {
            var document = JsonDocument.Parse(file.Bytes);
            documents.Add(document);
            return document.RootElement;
        }
        catch (JsonException)
        {
            // A file that is not JSON has no label. The list names it by its kind.
            return null;
        }
    }

    private static IReadOnlyDictionary<string, string> Titles(
        JsonElement root, string property, Dictionary<string, JsonElement> texts)
    {
        if (!root.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("textKey", out var keyValue)
            || keyValue.ValueKind != JsonValueKind.String
            || keyValue.GetString() is not { } key)
        {
            return _noTitles;
        }

        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (locale, entries) in texts)
        {
            if (entries.TryGetProperty(key, out var text) && text.ValueKind == JsonValueKind.String && text.GetString() is { } title)
            {
                titles[locale] = title;
            }
        }

        return titles;
    }
}
