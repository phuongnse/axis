using System.Text.Json;

namespace Axis.Configuration.Tests;

public sealed class SampleTemplateTests
{
    private static readonly string _folder = Path.Combine(AppContext.BaseDirectory, "SampleTemplates", "request-approval");

    [Fact]
    public void Every_text_key_of_the_request_approval_template_is_in_en_and_vi()
    {
        var en = Texts("en");
        var vi = Texts("vi");

        Assert.Equal(en.Keys.Order(StringComparer.Ordinal), vi.Keys.Order(StringComparer.Ordinal));
        Assert.All(en.Concat(vi), text => Assert.False(string.IsNullOrWhiteSpace(text.Value), $"The text '{text.Key}' is blank."));

        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(_folder, "*.json", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name is "template.json" or "en.json" or "vi.json")
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(file));
            CollectTextKeys(document.RootElement, used);
        }

        Assert.NotEmpty(used);
        Assert.All(used, key =>
        {
            Assert.True(en.ContainsKey(key), $"The text key '{key}' is missing in en.");
            Assert.True(vi.ContainsKey(key), $"The text key '{key}' is missing in vi.");
        });
    }

    private static Dictionary<string, string> Texts(string locale)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(_folder, "texts", $"{locale}.json")));
        return document.RootElement.GetProperty("texts").EnumerateObject().ToDictionary(text => text.Name, text => text.Value.GetString()!);
    }

    private static void CollectTextKeys(JsonElement element, ISet<string> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "textKey" && property.Value.ValueKind == JsonValueKind.String)
                    {
                        keys.Add(property.Value.GetString()!);
                    }
                    else
                    {
                        CollectTextKeys(property.Value, keys);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectTextKeys(item, keys);
                }

                break;
        }
    }
}
