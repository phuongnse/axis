using System.Text.Json;
using Axis.Configuration.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// Writes an application at the top of the typical size in docs/domain/knowledge.md: entities with
/// references, enums, computed fields and validations that call rules, data sources with a relation,
/// a parameter and a filter, rules that call rules, and texts in two locales. It has no pages,
/// sites or seeds.
/// </summary>
internal static class TypicalApplication
{
    public const int Entities = 90;
    public const int FieldsPerEntity = 23;
    public const int DataSources = 250;
    public const int Rules = 300;

    private const int ValidationsPerEntity = 3;

    /// <summary>
    /// Writes the application. With <paramref name="withErrors"/>, about 1 in 10 entities, data
    /// sources and rules has an expression error, and <paramref name="erroneousFiles"/> holds
    /// their relative paths. Every other file stays valid.
    /// </summary>
    public static TemporaryFolder Write(Guid applicationId, bool withErrors, out IReadOnlySet<string> erroneousFiles)
    {
        var errors = new HashSet<string>(StringComparer.Ordinal);
        var folder = new TemporaryFolder().With("application.json", Json(new
        {
            id = applicationId,
            kind = "application",
            name = "Typical",
            formatVersion = 1,
        }));

        for (var k = 0; k < Rules; k++)
        {
            var path = $"rules/rule-{k:D3}.json";
            var expression = k % 10 == 5 ? $"Rule{k - 1:D3}(value) or value > {k}" : $"value > {k}";
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

        var textKeys = new List<string>();
        for (var i = 0; i < Entities; i++)
        {
            var path = $"entities/entity-{i:D2}.json";
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
                validations.Add(new { expression, message = new { textKey }, field = "quantity" });
            }

            folder.With(path, Json(new
            {
                id = Guid.NewGuid(),
                kind = "entity",
                name = $"Entity{i:D2}",
                formatVersion = 1,
                displayField = "name",
                fields = Fields(i),
                validations,
            }));
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
                name = $"Texts{char.ToUpperInvariant(locale[0])}{locale[1..]}",
                formatVersion = 1,
                locale,
                texts = textKeys.ToDictionary(key => key, key => $"{key} ({locale})"),
            }));
        }

        erroneousFiles = errors;
        return folder;
    }

    /// <summary>The fields of entity <paramref name="index"/>, padded with text fields to <see cref="FieldsPerEntity"/>.</summary>
    private static List<object> Fields(int index)
    {
        List<object> fields =
        [
            new { name = "name", type = "text", required = true, maxLength = 100 },
            index == 0
                ? new { name = "parent", type = "text", maxLength = 100 }
                : new { name = "parent", type = "reference", target = $"Entity{index - 1:D2}" },
            new { name = "status", type = "enum", values = new[] { "open", "closed" } },
            new { name = "quantity", type = "integer" },
            new { name = "price", type = "decimal", precision = 18, scale = 2 },
            new { name = "total", type = "decimal", precision = 18, scale = 2, expression = "quantity * price" },
            new { name = "dueOn", type = "date" },
            new { name = "active", type = "boolean" },
        ];
        for (var extra = 1; fields.Count < FieldsPerEntity; extra++)
        {
            fields.Add(new { name = $"extra{extra:D2}", type = "text", maxLength = 200 });
        }

        return fields;
    }

    private static string Json(object resource) => JsonSerializer.Serialize(resource);
}
