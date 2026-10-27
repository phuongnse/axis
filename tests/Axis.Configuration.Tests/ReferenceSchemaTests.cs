using System.Reflection;
using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Tests;

/// <summary>
/// Keeps the x-axis-reference marks in the schemas in step with the reference index. Studio
/// offers a picker for each marked property, so a relation without a mark gets no picker.
/// </summary>
public sealed class ReferenceSchemaTests
{
    private const string SchemaResourcePrefix = "Axis.Configuration.Schemas.";
    private const string SchemaResourceSuffix = ".schema.json";
    private const string Keyword = "x-axis-reference";

    private static readonly string[] _kinds = ["entity", "dataSource", "process", "role", "rule", "connector", "event", "sequence", "form", "page", "text"];

    // Text entries, packages, modules, rule calls and field paths name no single schema property.
    // Stub packages and package operations name a package. Text keys are checked by their own test.
    private static readonly string[] _unpickered =
    [
        ReferenceRelations.TextEntry,
        ReferenceRelations.Package,
        ReferenceRelations.Module,
        ReferenceRelations.RuleCall,
        ReferenceRelations.FieldPath,
        ReferenceRelations.PackageOperation,
        ReferenceRelations.StubPackage,
    ];

    private static readonly (string Relation, string Schema, string Path, string Kind)[] _pickers =
    [
        (ReferenceRelations.FieldTarget, "entity", "properties/fields/items/properties/target", "entity"),
        (ReferenceRelations.FieldSequence, "entity", "properties/fields/items/properties/sequence", "sequence"),
        (ReferenceRelations.NavigationPage, "site", "properties/navigation/items/properties/page", "page"),
        (ReferenceRelations.FieldTarget, "page", "properties/parameters/items/properties/target", "entity"),
        (ReferenceRelations.WidgetEntity, "page", "properties/widgets/items/properties/entity", "entity"),
        (ReferenceRelations.WidgetDataSource, "page", "properties/widgets/items/properties/dataSource", "dataSource"),
        (ReferenceRelations.WidgetForm, "page", "properties/widgets/items/properties/form", "form"),
        (ReferenceRelations.WidgetProcess, "page", "properties/widgets/items/properties/process", "process"),
        (ReferenceRelations.ActionProcess, "page", "properties/widgets/items/properties/actions/items/properties/process", "process"),
        (ReferenceRelations.NavigatePage, "page", "$defs/action/properties/page", "page"),
        (ReferenceRelations.ActionProcess, "page", "$defs/rowAction/properties/process", "process"),
        (ReferenceRelations.NavigatePage, "page", "$defs/rowAction/properties/page", "page"),
        (ReferenceRelations.NavigatePage, "page", "$defs/navigateAction/properties/page", "page"),
        (ReferenceRelations.FormEntity, "form", "properties/entity", "entity"),
        (ReferenceRelations.SeedEntity, "seed", "properties/entity", "entity"),
        (ReferenceRelations.DataSourceEntity, "dataSource", "properties/entity", "entity"),
        (ReferenceRelations.ProcessEntity, "process", "properties/entity", "entity"),
        (ReferenceRelations.TaskForm, "process", "properties/steps/items/properties/form", "form"),
        (ReferenceRelations.TaskRole, "process", "properties/steps/items/properties/assignee/properties/role", "role"),
        (ReferenceRelations.ConnectorCall, "process", "properties/steps/items/properties/connector/properties/name", "connector"),
        (ReferenceRelations.RaisedEvent, "process", "properties/steps/items/properties/event", "event"),
        (ReferenceRelations.CreatedEntity, "process", "properties/steps/items/properties/entity", "entity"),
        (ReferenceRelations.NavigatePage, "process", "$defs/notifyOpen/properties/page", "page"),
        (ReferenceRelations.PolicyRole, "policy", "properties/roles/items", "role"),
        (ReferenceRelations.Grant, "policy", "properties/grants/items/properties/entity", "entity"),
        (ReferenceRelations.Grant, "policy", "properties/grants/items/properties/dataSource", "dataSource"),
        (ReferenceRelations.Grant, "policy", "properties/grants/items/properties/process", "process"),
        (ReferenceRelations.EventEntity, "event", "properties/entity", "entity"),
        (ReferenceRelations.EndpointEntity, "endpoint", "properties/entity", "entity"),
        (ReferenceRelations.EndpointProcess, "endpoint", "properties/process", "process"),
        (ReferenceRelations.TriggerProcess, "trigger", "properties/process", "process"),
        (ReferenceRelations.TriggerEvent, "trigger", "properties/event", "event"),
        (ReferenceRelations.TriggerEntity, "trigger", "properties/entity", "entity"),
        (ReferenceRelations.TriggerDataSource, "trigger", "properties/dataSource", "dataSource"),
        (ReferenceRelations.MigrationEntity, "migration", "properties/entity", "entity"),
        (ReferenceRelations.ScenarioEntity, "scenario", "properties/steps/items/properties/entity", "entity"),
        (ReferenceRelations.ScenarioProcess, "scenario", "properties/steps/items/properties/process", "process"),
        (ReferenceRelations.ScenarioRole, "scenario", "properties/users/additionalProperties/items", "role"),
        (ReferenceRelations.ScenarioRole, "scenario", "properties/steps/items/properties/assignee/properties/role", "role"),
        (ReferenceRelations.ScenarioRole, "scenario", "properties/steps/items/properties/recipient/properties/role", "role"),
        (ReferenceRelations.StubConnector, "scenario", "properties/stubs/items/properties/connector/properties/name", "connector"),
    ];

    // Entity pickers with no reference index edge.
    private static readonly (string Schema, string Path, string Kind)[] _unindexedPickers =
    [
        ("dataSource", "$defs/parameter/properties/target", "entity"),
        ("endpoint", "$defs/input/properties/target", "entity"),
    ];

    private static readonly (string Schema, string Path, string Part, string Of)[] _partMarks =
    [
        ("form", "$defs/field/properties/field", "field", "/entity"),
        ("policy", "properties/grants/items/properties/fields/items", "field", "2/entity"),
        ("process", "$defs/step/properties/next", "step", "/name"),
        ("process", "$defs/branch/properties/next", "step", "/name"),
        ("process", "$defs/outcome/properties/next", "step", "/name"),
        ("process", "$defs/step/properties/deadline/properties/next", "step", "/name"),
        ("scenario", "properties/steps/items/properties/step", "step", "1/process"),
        ("page", "$defs/widget/properties/measure", "column", "1/dataSource"),
        ("page", "$defs/widget/properties/comparison/properties/measure", "column", "2/dataSource"),
        ("page", "$defs/widget/properties/category", "column", "1/dataSource"),
        ("page", "$defs/widget/properties/measures/items/properties/name", "column", "3/dataSource"),
        ("page", "$defs/widget/properties/parameters/propertyNames", "parameter", "2/dataSource"),
        ("trigger", "properties/parameters/propertyNames", "parameter", "/dataSource"),
        ("process", "$defs/step/properties/connector/properties/operation", "operation", "1/name"),
        ("scenario", "$defs/stub/properties/connector/properties/operation", "operation", "1/name"),
        ("connector", "$defs/operation/properties/lookup", "operation", "/name"),
        ("scenario", "$defs/step/properties/outcome", "outcome", "1/step"),
    ];

    [Fact]
    public void Every_reference_relation_has_a_picker_or_is_unpickered()
    {
        var relations = typeof(ReferenceRelations).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();
        var pickered = _pickers.Select(picker => picker.Relation).Append(ReferenceRelations.LabelText).ToList();

        Assert.Empty(relations.Except(pickered).Except(_unpickered));
        Assert.Empty(pickered.Except(relations));
        Assert.Empty(pickered.Intersect(_unpickered));
    }

    [Fact]
    public void Each_picker_property_carries_its_kind()
    {
        foreach (var (relation, schema, path, kind) in _pickers)
        {
            var root = LoadSchema(schema);
            var mark = Mark(root, Walk(root, path));
            Assert.True(mark is { ValueKind: JsonValueKind.String } value && value.GetString() == kind,
                $"{schema}: {path} ({relation}) should carry x-axis-reference \"{kind}\".");
        }

        foreach (var (schema, path, kind) in _unindexedPickers)
        {
            var root = LoadSchema(schema);
            var mark = Mark(root, Walk(root, path));
            Assert.True(mark is { ValueKind: JsonValueKind.String } value && value.GetString() == kind,
                $"{schema}: {path} should carry x-axis-reference \"{kind}\".");
        }
    }

    [Fact]
    public void Each_part_property_carries_its_part_mark()
    {
        foreach (var (schema, path, part, of) in _partMarks)
        {
            var root = LoadSchema(schema);
            var mark = Mark(root, Walk(root, path));
            Assert.True(mark is { ValueKind: JsonValueKind.Object } value
                && value.GetProperty("part").GetString() == part
                && value.GetProperty("of").GetString() == of,
                $"{schema}: {path} should carry the part mark {part} of {of}.");
        }
    }

    [Fact]
    public void Every_text_key_property_carries_the_text_kind()
    {
        foreach (var schema in SchemaKinds())
        {
            var root = LoadSchema(schema);
            foreach (var node in Objects(root))
            {
                if (!TryGetTextKey(node, out var textKey))
                {
                    continue;
                }

                Assert.True(Mark(root, textKey)?.GetString() == "text", $"{schema}: textKey should carry x-axis-reference \"text\".");
            }
        }
    }

    [Fact]
    public void Every_mark_is_a_known_kind_or_a_part_mark_and_is_listed()
    {
        var marks = 0;
        var textKeys = 0;
        foreach (var schema in SchemaKinds())
        {
            var root = LoadSchema(schema);
            foreach (var node in Objects(root))
            {
                if (node.TryGetProperty(Keyword, out var mark))
                {
                    marks++;
                    Assert.True(IsValidMark(mark), $"{schema}: invalid x-axis-reference {mark}.");
                }

                if (TryGetTextKey(node, out _))
                {
                    textKeys++;
                }
            }
        }

        Assert.Equal(_pickers.Length + _unindexedPickers.Length + _partMarks.Length + textKeys, marks);
    }

    private static bool IsValidMark(JsonElement mark) => mark.ValueKind switch
    {
        JsonValueKind.String => _kinds.Contains(mark.GetString()),
        JsonValueKind.Object => mark.GetProperty("part").GetString() is "field" or "step" or "parameter" or "column" or "outcome" or "operation" && mark.GetProperty("of").ValueKind == JsonValueKind.String,
        _ => false,
    };

    private static bool TryGetTextKey(JsonElement node, out JsonElement textKey)
    {
        textKey = default;
        return node.TryGetProperty("properties", out var properties) && properties.TryGetProperty("textKey", out textKey);
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
            .Select(name => name[SchemaResourcePrefix.Length..^SchemaResourceSuffix.Length]);

    private static IEnumerable<JsonElement> Objects(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            yield return node;
            foreach (var property in node.EnumerateObject())
            {
                foreach (var child in Objects(property.Value))
                {
                    yield return child;
                }
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray())
            {
                foreach (var child in Objects(item))
                {
                    yield return child;
                }
            }
        }
    }

    private static JsonElement Walk(JsonElement root, string path)
    {
        var node = root;
        var segments = path.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            node = Resolve(root, node);
            node = segments[i] switch
            {
                "properties" => node.GetProperty("properties").GetProperty(segments[++i]),
                "items" => node.GetProperty("items"),
                "additionalProperties" => node.GetProperty("additionalProperties"),
                "propertyNames" => node.GetProperty("propertyNames"),
                "$defs" => root.GetProperty("$defs").GetProperty(segments[++i]),
                _ => throw new ArgumentException($"Unknown path segment in {path}.", nameof(path)),
            };
        }

        return node;
    }

    private static JsonElement Resolve(JsonElement root, JsonElement node)
    {
        while (node.TryGetProperty("$ref", out var reference))
        {
            node = root.GetProperty("$defs").GetProperty(reference.GetString()!["#/$defs/".Length..]);
        }

        return node;
    }

    private static JsonElement? Mark(JsonElement root, JsonElement node)
    {
        if (node.TryGetProperty(Keyword, out var mark))
        {
            return mark;
        }

        var target = Resolve(root, node);
        return target.TryGetProperty(Keyword, out var resolved) ? resolved : null;
    }
}
