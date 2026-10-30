using System.Reflection;
using System.Text.Json.Nodes;
using Axis.Configuration.Resources;
using Json.Schema;

namespace Axis.Configuration.Loading;

/// <summary>The embedded JSON Schema for each resource kind.</summary>
internal static class ResourceSchemas
{
    private static readonly Dictionary<string, JsonSchema> _schemasByKind = new(StringComparer.Ordinal)
    {
        [ResourceKinds.Application] = Load("application.schema.json"),
        [ResourceKinds.Entity] = Load("entity.schema.json"),
        [ResourceKinds.Site] = Load("site.schema.json"),
        [ResourceKinds.Page] = Load("page.schema.json"),
        [ResourceKinds.Text] = Load("text.schema.json"),
        [ResourceKinds.Seed] = Load("seed.schema.json"),
        [ResourceKinds.DataSource] = Load("dataSource.schema.json"),
        [ResourceKinds.Rule] = Load("rule.schema.json"),
        [ResourceKinds.Sequence] = Load("sequence.schema.json"),
        [ResourceKinds.Process] = Load("process.schema.json"),
        [ResourceKinds.Form] = Load("form.schema.json"),
        [ResourceKinds.Role] = Load("role.schema.json"),
        [ResourceKinds.Policy] = Load("policy.schema.json"),
        [ResourceKinds.Connector] = Load("connector.schema.json"),
        [ResourceKinds.IdentityProvider] = Load("identityProvider.schema.json"),
        [ResourceKinds.Event] = Load("event.schema.json"),
        [ResourceKinds.Endpoint] = Load("endpoint.schema.json"),
        [ResourceKinds.Trigger] = Load("trigger.schema.json"),
        [ResourceKinds.Webhook] = Load("webhook.schema.json"),
        [ResourceKinds.Migration] = Load("migration.schema.json"),
        [ResourceKinds.Scenario] = Load("scenario.schema.json"),
    };

    /// <summary>
    /// The trigger schema for a module file. A module schedule runs in the time zone of each application that
    /// uses the module, D103, so it leaves out <c>timeZone</c> and the schema forbids it.
    /// </summary>
    public static JsonSchema ModuleTrigger { get; } = LoadModuleTrigger();

    public static bool TryGet(string kind, out JsonSchema schema) =>
        _schemasByKind.TryGetValue(kind, out schema!);

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
