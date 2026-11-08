using System.Text.Json.Nodes;
using Axis.Configuration.Loading;
using Axis.Configuration.Resources;

namespace Axis.Server.Mcp;

/// <summary>
/// The reference of the <c>read_reference</c> tool, see <c>docs/reference/mcp-server.md#reference-topics</c>. A topic is a
/// resource kind. The answer holds the JSON Schema that the configuration pipeline checks the kind against and the text of the
/// reference pages that describe it. Both are embedded in the server at build time, so an agent reads the rules of the version it
/// talks to. An unknown topic is a fixed text that never holds the topic or a path.
/// </summary>
internal static class McpReference
{
    internal const string NoTopic = "Axis has no reference for this topic.";

    private const string ResourcePrefix = "Axis.Server.Reference.";

    /// <summary>The page file names of each topic, in the order the answer lists them.</summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Pages = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        [ResourceKinds.Application] = ["resource-files.md"],
        [ResourceKinds.Entity] = ["resource-files.md"],
        [ResourceKinds.Site] = ["resource-files.md"],
        [ResourceKinds.Page] = ["resource-files.md", "frontend.md"],
        [ResourceKinds.Text] = ["resource-files.md"],
        [ResourceKinds.Seed] = ["resource-files.md"],
        [ResourceKinds.DataSource] = ["data-sources.md"],
        [ResourceKinds.Rule] = ["resource-files.md"],
        [ResourceKinds.Sequence] = ["resource-files.md"],
        [ResourceKinds.Process] = ["processes.md", "process-flow.md"],
        [ResourceKinds.Form] = ["form-widget.md", "form-conditions.md"],
        [ResourceKinds.Role] = ["roles-and-policies.md"],
        [ResourceKinds.Policy] = ["roles-and-policies.md"],
        [ResourceKinds.Connector] = ["integration-resources.md"],
        [ResourceKinds.IdentityProvider] = ["identity-provider.md"],
        [ResourceKinds.Event] = ["integration-resources.md"],
        [ResourceKinds.Endpoint] = ["integration-resources.md"],
        [ResourceKinds.Trigger] = ["integration-resources.md"],
        [ResourceKinds.Webhook] = ["outbound-webhooks.md"],
        [ResourceKinds.Migration] = ["migrations.md", "resource-files.md"],
        [ResourceKinds.Scenario] = ["test-scenarios.md"],
        [ResourceKinds.Contract] = ["module-contracts.md"],
        [ResourceKinds.Layer] = ["customization-layers.md"],
    };

    /// <summary>The file names of the reference pages embedded in the server.</summary>
    internal static IEnumerable<string> EmbeddedPages() =>
        typeof(McpReference).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name => name[ResourcePrefix.Length..]);

    /// <summary>The schema and the pages of a topic, or the fixed text when the topic is unknown.</summary>
    public static (object? Output, string? Error) Read(string topic)
    {
        if (!Pages.TryGetValue(topic, out var pages) || !ResourceSchemas.TryGetText(topic, out var schema))
        {
            return (null, NoTopic);
        }

        return (new { topic, schema = JsonNode.Parse(schema), pages = pages.Select(page => new { page, text = ReadPage(page) }).ToList() }, null);
    }

    private static string ReadPage(string page)
    {
        var name = ResourcePrefix + page;
        using var stream = typeof(McpReference).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded reference page '{name}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
