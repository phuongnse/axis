using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axis.Configuration.Tests;

internal sealed partial class TemporaryFolder
{
    // Test pages name every element so customization layers can address it.
    static partial void RejectUnnamedElements(string relativePath, string content)
    {
        JsonNode? page;
        try
        {
            page = JsonNode.Parse(content);
        }
        catch (JsonException)
        {
            return;
        }

        if (page is not JsonObject { } obj
            || obj["kind"] is not JsonValue kind
            || !kind.TryGetValue(out string? kindName)
            || kindName != "page"
            || obj["widgets"] is not JsonArray)
        {
            return;
        }

        var problems = PageElementNameTests.UnnamedElements(page, relativePath.Replace('\\', '/'));
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        }
    }
}
