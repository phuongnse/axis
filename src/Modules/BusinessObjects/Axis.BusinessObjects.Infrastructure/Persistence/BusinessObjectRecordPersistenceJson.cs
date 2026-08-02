using System.Text.Json;
using Axis.BusinessObjects.Domain.Aggregates;

namespace Axis.BusinessObjects.Infrastructure.Persistence;

internal static class BusinessObjectRecordPersistenceJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string SerializeValues(IReadOnlyDictionary<string, IReadOnlyList<string>> values) =>
        JsonSerializer.Serialize(
            values.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal),
            Options);

    public static Dictionary<string, IReadOnlyList<string>> DeserializeValues(string json)
    {
        Dictionary<string, string[]> values =
            JsonSerializer.Deserialize<Dictionary<string, string[]>>(json, Options)
            ?? new(StringComparer.Ordinal);
        return values.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
    }

    public static string SerializeRuleEvaluations(
        IReadOnlyList<BusinessObjectRecordRuleEvaluation> evaluations) =>
        JsonSerializer.Serialize(evaluations, Options);

    public static List<BusinessObjectRecordRuleEvaluation> DeserializeRuleEvaluations(string json) =>
        JsonSerializer.Deserialize<List<BusinessObjectRecordRuleEvaluation>>(json, Options) ?? [];
}
