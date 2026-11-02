using System.Text.Json;
using Axis.Configuration.Loading;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Tests;

/// <summary>
/// Keeps the list keys table in step with the resource schemas. A customization layer finds an element of
/// a list by its key, so a list without an entry, or a keyed list whose schema does not require the key,
/// would let a layer miss the element.
/// </summary>
public sealed class ListKeyTests
{
    private const string SchemaResourcePrefix = "Axis.Configuration.Schemas.";
    private const string SchemaResourceSuffix = ".schema.json";

    // No layer targets an application or a scenario.
    private static readonly string[] _untargeted = [ResourceKinds.Application, ResourceKinds.Scenario];

    private static readonly string[] _pendingKinds = [ResourceKinds.Process, ResourceKinds.Form, ResourceKinds.Page];

    [Fact]
    public void Every_object_list_in_the_schemas_agrees_with_the_table()
    {
        var problems = new List<string>();
        foreach (var kind in SchemaKinds())
        {
            problems.AddRange(Problems(kind, LoadSchema(kind), Lookup));
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void An_object_list_missing_from_the_table_is_reported()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": { "things": { "type": "array", "items": { "$ref": "#/$defs/thing" } } },
              "$defs": { "thing": { "type": "object", "required": ["name"], "properties": { "name": { "type": "string" } } } }
            }
            """);

        var problems = Problems(ResourceKinds.Entity, schema, (_, _) => null);

        Assert.Equal(["entity: things is not in the list keys table"], problems);
    }

    [Fact]
    public void A_keyed_list_whose_object_items_do_not_require_the_key_is_reported()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": {
                "things": {
                  "type": "array",
                  "items": { "oneOf": [
                    { "$ref": "#/$defs/thing" },
                    { "type": "object", "required": ["name"], "properties": { "name": { "type": "string" } } }
                  ] }
                }
              },
              "$defs": { "thing": { "type": "object", "properties": { "name": { "type": "string" } } } }
            }
            """);

        var problems = Problems(ResourceKinds.Entity, schema, Keyed("name"));

        Assert.Equal(["entity: things is keyed by name, but an object item does not require it"], problems);
    }

    [Fact]
    public void A_pending_entry_in_a_kind_other_than_process_form_or_page_is_reported()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": { "things": { "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" } } } } }
            }
            """);

        var problems = Problems(ResourceKinds.Entity, schema, Pending);

        Assert.Equal(["entity: things is pending, which only a process, form or page list may be"], problems);
    }

    [Fact]
    public void A_pending_entry_whose_object_items_already_require_the_key_is_reported()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": { "things": { "type": "array", "items": { "type": "object", "required": ["name"], "properties": { "name": { "type": "string" } } } } }
            }
            """);

        var problems = Problems(ResourceKinds.Process, schema, Pending);

        Assert.Equal(["process: things is pending, but its items already require name, so key it"], problems);
    }

    [Fact]
    public void A_pending_entry_is_accepted_while_an_object_item_does_not_require_the_key()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": { "things": { "type": "array", "items": { "type": "object", "properties": { "name": { "type": "string" } } } } }
            }
            """);

        Assert.Empty(Problems(ResourceKinds.Page, schema, Pending));
    }

    [Fact]
    public void A_string_variant_counts_as_the_key_and_a_list_of_strings_needs_no_entry()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": {
                "groupBy": {
                  "type": "array",
                  "items": { "oneOf": [
                    { "type": "string" },
                    { "type": "object", "required": ["field"], "properties": { "field": { "type": "string" } } }
                  ] }
                },
                "roles": { "type": "array", "items": { "type": "string" } }
              }
            }
            """);

        Assert.Empty(Problems(ResourceKinds.DataSource, schema, Keyed("field")));
    }

    [Fact]
    public void A_recursive_definition_is_walked_once_and_its_nested_lists_are_checked()
    {
        var schema = Parse("""
            {
              "type": "object",
              "properties": { "nodes": { "type": "array", "items": { "$ref": "#/$defs/node" } } },
              "$defs": {
                "node": {
                  "type": "object",
                  "required": ["name"],
                  "properties": {
                    "name": { "type": "string" },
                    "children": { "type": "array", "items": { "$ref": "#/$defs/node" } },
                    "extras": { "type": "object", "additionalProperties": { "type": "array", "items": { "type": "object", "properties": { "x": {} } } } }
                  }
                }
              }
            }
            """);

        var problems = Problems(ResourceKinds.Entity, schema, (_, property) => property == "extras" ? null : new ListKey(ListKeyMode.Keyed, "name"));

        Assert.Equal(["entity: extras is not in the list keys table"], problems);
    }

    [Fact]
    public void TryGet_gives_the_key_of_a_keyed_list()
    {
        Assert.True(ListKeys.TryGet(ResourceKinds.Entity, "validations", out var validations));
        Assert.Equal(new ListKey(ListKeyMode.Keyed, "name"), validations);

        Assert.True(ListKeys.TryGet(ResourceKinds.DataSource, "groupBy", out var groupBy));
        Assert.Equal(new ListKey(ListKeyMode.Keyed, "field"), groupBy);

        Assert.True(ListKeys.TryGet(ResourceKinds.Seed, "records", out var records));
        Assert.Equal(new ListKey(ListKeyMode.Keyed, "id"), records);
    }

    [Fact]
    public void TryGet_marks_a_value_list_and_does_not_know_an_unnamed_list()
    {
        Assert.True(ListKeys.TryGet(ResourceKinds.DataSource, "measures", out var measures));
        Assert.Equal(new ListKey(ListKeyMode.Value, null), measures);

        Assert.False(ListKeys.TryGet(ResourceKinds.Entity, "unknown", out _));
    }

    private static ListKey? Lookup(string kind, string property) =>
        ListKeys.TryGet(kind, property, out var key) ? key : null;

    private static Func<string, string, ListKey?> Keyed(string key) =>
        (_, _) => new ListKey(ListKeyMode.Keyed, key);

    private static ListKey? Pending(string kind, string property) => new ListKey(ListKeyMode.Pending, null);

    /// <summary>Walks a schema and lists where its object lists and the table disagree.</summary>
    private static List<string> Problems(string kind, JsonElement root, Func<string, string, ListKey?> lookup)
    {
        var problems = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Walk(root, null);
        return problems.Distinct(StringComparer.Ordinal).ToList();

        void Walk(JsonElement node, string? property)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (node.TryGetProperty("$ref", out var reference))
            {
                var name = reference.GetString()!["#/$defs/".Length..];
                if (visited.Add($"{name}|{property}"))
                {
                    Walk(root.GetProperty("$defs").GetProperty(name), property);
                }
            }

            if (node.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
            {
                if (property is not null)
                {
                    CheckList(property, Variants(items).Where(IsObject).ToList());
                }

                Walk(items, property);
            }

            if (node.TryGetProperty("properties", out var properties))
            {
                foreach (var entry in properties.EnumerateObject())
                {
                    Walk(entry.Value, entry.Name);
                }
            }

            if (node.TryGetProperty("additionalProperties", out var additional))
            {
                Walk(additional, property);
            }

            foreach (var keyword in new[] { "oneOf", "anyOf", "allOf" })
            {
                if (node.TryGetProperty(keyword, out var members) && members.ValueKind == JsonValueKind.Array)
                {
                    foreach (var member in members.EnumerateArray())
                    {
                        Walk(member, property);
                    }
                }
            }

            foreach (var keyword in new[] { "if", "then", "else" })
            {
                if (node.TryGetProperty(keyword, out var branch))
                {
                    Walk(branch, property);
                }
            }
        }

        void CheckList(string property, List<JsonElement> objectItems)
        {
            if (objectItems.Count == 0)
            {
                return;
            }

            if (lookup(kind, property) is not { } entry)
            {
                problems.Add($"{kind}: {property} is not in the list keys table");
                return;
            }

            switch (entry.Mode)
            {
                case ListKeyMode.Keyed:
                    if (objectItems.Any(item => !Requires(item, entry.Key!)))
                    {
                        problems.Add($"{kind}: {property} is keyed by {entry.Key}, but an object item does not require it");
                    }

                    break;
                case ListKeyMode.Pending:
                    if (!_pendingKinds.Contains(kind))
                    {
                        problems.Add($"{kind}: {property} is pending, which only a process, form or page list may be");
                    }
                    else if (objectItems.All(item => Requires(item, "name")))
                    {
                        problems.Add($"{kind}: {property} is pending, but its items already require name, so key it");
                    }

                    break;
            }
        }

        // The item schemas of a list: a reference is followed, and each member of oneOf and anyOf counts.
        IEnumerable<JsonElement> Variants(JsonElement item)
        {
            if (item.TryGetProperty("$ref", out var reference))
            {
                var name = reference.GetString()!["#/$defs/".Length..];
                foreach (var variant in Variants(root.GetProperty("$defs").GetProperty(name)))
                {
                    yield return variant;
                }

                yield break;
            }

            var expanded = false;
            foreach (var keyword in new[] { "oneOf", "anyOf" })
            {
                if (item.TryGetProperty(keyword, out var members) && members.ValueKind == JsonValueKind.Array)
                {
                    expanded = true;
                    foreach (var member in members.EnumerateArray())
                    {
                        foreach (var variant in Variants(member))
                        {
                            yield return variant;
                        }
                    }
                }
            }

            if (!expanded)
            {
                yield return item;
            }
        }
    }

    private static bool IsObject(JsonElement schema) =>
        schema.TryGetProperty("properties", out _)
        || (schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object");

    private static bool Requires(JsonElement schema, string key) =>
        schema.TryGetProperty("required", out var required)
        && required.ValueKind == JsonValueKind.Array
        && required.EnumerateArray().Any(entry => entry.GetString() == key);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement LoadSchema(string kind)
    {
        using var stream = typeof(ResourceKinds).Assembly.GetManifestResourceStream($"{SchemaResourcePrefix}{kind}{SchemaResourceSuffix}")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }

    private static IEnumerable<string> SchemaKinds() =>
        typeof(ResourceKinds).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(SchemaResourcePrefix, StringComparison.Ordinal) && name.EndsWith(SchemaResourceSuffix, StringComparison.Ordinal))
            .Select(name => name[SchemaResourcePrefix.Length..^SchemaResourceSuffix.Length])
            .Where(kind => !_untargeted.Contains(kind, StringComparer.Ordinal));
}
