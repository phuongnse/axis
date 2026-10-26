using System.Reflection;
using System.Text.Json.Nodes;

namespace Axis.Configuration.Compilation;

/// <summary>
/// The grant actions each target kind takes, read once from the embedded <c>policy.schema.json</c>
/// so that the schema is their only home. The compiler, the policy matrix and Studio all take them
/// from there.
/// </summary>
internal static class PolicyActions
{
    private const string ResourceName = "Axis.Configuration.Schemas.policy.schema.json";

    private static readonly JsonNode _schema = LoadSchema();

    private static readonly Dictionary<string, IReadOnlyList<string>> _byKind = new[] { "entity", "dataSource", "process", "endpoint" }
        .ToDictionary(kind => kind, kind => Enum(_schema["$defs"]![$"{kind}Action"]!["enum"]!));

    /// <summary>Every action a grant of any target kind may name, as the schema's flat list gives them.</summary>
    public static IReadOnlyList<string> All { get; } =
        Enum(_schema["$defs"]!["grant"]!["properties"]!["actions"]!["items"]!["enum"]!);

    /// <summary>The actions a grant on a target of <paramref name="kind"/> takes, in schema order.</summary>
    public static IReadOnlyList<string> Of(string kind) => _byKind[kind];

    private static IReadOnlyList<string> Enum(JsonNode values) =>
        [.. values.AsArray().Select(value => value!.GetValue<string>())];

    private static JsonNode LoadSchema()
    {
        using var stream = typeof(PolicyActions).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{ResourceName}' was not found.");
        return JsonNode.Parse(stream)!;
    }
}
