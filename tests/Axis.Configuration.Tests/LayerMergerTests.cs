using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Tests;

public sealed class LayerMergerTests
{
    public static TheoryData<string, string, string> KeyedLists()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var ((kind, property), key) in ListKeys.Entries.Where(entry => entry.Value.Mode == ListKeyMode.Keyed))
        {
            data.Add(kind, property, key.Key!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(KeyedLists))]
    public void A_keyed_list_changes_one_element_adds_one_and_removes_one(string kind, string property, string key)
    {
        using var module = JsonDocument.Parse($$"""
            { "kind": "{{kind}}", "{{property}}": [ { "{{key}}": "a", "x": 1 }, { "{{key}}": "b", "x": 2 } ] }
            """);
        var layer = Layer($$"""
            { "{{property}}": [ { "{{key}}": "A", "x": 9 }, { "{{key}}": "c", "x": 3 }, { "{{key}}": "b", "remove": true } ] }
            """);
        var diagnostics = new List<Diagnostic>();

        var result = LayerMerger.Merge(kind, module.RootElement, layer, diagnostics);

        Assert.Empty(diagnostics);
        var list = result.Merged[property]!.AsArray();
        Assert.Equal(
            [("a", 9), ("c", 3)],
            list.Select(element => (element![key]!.GetValue<string>(), element["x"]!.GetValue<int>())));
        Assert.Equal("/changes/" + property + "/0/x", result.LayerPathByMergedPath[$"/{property}/0/x"]);
        Assert.Equal("/changes/" + property + "/1/x", result.LayerPathByMergedPath[$"/{property}/1/x"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_list_without_a_key_is_replaced_whole(bool pending)
    {
        var mode = pending ? ListKeyMode.Pending : ListKeyMode.Value;
        var (kind, property) = ListKeys.Entries.First(entry => entry.Value.Mode == mode).Key;
        using var module = JsonDocument.Parse($$"""{ "kind": "{{kind}}", "{{property}}": [ { "name": "a", "x": 1 }, { "name": "b", "x": 2 } ] }""");
        var layer = Layer($$"""{ "{{property}}": [ { "name": "c", "x": 3 } ] }""");

        var result = LayerMerger.Merge(kind, module.RootElement, layer, []);

        var list = result.Merged[property]!.AsArray();
        Assert.Equal("c", Assert.Single(list)!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Objects_merge_property_by_property_and_the_identity_of_the_root_never_changes()
    {
        using var module = JsonDocument.Parse("""{ "kind": "process", "name": "Review", "start": "a", "deep": { "x": 1, "y": 2 } }""");
        var layer = Layer("""{ "name": "Other", "start": "b", "deep": { "y": 3 } }""");

        var result = LayerMerger.Merge("process", module.RootElement, layer, []);

        Assert.Equal("""{"kind":"process","name":"Review","start":"b","deep":{"x":1,"y":3}}""", result.Merged.ToJsonString());
    }

    private static LayerResource Layer(string changes)
    {
        using var document = JsonDocument.Parse(changes);
        return new LayerResource
        {
            Id = Guid.NewGuid(),
            Kind = "layer",
            FormatVersion = 1,
            Target = "m/R",
            Changes = document.RootElement.Clone(),
            File = "customizations/m/R.json",
        };
    }
}
