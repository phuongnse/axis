using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Loading;

namespace Axis.Configuration.Tests;

/// <summary>Pins which resource kinds take a <c>description</c>, in their schemas and in <c>resource-files.md</c>.</summary>
public sealed class ResourceDescriptionSchemaTests
{
    private static readonly string[] _withoutDescription = ["text", "layer"];

    private const string Description = """, "description": { "textKey": "x" }""";

    private const string Role = """
        { "id": "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c53", "kind": "role", "name": "finance", "formatVersion": 1,
          "label": { "textKey": "role.finance" }
        """;

    private const string Text = """
        { "id": "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c53", "kind": "text", "name": "TextsEn", "formatVersion": 1,
          "locale": "en", "texts": { "a": "A" }
        """;

    private const string Layer = """
        { "id": "6a1d2f0e-7a3b-4c8d-9e21-3b6a8f4d1c53", "kind": "layer", "formatVersion": 1,
          "target": "approvals/Request", "changes": {}
        """;

    public static TheoryData<string> DescribedKinds() =>
        [.. ResourceSchemas.Kinds.Except(_withoutDescription, StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(DescribedKinds))]
    public void A_kind_but_text_and_layer_has_an_optional_description_label(string kind)
    {
        Assert.True(ResourceSchemas.TryGetText(kind, out var text));
        var schema = JsonNode.Parse(text)!;

        var description = schema["properties"]!["description"];
        Assert.NotNull(description);
        Assert.Equal("#/$defs/textReference", description["$ref"]!.GetValue<string>());
        Assert.Equal("textKey", Assert.Single(schema["$defs"]!["textReference"]!["required"]!.AsArray())!.GetValue<string>());
        Assert.DoesNotContain(schema["required"]!.AsArray(), name => name!.GetValue<string>() == "description");
    }

    [Fact]
    public void A_role_accepts_a_description_and_works_without_one()
    {
        Assert.True(IsValid("role", Role + " }"));
        Assert.True(IsValid("role", Role + Description + " }"));
    }

    [Fact]
    public void A_text_resource_and_a_layer_refuse_a_description()
    {
        Assert.True(IsValid("text", Text + " }"));
        Assert.False(IsValid("text", Text + Description + " }"));
        Assert.True(IsValid("layer", Layer + " }"));
        Assert.False(IsValid("layer", Layer + Description + " }"));
    }

    [Fact]
    public void The_kinds_the_doc_says_take_a_description_are_the_kinds_whose_schema_has_one()
    {
        var line = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "reference", "resource-files.md"))
            .Where(candidate => candidate.StartsWith("- **`description`", StringComparison.Ordinal))
            .ToList();
        Assert.True(line.Count == 1, $"Expected one bullet that starts with '- **`description`', found {line.Count}.");
        var sentence = line[0];
        var excluded = sentence[sentence.IndexOf(" but ", StringComparison.Ordinal)..sentence.IndexOf(" may ", StringComparison.Ordinal)]
            .Split('`')
            .Where((_, index) => index % 2 == 1)
            .ToList();

        var documented = ResourceSchemas.Kinds.Except(excluded, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var defined = ResourceSchemas.Kinds
            .Where(kind =>
            {
                Assert.True(ResourceSchemas.TryGetText(kind, out var text));
                return JsonNode.Parse(text)!["properties"]!["description"] is not null;
            })
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(defined, documented);
    }

    private static bool IsValid(string kind, string json)
    {
        Assert.True(ResourceSchemas.TryGet(kind, out var schema));
        using var document = JsonDocument.Parse(json);
        return schema.Evaluate(document.RootElement).IsValid;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }
}
