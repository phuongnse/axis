using System.Text.Json;
using System.Text.Json.Serialization;
using Axis.Configuration.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// Writes an application at the top of the typical size in docs/domain/knowledge.md: entities with
/// references, enums, computed fields and validations that call rules, data sources with a relation,
/// a parameter and a filter, rules that call rules, and texts in two locales. It has no pages,
/// sites or seeds. With modules, it also declares them and uses their entities and rules.
/// </summary>
internal static class TypicalApplication
{
    public const int Entities = 90;
    public const int FieldsPerEntity = 23;
    public const int DataSources = 250;
    public const int Rules = 300;
    public const int TextKeys = 3_000;
    public const string MostUsedEntity = "Entity00";
    public const string SharedTextKey = "common.name";

    private const int ValidationsPerEntity = 3;
    private const int ModuleItems = 10;
    private const int ModuleDataSources = 20;
    private const int ModuleRules = 20;
    private const int ModuleTexts = 100;

    private static readonly JsonSerializerOptions _options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>
    /// Writes the application. With <paramref name="withErrors"/>, about 1 in 10 entities, data
    /// sources and rules has an expression error, and <paramref name="erroneousFiles"/> holds
    /// their relative paths. Every other file stays valid. With <paramref name="modules"/>, the
    /// application declares those modules, written by <see cref="WriteModules"/>, and uses them.
    /// </summary>
    public static TemporaryFolder Write(Guid applicationId, bool withErrors, out IReadOnlySet<string> erroneousFiles, IReadOnlyList<string>? modules = null)
    {
        var errors = new HashSet<string>(StringComparer.Ordinal);
        var folder = new TemporaryFolder().With("application.json", Json(new
        {
            id = applicationId,
            kind = "application",
            name = "Typical",
            formatVersion = 1,
            modules = modules?.Select(id => new { id, version = "^1.0.0" }).ToArray(),
        }));

        for (var k = 0; k < Rules; k++)
        {
            var path = $"rules/rule-{k:D3}.json";
            var expression = k % 10 == 5 ? $"Rule{k - 1:D3}(value) or value > {k}" : $"value > {k}";
            if (modules is not null && k % 10 == 7)
            {
                expression = $"{modules[k % modules.Count]}:IsPositive(value) or value > {k}";
            }

            if (withErrors && k % 10 == 0)
            {
                expression = "value > 'x'";
                errors.Add(path);
            }

            folder.With(path, Json(new
            {
                id = Guid.NewGuid(),
                kind = "rule",
                name = $"Rule{k:D3}",
                formatVersion = 1,
                parameters = new[] { new { name = "value", type = "integer" } },
                resultType = "boolean",
                expression,
            }));
        }

        var textKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Entities; i++)
        {
            var path = $"entities/entity-{i:D2}.json";
            var module = i == 0 || modules is null ? null : modules[i % modules.Count];
            var validations = new List<object>();
            for (var j = 0; j < ValidationsPerEntity; j++)
            {
                var expression = $"Rule{(ValidationsPerEntity * i) + j:D3}(quantity)";
                if (withErrors && i % 10 == 0 && j == 0)
                {
                    expression = "missingField > 0";
                    errors.Add(path);
                }

                var textKey = $"entity{i:D2}.check{j}";
                textKeys.Add(textKey);
                validations.Add(new { name = $"check{j}", expression, message = new { textKey }, field = "quantity" });
            }

            folder.With(path, Json(new
            {
                id = Guid.NewGuid(),
                kind = "entity",
                name = $"Entity{i:D2}",
                formatVersion = 1,
                label = Label(i, "label", textKeys),
                displayField = "name",
                fields = Fields(i, module, textKeys),
                validations,
            }));
        }

        for (var filler = 0; textKeys.Count < TextKeys; filler++)
        {
            textKeys.Add($"common.text{filler:D4}");
        }

        for (var d = 0; d < DataSources; d++)
        {
            var path = $"data-sources/data-source-{d:D3}.json";
            var filter = "parent.name != 'x' and (minQuantity is null or quantity >= minQuantity)";
            if (withErrors && d % 10 == 0)
            {
                filter = "missingField > 0";
                errors.Add(path);
            }

            folder.With(path, Json(new
            {
                id = Guid.NewGuid(),
                kind = "dataSource",
                name = $"DataSource{d:D3}",
                formatVersion = 1,
                // Entity00's parent is a text field, so every data source reads an entity with a reference.
                entity = $"Entity{(d % (Entities - 1)) + 1:D2}",
                fields = new[]
                {
                    new { name = "name", path = "name" },
                    new { name = "quantity", path = "quantity" },
                    new { name = "status", path = "status" },
                    new { name = "parentName", path = "parent.name" },
                    new { name = "parent", path = "parent" },
                },
                parameters = new[] { new { name = "minQuantity", type = "integer" } },
                filter,
                sort = "name",
                pageSize = 20,
            }));
        }

        foreach (var locale in new[] { "en", "vi" })
        {
            folder.With($"texts/{locale}.json", Json(new
            {
                id = Guid.NewGuid(),
                kind = "text",
                name = TextsName(locale),
                formatVersion = 1,
                locale,
                texts = textKeys.ToDictionary(key => key, key => $"{key} ({locale})"),
            }));
        }

        erroneousFiles = errors;
        return folder;
    }

    /// <summary>
    /// Writes one module per id, at version 1.0.0. Each has ten entities, twenty data sources, twenty
    /// rules and texts in two locales. Its entities and filters call no rule.
    /// </summary>
    public static TemporaryFolder WriteModules(IReadOnlyList<string> ids)
    {
        var root = new TemporaryFolder();
        foreach (var id in ids)
        {
            var folder = $"{id}/1.0.0";
            root.With($"{folder}/module.json", Json(new { kind = "module", id, version = "1.0.0", label = $"Module {id}" }));

            for (var k = 0; k < ModuleItems; k++)
            {
                List<object> fields =
                [
                    new { name = "name", type = "text", required = true, maxLength = 100 },
                    k == 0
                        ? new { name = "parent", type = "text", maxLength = 100 }
                        : new { name = "parent", type = "reference", target = $"Item{k - 1:D2}" },
                    new { name = "quantity", type = "integer" },
                ];
                for (var extra = 1; extra <= 8; extra++)
                {
                    fields.Add(new { name = $"field{extra:D2}", type = "text", maxLength = 200 });
                }

                root.With($"{folder}/entities/item-{k:D2}.json", Json(new
                {
                    id = Guid.NewGuid(),
                    kind = "entity",
                    name = $"Item{k:D2}",
                    formatVersion = 1,
                    displayField = "name",
                    fields,
                }));
            }

            for (var d = 0; d < ModuleDataSources; d++)
            {
                root.With($"{folder}/data-sources/data-source-{d:D2}.json", Json(new
                {
                    id = Guid.NewGuid(),
                    kind = "dataSource",
                    name = $"ItemSource{d:D2}",
                    formatVersion = 1,
                    entity = $"Item{d % ModuleItems:D2}",
                    fields = new[]
                    {
                        new { name = "name", path = "name" },
                        new { name = "quantity", path = "quantity" },
                    },
                    filter = "quantity >= 0",
                    sort = "name",
                    pageSize = 20,
                }));
            }

            for (var k = 0; k < ModuleRules; k++)
            {
                root.With($"{folder}/rules/rule-{k:D2}.json", Json(new
                {
                    id = Guid.NewGuid(),
                    kind = "rule",
                    name = k == 0 ? "IsPositive" : $"Rule{k:D2}",
                    formatVersion = 1,
                    parameters = new[] { new { name = "value", type = "integer" } },
                    resultType = "boolean",
                    expression = k == 0 ? "value > 0" : $"value > {k}",
                }));
            }

            foreach (var locale in new[] { "en", "vi" })
            {
                root.With($"{folder}/texts/{locale}.json", Json(new
                {
                    id = Guid.NewGuid(),
                    kind = "text",
                    name = TextsName(locale),
                    formatVersion = 1,
                    locale,
                    texts = Enumerable.Range(0, ModuleTexts).ToDictionary(n => $"{id}.text{n:D3}", n => $"{id}.text{n:D3} ({locale})"),
                }));
            }
        }

        return root;
    }

    /// <summary>
    /// The fields of entity <paramref name="index"/>, padded with text fields to <see cref="FieldsPerEntity"/>.
    /// With <paramref name="module"/>, the last field is a reference to that module's first item.
    /// </summary>
    private static List<object> Fields(int index, string? module, ISet<string> textKeys)
    {
        List<object> fields =
        [
            new { name = "name", type = "text", required = true, maxLength = 100, label = Label(index, "name", textKeys) },
            index == 0
                ? new { name = "parent", type = "text", maxLength = 100, label = Label(index, "parent", textKeys) }
                : new { name = "parent", type = "reference", target = $"Entity{index - 1:D2}", label = Label(index, "parent", textKeys) },
            new { name = "status", type = "enum", values = new[] { "open", "closed" }, label = Label(index, "status", textKeys) },
            new { name = "quantity", type = "integer", label = Label(index, "quantity", textKeys) },
            new { name = "price", type = "decimal", precision = 18, scale = 2, label = Label(index, "price", textKeys) },
            new { name = "total", type = "decimal", precision = 18, scale = 2, expression = "quantity * price", label = Label(index, "total", textKeys) },
            new { name = "dueOn", type = "date", label = Label(index, "dueOn", textKeys) },
            new { name = "active", type = "boolean", label = Label(index, "active", textKeys) },
        ];
        if (index > 0)
        {
            fields.Add(new { name = "owner", type = "reference", target = MostUsedEntity, label = Label(index, "owner", textKeys) });
        }

        var padded = module is null ? FieldsPerEntity : FieldsPerEntity - 1;
        for (var extra = 1; fields.Count < padded; extra++)
        {
            var name = $"extra{extra:D2}";
            fields.Add(new { name, type = "text", maxLength = 200, label = Label(index, name, textKeys) });
        }

        if (module is not null)
        {
            fields.Add(new { name = "item", type = "reference", target = $"{module}/Item00", label = Label(index, "item", textKeys) });
        }

        return fields;
    }

    /// <summary>The label of a resource's <paramref name="field"/>. Every name field of every entity shares one key.</summary>
    private static object Label(int index, string field, ISet<string> textKeys)
    {
        var textKey = field == "name" ? SharedTextKey : $"entity{index:D2}.{field}";
        textKeys.Add(textKey);
        return new { textKey };
    }

    private static string TextsName(string locale) => $"Texts{char.ToUpperInvariant(locale[0])}{locale[1..]}";

    private static string Json(object resource) => JsonSerializer.Serialize(resource, _options);
}
