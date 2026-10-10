using System.Reflection;
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
    };

    public static bool TryGet(string kind, out JsonSchema schema) =>
        _schemasByKind.TryGetValue(kind, out schema!);

    private static JsonSchema Load(string fileName)
    {
        var resourceName = $"Axis.Configuration.Schemas.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
