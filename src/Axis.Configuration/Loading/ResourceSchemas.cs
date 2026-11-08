using System.Reflection;
using System.Text.Json.Nodes;
using Axis.Configuration.Resources;
using Json.Schema;

namespace Axis.Configuration.Loading;

/// <summary>The embedded JSON Schema for each resource kind.</summary>
internal static class ResourceSchemas
{
    private static readonly Dictionary<string, string> _fileNamesByKind = new(StringComparer.Ordinal)
    {
        [ResourceKinds.Application] = "application.schema.json",
        [ResourceKinds.Entity] = "entity.schema.json",
        [ResourceKinds.Site] = "site.schema.json",
        [ResourceKinds.Page] = "page.schema.json",
        [ResourceKinds.Text] = "text.schema.json",
        [ResourceKinds.Seed] = "seed.schema.json",
        [ResourceKinds.DataSource] = "dataSource.schema.json",
        [ResourceKinds.Rule] = "rule.schema.json",
        [ResourceKinds.Sequence] = "sequence.schema.json",
        [ResourceKinds.Process] = "process.schema.json",
        [ResourceKinds.Form] = "form.schema.json",
        [ResourceKinds.Role] = "role.schema.json",
        [ResourceKinds.Policy] = "policy.schema.json",
        [ResourceKinds.Connector] = "connector.schema.json",
        [ResourceKinds.IdentityProvider] = "identityProvider.schema.json",
        [ResourceKinds.Event] = "event.schema.json",
        [ResourceKinds.Endpoint] = "endpoint.schema.json",
        [ResourceKinds.Trigger] = "trigger.schema.json",
        [ResourceKinds.Webhook] = "webhook.schema.json",
        [ResourceKinds.Migration] = "migration.schema.json",
        [ResourceKinds.Scenario] = "scenario.schema.json",
        [ResourceKinds.Contract] = "contract.schema.json",
        [ResourceKinds.Layer] = "layer.schema.json",
    };

    private static readonly Dictionary<string, JsonSchema> _schemasByKind =
        _fileNamesByKind.ToDictionary(entry => entry.Key, entry => Load(entry.Value), StringComparer.Ordinal);

    /// <summary>
    /// The trigger schema for a module file. A module schedule runs in the time zone of each application that
    /// uses the module, D103, so it leaves out <c>timeZone</c> and the schema forbids it.
    /// </summary>
    public static JsonSchema ModuleTrigger { get; } = LoadModuleTrigger();

    /// <summary>The resource kinds that have an embedded schema.</summary>
    public static IReadOnlyCollection<string> Kinds => _fileNamesByKind.Keys;

    public static bool TryGet(string kind, out JsonSchema schema) =>
        _schemasByKind.TryGetValue(kind, out schema!);

    /// <summary>The text of the embedded schema of a kind, as the file holds it.</summary>
    public static bool TryGetText(string kind, out string text)
    {
        if (_fileNamesByKind.TryGetValue(kind, out var fileName))
        {
            text = ReadText(fileName);
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static JsonSchema Load(string fileName) => JsonSchema.FromText(ReadText(fileName));

    private static JsonSchema LoadModuleTrigger()
    {
        var schema = JsonNode.Parse(ReadText("trigger.schema.json"))!;
        var then = schema["allOf"]!.AsArray()
            .FirstOrDefault(entry => entry?["if"]?["properties"]?["type"]?["const"]?.GetValue<string>() == "schedule")?["then"]
            ?? throw new InvalidOperationException("The trigger schema has no branch for a schedule trigger.");
        then["required"] = new JsonArray("schedule");
        then["properties"]!["timeZone"] = false;
        return JsonSchema.FromText(schema.ToJsonString());
    }

    private static string ReadText(string fileName)
    {
        var resourceName = $"Axis.Configuration.Schemas.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
