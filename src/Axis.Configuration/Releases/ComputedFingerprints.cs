using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Releases;

/// <summary>
/// The fingerprint of the stored computed values of each entity that is not a child entity: a
/// lowercase hex SHA-256 over every input that decides them. Those are the computed fields of the
/// entity and of its child entities, with their types and expressions, and each rule they call,
/// directly or through other rules. Two releases give an entity the same fingerprint exactly when
/// their computed definitions are the same, whatever else in the entity differs.
/// </summary>
public static class ComputedFingerprints
{
    /// <summary>The fingerprint of every entity of <paramref name="model"/> that is not a child entity, by entity id.</summary>
    public static IReadOnlyDictionary<Guid, string> Compute(ApplicationModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var fingerprints = new Dictionary<Guid, string>();
        foreach (var entity in model.Entities.Where(entity => !model.IsChildEntity(entity)))
        {
            fingerprints[entity.Id] = Fingerprint(model, entity);
        }

        return fingerprints;
    }

    private static string Fingerprint(ApplicationModel model, EntityModel entity)
    {
        var computedFields = ComputedFieldsOf(entity).ToList();
        var children = new List<(FieldModel Collection, EntityModel Child)>();
        foreach (var collection in entity.Fields.Where(field => field.Type == FieldType.ChildCollection))
        {
            var child = collection.Target is null ? null : model.FindEntity(collection.Target.Id);
            if (child is not null)
            {
                children.Add((collection, child));
                computedFields.AddRange(ComputedFieldsOf(child));
            }
        }

        var document = new JsonObject
        {
            ["fields"] = FieldsJson(ComputedFieldsOf(entity)),
            ["children"] = new JsonArray(children
                .OrderBy(pair => pair.Collection.Name, StringComparer.Ordinal)
                .Select(pair => (JsonNode)new JsonObject
                {
                    ["collection"] = pair.Collection.Name,
                    ["entity"] = pair.Child.Id.ToString(),
                    ["fields"] = FieldsJson(ComputedFieldsOf(pair.Child)),
                })
                .ToArray()),
            ["rules"] = new JsonArray(RulesCalledBy(computedFields)
                .Select(rule => (JsonNode)RuleJson(rule))
                .ToArray()),
        };

        var canonical = JsonCanonicalizer.Canonicalize(JsonSerializer.SerializeToElement(document));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static IEnumerable<FieldModel> ComputedFieldsOf(EntityModel entity) =>
        entity.Fields.Where(field => field.IsComputed);

    private static JsonArray FieldsJson(IEnumerable<FieldModel> fields) =>
        new(fields
            .OrderBy(field => field.Name, StringComparer.Ordinal)
            .Select(field => (JsonNode)new JsonObject
            {
                ["name"] = field.Name,
                ["type"] = field.Type.ToString(),
                ["maxLength"] = field.MaxLength,
                ["precision"] = field.Precision,
                ["scale"] = field.Scale,
                ["expression"] = field.Computed!.Expression,
            })
            .ToArray());

    private static JsonObject RuleJson(ExpressionRule rule) => new()
    {
        ["name"] = rule.Name,
        ["parameters"] = new JsonArray(rule.Parameters
            .Select(parameter => (JsonNode)new JsonObject
            {
                ["name"] = parameter.Name,
                ["type"] = parameter.Type.ToString(),
            })
            .ToArray()),
        ["resultType"] = rule.ResultType.ToString(),
        ["expression"] = rule.Expression,
    };

    /// <summary>Every rule the fields call, directly or through other rules, once by name, sorted by name ignoring case.</summary>
    private static IEnumerable<ExpressionRule> RulesCalledBy(IEnumerable<FieldModel> computedFields)
    {
        var found = new Dictionary<string, ExpressionRule>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<ExpressionRule>(computedFields.SelectMany(field => field.Computed!.Check.RuleCalls.Values));
        while (pending.TryPop(out var rule))
        {
            if (!found.TryAdd(rule.Name, rule))
            {
                continue;
            }

            foreach (var called in rule.BodyCheck?.RuleCalls.Values ?? [])
            {
                pending.Push(called);
            }
        }

        return found.Values.OrderBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase);
    }
}
