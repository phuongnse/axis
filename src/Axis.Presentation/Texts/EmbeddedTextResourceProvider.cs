using System.Reflection;
using System.Text.Json;

namespace Axis.Presentation.Texts;

/// <summary>Reads the platform texts embedded as one <c>Texts/{locale}.json</c> file per locale.</summary>
public sealed class EmbeddedTextResourceProvider : ITextResourceProvider
{
    private const string ResourcePrefix = "Axis.Presentation.Texts.";
    private const string ResourceSuffix = ".json";

    private static readonly Assembly _assembly = typeof(EmbeddedTextResourceProvider).Assembly;

    private readonly Lazy<Dictionary<string, TextResources>> _textsByLocale;

    public EmbeddedTextResourceProvider()
    {
        _textsByLocale = new(() => Locales.ToDictionary(locale => locale, Load, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The locales that have embedded texts, as lowercase tags.</summary>
    public IReadOnlyList<string> Locales { get; } = _assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
        .Select(name => name[ResourcePrefix.Length..^ResourceSuffix.Length])
        .Order(StringComparer.Ordinal)
        .ToArray();

    public TextResources? GetTexts(string locale) =>
        _textsByLocale.Value.GetValueOrDefault(locale);

    private static TextResources Load(string locale)
    {
        var resourceName = $"{ResourcePrefix}{locale}{ResourceSuffix}";
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded texts '{resourceName}' were not found.");
        var texts = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Embedded texts '{resourceName}' are empty.");
        return new TextResources(locale, new Dictionary<string, string>(texts, StringComparer.Ordinal));
    }
}
