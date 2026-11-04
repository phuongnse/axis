using System.Text.Json;
using System.Text.Json.Nodes;
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
        Assert.Equal("/changes/" + property + "/1", result.LayerPathByMergedPath[$"/{property}/1"]);
        Assert.Equal("/changes/" + property + "/0", result.ChangedPathByMergedPath[$"/{property}/0"]);
        Assert.Equal("/changes/" + property, result.ChangedPathByMergedPath[$"/{property}"]);
        Assert.Equal([new LayerRemove("b", "/changes/" + property + "/2")], result.Removes);
    }

    [Fact]
    public void A_list_without_a_key_is_replaced_whole()
    {
        var (kind, property) = ListKeys.Entries.First(entry => entry.Value.Mode == ListKeyMode.Value).Key;
        using var module = JsonDocument.Parse($$"""{ "kind": "{{kind}}", "{{property}}": [ { "name": "a", "x": 1 }, { "name": "b", "x": 2 } ] }""");
        var layer = Layer($$"""{ "{{property}}": [ { "name": "c", "x": 3 } ] }""");

        var result = LayerMerger.Merge(kind, module.RootElement, layer, []);

        var list = result.Merged[property]!.AsArray();
        Assert.Equal("c", Assert.Single(list)!["name"]!.GetValue<string>());
    }

    [Fact]
    public void A_section_field_moves_after_another_by_the_sections_name_and_the_other_sections_stay()
    {
        using var module = JsonDocument.Parse("""
            { "kind": "form", "sections": [
              { "name": "request", "fields": [ { "field": "title" }, { "field": "amount" }, { "field": "owner" } ] },
              { "name": "decision", "fields": [ { "field": "comments" } ] } ] }
            """);
        var layer = Layer("""{ "sections": [ { "name": "REQUEST", "fields": [ { "field": "owner", "after": "title" } ] } ] }""");
        var diagnostics = new List<Diagnostic>();

        var result = LayerMerger.Merge("form", module.RootElement, layer, diagnostics);

        Assert.Empty(diagnostics);
        var sections = result.Merged["sections"]!.AsArray();
        Assert.Equal(
            ["title", "owner", "amount"],
            sections[0]!["fields"]!.AsArray().Select(field => field!["field"]!.GetValue<string>()));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(module.RootElement.GetProperty("sections")[1].GetRawText()), sections[1]));
        Assert.DoesNotContain("after", result.Merged.ToJsonString());
        Assert.Equal("/changes/sections/0/fields/0", result.ChangedPathByMergedPath["/sections/0/fields/1"]);
    }

    [Fact]
    public void A_steps_section_names_merge_by_the_name_itself()
    {
        using var module = JsonDocument.Parse("""
            { "kind": "form", "steps": [ { "name": "one", "sections": [ "a", "b" ] } ] }
            """);
        var layer = Layer("""{ "steps": [ { "name": "one", "sections": [ "B", "c", { "name": "a", "remove": true } ] } ] }""");
        var diagnostics = new List<Diagnostic>();

        var result = LayerMerger.Merge("form", module.RootElement, layer, diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            ["b", "c"],
            result.Merged["steps"]![0]!["sections"]!.AsArray().Select(name => name!.GetValue<string>()));
    }

    [Fact]
    public void Objects_merge_property_by_property_and_the_identity_of_the_root_never_changes()
    {
        using var module = JsonDocument.Parse("""{ "kind": "process", "name": "Review", "start": "a", "deep": { "x": 1, "y": 2 } }""");
        var layer = Layer("""{ "name": "Other", "start": "b", "deep": { "y": 3 } }""");

        var result = LayerMerger.Merge("process", module.RootElement, layer, []);

        Assert.Equal("""{"kind":"process","name":"Review","start":"b","deep":{"x":1,"y":3}}""", result.Merged.ToJsonString());
    }

    public static TheoryData<string, string, string> OrderedLists()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var ((kind, property), key) in ListKeys.Entries.Where(entry => entry.Value.Ordered))
        {
            data.Add(kind, property, key.Key!);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(OrderedLists))]
    public void An_element_of_an_ordered_list_is_placed_after_or_before_its_anchor(string kind, string property, string key)
    {
        using var module = JsonDocument.Parse($$"""
            { "kind": "{{kind}}", "{{property}}": [ { "{{key}}": "a" }, { "{{key}}": "b" }, { "{{key}}": "c" } ] }
            """);
        var layer = Layer($$"""
            { "{{property}}": [ { "{{key}}": "x", "after": "a" }, { "{{key}}": "y" }, { "{{key}}": "c", "before": "a", "v": 1 } ] }
            """);
        var diagnostics = new List<Diagnostic>();

        var result = LayerMerger.Merge(kind, module.RootElement, layer, diagnostics);

        Assert.Empty(diagnostics);
        var list = result.Merged[property]!.AsArray();
        Assert.Equal(["c", "a", "x", "b", "y"], list.Select(element => element![key]!.GetValue<string>()));
        Assert.Equal(1, list[0]!["v"]!.GetValue<int>());
        Assert.All(list, element =>
        {
            Assert.False(element!.AsObject().ContainsKey("after"));
            Assert.False(element.AsObject().ContainsKey("before"));
        });
        Assert.Equal($"/changes/{property}/2/v", result.LayerPathByMergedPath[$"/{property}/0/v"]);
        Assert.Equal($"/changes/{property}/0/{key}", result.LayerPathByMergedPath[$"/{property}/2/{key}"]);
        Assert.DoesNotContain(result.LayerPathByMergedPath.Keys, path => path.EndsWith("/after", StringComparison.Ordinal) || path.EndsWith("/before", StringComparison.Ordinal));
    }

    [Fact]
    public void An_added_form_field_lands_right_behind_its_anchor_and_one_without_either_goes_last()
    {
        using var module = JsonDocument.Parse("""{ "kind": "form", "fields": [ { "field": "title" }, { "field": "amount" }, { "field": "owner" } ] }""");
        var layer = Layer("""{ "fields": [ { "field": "note" }, { "field": "reason", "after": "TITLE" } ] }""");
        var diagnostics = new List<Diagnostic>();

        var result = LayerMerger.Merge("form", module.RootElement, layer, diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            ["title", "reason", "amount", "owner", "note"],
            result.Merged["fields"]!.AsArray().Select(element => element!["field"]!.GetValue<string>()));
    }

    [Fact]
    public void A_module_widget_moves_before_its_anchor_inside_a_tab_and_keeps_its_other_changes()
    {
        using var module = JsonDocument.Parse("""
            { "kind": "page", "widgets": [ { "name": "tabs", "type": "tabs", "tabs": [
                { "name": "t", "widgets": [ { "name": "w1", "type": "text" }, { "name": "w2", "type": "text" } ] } ] } ] }
            """);
        var layer = Layer("""
            { "widgets": [ { "name": "tabs", "tabs": [ { "name": "t", "widgets": [ { "name": "w2", "before": "w1", "type": "form" } ] } ] } ] }
            """);
        var diagnostics = new List<Diagnostic>();

        var result = LayerMerger.Merge("page", module.RootElement, layer, diagnostics);

        Assert.Empty(diagnostics);
        var widgets = result.Merged["widgets"]![0]!["tabs"]![0]!["widgets"]!.AsArray();
        Assert.Equal([("w2", "form"), ("w1", "text")], widgets.Select(widget => (widget!["name"]!.GetValue<string>(), widget["type"]!.GetValue<string>())));
        Assert.False(widgets[0]!.AsObject().ContainsKey("before"));
    }

    [Fact]
    public void A_placement_with_both_after_and_before_is_reported_and_one_with_an_unknown_anchor_too()
    {
        using var module = JsonDocument.Parse("""{ "kind": "process", "steps": [ { "name": "a" }, { "name": "b" } ] }""");
        var layer = Layer("""
            { "steps": [ { "name": "x", "after": "a", "before": "b" }, { "name": "y", "after": "nope" }, { "name": "z", "before": 3 }, { "name": "b", "after": "b" } ] }
            """);
        var diagnostics = new List<Diagnostic>();

        LayerMerger.Merge("process", module.RootElement, layer, diagnostics);

        Assert.Equal(
            [(DiagnosticCodes.LayerAfterAndBefore, "/changes/steps/0"), (DiagnosticCodes.UnknownLayerAnchor, "/changes/steps/1"), (DiagnosticCodes.UnknownLayerAnchor, "/changes/steps/2"), (DiagnosticCodes.UnknownLayerAnchor, "/changes/steps/3")],
            diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)).OrderBy(pair => pair.Path, StringComparer.Ordinal));
    }

    [Fact]
    public void After_and_before_stay_ordinary_properties_in_a_list_where_order_does_not_count()
    {
        using var module = JsonDocument.Parse("""{ "kind": "process", "steps": [ { "name": "s", "escalations": [ { "name": "e", "after": "P1D" } ] } ] }""");
        var layer = Layer("""{ "steps": [ { "name": "s", "escalations": [ { "name": "e", "after": "PT4H" } ] } ] }""");
        var diagnostics = new List<Diagnostic>();

        var result = LayerMerger.Merge("process", module.RootElement, layer, diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal("PT4H", result.Merged["steps"]![0]!["escalations"]![0]!["after"]!.GetValue<string>());
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
