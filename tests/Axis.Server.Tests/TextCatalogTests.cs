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
    public void Every_key_of_every_locale_exists_in_english()
    {
        var reference = _provider.GetTexts(ReferenceLocale)!.Texts;

        foreach (var locale in _provider.Locales)
        {
            var missing = _provider.GetTexts(locale)!.Texts.Keys.Where(key => !reference.ContainsKey(key)).ToList();
            Assert.True(missing.Count == 0, $"Locale '{locale}' has keys missing from '{ReferenceLocale}': {string.Join(", ", missing)}");
        }
    }
}
