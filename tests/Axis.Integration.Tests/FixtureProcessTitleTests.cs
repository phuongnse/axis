using System.Text.Json;

namespace Axis.Integration.Tests;

/// <summary>
/// Every process file under the fixtures has a <c>title</c> with a text key, as <c>docs/reference/resource-files.md#resource-file-shape</c>
/// says. The title is optional in the schema for now, and a later change makes it required, so this test keeps the fixtures
/// ready for it. A process is found by its <c>kind</c>, not by its folder, so a process kept outside a <c>processes</c> folder
/// is checked too.
/// </summary>
public sealed class FixtureProcessTitleTests
{
    [Fact]
    public void Every_process_file_in_the_fixtures_has_a_title()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var resource = document.RootElement;
            if (!resource.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "process")
            {
                continue;
            }

            var textKey = resource.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.Object
                && title.TryGetProperty("textKey", out var key) && key.ValueKind == JsonValueKind.String
                ? key.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(textKey))
            {
                missing.Add(Path.GetRelativePath(root, file));
            }
        }

        Assert.Empty(missing);
    }
}
