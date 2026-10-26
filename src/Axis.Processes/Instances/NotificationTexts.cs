using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;

namespace Axis.Processes.Instances;

/// <summary>
/// Chooses the texts of a notification and renders its subject and body. The in-app list and the staged emails use
/// it, so both fill a placeholder the same way: with its stored value as raw text. A string is used as it is, a number
/// or boolean as JSON writes it, null as empty text and any other value as JSON.
/// </summary>
public static class NotificationTexts
{
    /// <summary>The texts of <paramref name="locale"/> when the release has them, otherwise its first locale in ordinal order, or null when it has none.</summary>
    public static TextResource? ChooseTexts(ApplicationModel release, string? locale)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (locale is not null
            && release.Texts.FirstOrDefault(text => string.Equals(text.Locale, locale, StringComparison.Ordinal)) is { } preferred)
        {
            return preferred;
        }

        return release.Texts.OrderBy(text => text.Locale, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>The text of <paramref name="key"/> with its placeholders filled, or the key itself when the texts have none.</summary>
    public static string Render(TextResource? resource, string key, IReadOnlyDictionary<string, string> values) =>
        resource is not null && resource.Texts.TryGetValue(key, out var text) ? TextPlaceholders.Fill(text, values) : key;

    /// <summary>The values of a stored JSON object as raw text, keyed by name.</summary>
    public static Dictionary<string, string> ReadValues(string json) =>
        JsonNode.Parse(json) is JsonObject stored ? RawValues(stored) : new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The values of a JSON object as raw text, keyed by name.</summary>
    public static Dictionary<string, string> RawValues(JsonObject stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in stored)
        {
            values[name] = RawText(value);
        }

        return values;
    }

    /// <summary>One JSON value as raw text.</summary>
    internal static string RawText(JsonNode? value) =>
        value switch
        {
            null => string.Empty,
            JsonValue text when text.TryGetValue<string>(out var raw) => raw,
            _ => value.ToJsonString(),
        };
}
