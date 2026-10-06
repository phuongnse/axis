using Axis.Presentation.Texts;

namespace Axis.Server.Tests;

public sealed class TextCatalogTests
{
    private const string ReferenceLocale = "en";

    private readonly EmbeddedTextResourceProvider _provider = new();

    [Fact]
    public void The_platform_ships_english_and_vietnamese()
    {
        Assert.Equal(["en", "vi"], _provider.Locales);
    }

    [Fact]
    public void Every_locale_has_the_same_keys_as_english()
    {
        var reference = _provider.GetTexts(ReferenceLocale)!.Texts.Keys.ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var locale in _provider.Locales)
        {
            var keys = _provider.GetTexts(locale)!.Texts.Keys.ToHashSet(StringComparer.Ordinal);
            var missing = reference.Except(keys).Order(StringComparer.Ordinal).ToList();
            var extra = keys.Except(reference).Order(StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
            {
                problems.Add($"Locale '{locale}' is missing keys from '{ReferenceLocale}': {string.Join(", ", missing)}");
            }
            if (extra.Count > 0)
            {
                problems.Add($"Locale '{locale}' has keys that '{ReferenceLocale}' does not have: {string.Join(", ", extra)}");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
