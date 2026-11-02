using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Tests;

public sealed class LayerDifferenceTests
{
    [Theory]
    [MemberData(nameof(LayerMergerTests.KeyedLists), MemberType = typeof(LayerMergerTests))]
    public void A_keyed_list_gives_a_layer_that_changes_adds_and_removes_one_element(string kind, string property, string key)
    {
        var module = $$"""{ "kind": "{{kind}}", "{{property}}": [ { "{{key}}": "a", "x": 1 }, { "{{key}}": "b", "x": 2 } ] }""";
        var edited = $$"""{ "kind": "{{kind}}", "{{property}}": [ { "{{key}}": "a", "x": 9 }, { "{{key}}": "c", "x": 3 } ] }""";

        var result = Compare(kind, module, edited);

        Assert.Null(result.Refusal);
        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse($$"""{ "{{property}}": [ { "{{key}}": "a", "x": 9 }, { "{{key}}": "c", "x": 3 }, { "{{key}}": "b", "remove": true } ] }"""),
            result.Changes));
        Assert.Equal([$"/{property}/1"], result.Added);
        Assert.Equal([$"/{property}/0", $"/{property}/0/x"], result.Changed);
        Assert.Equal([new LayerRemovedElement($"/{property}", "b")], result.Removed);
        AssertRoundTrip(kind, module, edited, result);
    }

    [Theory]
    [MemberData(nameof(LayerMergerTests.OrderedLists), MemberType = typeof(LayerMergerTests))]
    public void An_ordered_list_gives_a_layer_whose_merge_has_the_edited_order(string kind, string property, string key)
    {
        var module = $$"""{ "kind": "{{kind}}", "{{property}}": [ {{Elements(key, "a", "b", "c", "d")}} ] }""";
        var edited = $$"""{ "kind": "{{kind}}", "{{property}}": [ {{Elements(key, "d", "x", "b", "a", "y", "c")}} ] }""";

        var result = Compare(kind, module, edited);

        Assert.Null(result.Refusal);
        AssertRoundTrip(kind, module, edited, result);
        Assert.Contains(result.Added, path => path == $"/{property}/1");
        Assert.Contains(result.Added, path => path == $"/{property}/4");
    }

    [Fact]
    public void Every_order_of_an_ordered_list_with_an_added_element_at_every_position_gives_the_edited_order()
    {
        var count = 0;
        foreach (var order in Permutations(["a", "b", "c", "d"]))
        {
            for (var position = 0; position <= order.Length; position++)
            {
                var edited = order.ToList();
                edited.Insert(position, "x");
                var module = $$"""{ "kind": "entity", "fields": [ {{Elements("name", "a", "b", "c", "d")}} ] }""";
                var editedResource = $$"""{ "kind": "entity", "fields": [ {{Elements("name", [.. edited])}} ] }""";

                AssertRoundTrip("entity", module, editedResource, Compare("entity", module, editedResource));
                count++;
            }
        }

        Assert.Equal(120, count);
    }

    [Fact]
    public void A_moved_element_that_has_no_other_change_is_written_with_its_place_alone()
    {
        var module = """{ "kind": "entity", "fields": [ { "name": "a" }, { "name": "b" }, { "name": "c" } ] }""";
        var edited = """{ "kind": "entity", "fields": [ { "name": "a" }, { "name": "c" }, { "name": "b" } ] }""";

        var result = Compare("entity", module, edited);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "fields": [ { "name": "c", "after": "a" } ] }"""), result.Changes));
        Assert.Equal(["/fields/1"], result.Changed);
        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        AssertRoundTrip("entity", module, edited, result);
    }

    [Fact]
    public void An_order_that_a_layer_cannot_express_is_ignored_in_a_keyed_list_where_order_does_not_count()
    {
        var module = """{ "kind": "entity", "indexes": [ { "name": "a" }, { "name": "b" } ] }""";
        var edited = """{ "kind": "entity", "indexes": [ { "name": "b" }, { "name": "a" } ] }""";

        var result = Compare("entity", module, edited);

        Assert.Null(result.Refusal);
        Assert.Null(result.Changes);
    }

    [Fact]
    public void An_edited_copy_that_equals_the_module_gives_no_changes()
    {
        var module = """{ "id": "7f6e3b9c-2d4a-4b8e-9a51-0c3d5e7f9a12", "kind": "process", "name": "Review", "formatVersion": 1, "start": "a", "steps": [ { "name": "s", "x": 1 } ] }""";

        var result = Compare("process", module, module);

        Assert.Null(result.Refusal);
        Assert.Null(result.Changes);
        Assert.Empty(result.Added);
        Assert.Empty(result.Changed);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void Properties_objects_and_lists_without_a_key_are_replaced_whole()
    {
        var module = """{ "kind": "process", "start": "a", "deep": { "x": 1, "y": 2 }, "parameters": [ { "name": "p" } ] }""";
        var edited = """{ "kind": "process", "start": "b", "deep": { "x": 1, "y": 3, "z": 4 }, "parameters": [ { "name": "q" } ], "extra": [ 1 ] }""";

        var result = Compare("process", module, edited);

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{ "start": "b", "deep": { "y": 3, "z": 4 }, "parameters": [ { "name": "q" } ], "extra": [ 1 ] }"""),
            result.Changes));
        Assert.Equal(["/deep/z", "/extra"], result.Added);
        Assert.Equal(["/start", "/deep/y", "/parameters"], result.Changed);
        AssertRoundTrip("process", module, edited, result);
    }

    [Fact]
    public void A_nested_keyed_list_of_a_changed_element_gives_only_its_difference()
    {
        var module = """{ "kind": "process", "steps": [ { "name": "s", "outcomes": [ { "name": "ok", "to": "a" }, { "name": "no", "to": "b" } ] } ] }""";
        var edited = """{ "kind": "process", "steps": [ { "name": "s", "outcomes": [ { "name": "ok", "to": "c" } ] } ] }""";

        var result = Compare("process", module, edited);

        Assert.True(JsonNode.DeepEquals(
            JsonNode.Parse("""{ "steps": [ { "name": "s", "outcomes": [ { "name": "ok", "to": "c" }, { "name": "no", "remove": true } ] } ] }"""),
            result.Changes));
        Assert.Equal(["/steps/0", "/steps/0/outcomes/0", "/steps/0/outcomes/0/to"], result.Changed);
        Assert.Equal([new LayerRemovedElement("/steps/0/outcomes", "no")], result.Removed);
        AssertRoundTrip("process", module, edited, result);
    }

    [Theory]
    [InlineData("id", "\"7f6e3b9c-2d4a-4b8e-9a51-0c3d5e7f9a12\"", "\"7f6e3b9c-2d4a-4b8e-9a51-0c3d5e7f9a13\"")]
    [InlineData("kind", "\"process\"", "\"entity\"")]
    [InlineData("name", "\"Review\"", "\"Other\"")]
    [InlineData("formatVersion", "1", "2")]
    public void A_change_to_the_identity_of_the_resource_is_refused(string property, string moduleValue, string editedValue)
    {
        var module = Resource(property, moduleValue);
        var edited = Resource(property, editedValue);

        var result = Compare("process", module, edited);

        Assert.StartsWith($"/{property}:", result.Refusal, StringComparison.Ordinal);
        Assert.Null(result.Changes);
        Assert.Empty(result.Added);
        Assert.Empty(result.Changed);
        Assert.Empty(result.Removed);
    }

    [Fact]
    public void A_dropped_property_is_refused_at_the_root_and_inside_a_list_element()
    {
        var module = """{ "kind": "process", "start": "a", "steps": [ { "name": "s", "x": 1 } ] }""";

        var root = Compare("process", module, """{ "kind": "process", "steps": [ { "name": "s", "x": 1 } ] }""");
        var element = Compare("process", module, """{ "kind": "process", "start": "a", "steps": [ { "name": "s" } ] }""");

        Assert.StartsWith("/start:", root.Refusal, StringComparison.Ordinal);
        Assert.StartsWith("/steps/0/x:", element.Refusal, StringComparison.Ordinal);
        Assert.Null(element.Changes);
    }

    [Fact]
    public void A_keyed_element_without_a_key_is_refused()
    {
        var module = """{ "kind": "process", "steps": [ { "name": "a" } ] }""";

        var edited = Compare("process", module, """{ "kind": "process", "steps": [ { "name": "a" }, { "x": 1 } ] }""");
        var fromModule = Compare("process", """{ "kind": "process", "steps": [ { "x": 1 } ] }""", """{ "kind": "process", "steps": [ { "name": "a" } ] }""");

        Assert.StartsWith("/steps/1/name:", edited.Refusal, StringComparison.Ordinal);
        Assert.StartsWith("/steps/0/name:", fromModule.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_repeated_key_is_refused_ignoring_letter_case()
    {
        var module = """{ "kind": "process", "steps": [ { "name": "a" } ] }""";

        var result = Compare("process", module, """{ "kind": "process", "steps": [ { "name": "a" }, { "name": "A" } ] }""");

        Assert.StartsWith("/steps/1/name:", result.Refusal, StringComparison.Ordinal);
        Assert.Null(result.Changes);
    }

    [Fact]
    public void A_key_whose_letter_case_alone_changed_is_refused()
    {
        var module = """{ "kind": "process", "steps": [ { "name": "a" } ] }""";

        var result = Compare("process", module, """{ "kind": "process", "steps": [ { "name": "A" } ] }""");

        Assert.StartsWith("/steps/0/name:", result.Refusal, StringComparison.Ordinal);
        Assert.Null(result.Changes);
    }

    [Theory]
    [InlineData("after")]
    [InlineData("before")]
    public void An_element_of_an_ordered_list_that_holds_a_place_property_is_refused(string property)
    {
        var module = """{ "kind": "process", "steps": [ { "name": "a" } ] }""";

        var result = Compare("process", module, $$"""{ "kind": "process", "steps": [ { "name": "a", "{{property}}": "b" } ] }""");

        Assert.StartsWith($"/steps/0/{property}:", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_string_element_that_must_move_is_refused()
    {
        var module = """{ "kind": "process", "steps": [ "a", "b" ] }""";

        var result = Compare("process", module, """{ "kind": "process", "steps": [ "b", "a" ] }""");

        Assert.StartsWith("/steps/", result.Refusal, StringComparison.Ordinal);
    }

    private static string Resource(string changed, string value)
    {
        var properties = new Dictionary<string, string>
        {
            ["id"] = "\"7f6e3b9c-2d4a-4b8e-9a51-0c3d5e7f9a12\"",
            ["kind"] = "\"process\"",
            ["name"] = "\"Review\"",
            ["formatVersion"] = "1",
        };
        properties[changed] = value;
        return "{ " + string.Join(", ", properties.Select(pair => $"\"{pair.Key}\": {pair.Value}")) + " }";
    }

    private static string Elements(string key, params string[] keys) =>
        string.Join(", ", keys.Select(name => $$"""{ "{{key}}": "{{name}}" }"""));

    private static IEnumerable<string[]> Permutations(string[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var index = 0; index < items.Length; index++)
        {
            var rest = items.Where((_, other) => other != index).ToArray();
            foreach (var tail in Permutations(rest))
            {
                yield return [items[index], .. tail];
            }
        }
    }

    private static LayerDifferenceResult Compare(string kind, string module, string edited)
    {
        using var moduleDocument = JsonDocument.Parse(module);
        using var editedDocument = JsonDocument.Parse(edited);
        return LayerDifference.Compare(kind, moduleDocument.RootElement, editedDocument.RootElement);
    }

    private static void AssertRoundTrip(string kind, string module, string edited, LayerDifferenceResult result)
    {
        Assert.NotNull(result.Changes);
        using var moduleDocument = JsonDocument.Parse(module);
        var layer = new LayerResource
        {
            Id = Guid.NewGuid(),
            Kind = "layer",
            FormatVersion = 1,
            Target = "m/R",
            Changes = JsonSerializer.SerializeToElement(result.Changes),
            File = "customizations/m/R.json",
        };
        var diagnostics = new List<Diagnostic>();

        var merged = LayerMerger.Merge(kind, moduleDocument.RootElement, layer, diagnostics).Merged;

        Assert.Empty(diagnostics);
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(edited), merged),
            $"Merged {merged.ToJsonString()} but expected {edited}.");
    }
}
