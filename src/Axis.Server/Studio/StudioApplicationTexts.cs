using System.Text.Json;

namespace Axis.Server.Studio;

/// <summary>
/// Reads the title and the description of an application by locale from its folder's JSON files, without
/// compiling the folder. The manifest's <c>label</c> and <c>description</c> name a text key, and each text
/// resource gives the text of that key for its locale.
/// </summary>
internal static class StudioApplicationTexts
{
    private const string ManifestFile = "application.json";

    /// <summary>
    /// The titles and the descriptions of <paramref name="folder"/>, each keyed by locale. A locale whose text
    /// resource has no string for the key is left out. Both are empty when the manifest names no key or cannot be read.
    /// </summary>
    public static (IReadOnlyDictionary<string, string> Titles, IReadOnlyDictionary<string, string> Descriptions) Read(string folder)
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var (titleKey, descriptionKey) = ReadKeys(folder);
            if (titleKey is null && descriptionKey is null)
            {
                return (titles, descriptions);
            }

            foreach (var (file, locale) in StudioWrites.TextFiles(folder))
            {
                using var document = JsonDocument.Parse(file.Bytes);
                if (!document.RootElement.TryGetProperty("texts", out var texts) || texts.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                Add(titles, texts, titleKey, locale);
                Add(descriptions, texts, descriptionKey, locale);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A folder that cannot be read gives what was read so far, and its name alone when nothing was.
        }

        return (titles, descriptions);
    }

    private static (string? TitleKey, string? DescriptionKey) ReadKeys(string folder)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, ManifestFile)));
        return document.RootElement.ValueKind == JsonValueKind.Object
            ? (TextKey(document.RootElement, "label"), TextKey(document.RootElement, "description"))
            : (null, null);
    }

    private static string? TextKey(JsonElement manifest, string property) =>
        manifest.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("textKey", out var key)
            && key.ValueKind == JsonValueKind.String
                ? key.GetString()
                : null;

    private static void Add(Dictionary<string, string> target, JsonElement texts, string? key, string locale)
    {
        if (key is not null
            && texts.TryGetProperty(key, out var text)
            && text.ValueKind == JsonValueKind.String
            && text.GetString() is { } value)
        {
            target[locale] = value;
        }
    }
}
