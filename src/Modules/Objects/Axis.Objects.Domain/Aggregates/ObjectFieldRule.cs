using System.Text.RegularExpressions;
using Axis.Objects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Objects.Domain.Aggregates;

public sealed partial class ObjectFieldRule : Entity<ObjectFieldRuleId>
{
    private Dictionary<string, string[]> _parameters = new(StringComparer.Ordinal);

    public string DefinitionKey { get; private set; }
    public int Order { get; private set; }
    public IReadOnlyDictionary<string, string[]> Parameters =>
        _parameters.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);

    private ObjectFieldRule(ObjectFieldRuleId id, string definitionKey, int order)
        : this(id, definitionKey, order, new Dictionary<string, string[]>(StringComparer.Ordinal))
    {
    }

    private ObjectFieldRule(
        ObjectFieldRuleId id,
        string definitionKey,
        int order,
        IReadOnlyDictionary<string, string[]> parameters)
        : base(id)
    {
        DefinitionKey = definitionKey;
        Order = order;
        _parameters = Clone(parameters);
    }

    public static Result<IReadOnlyList<ObjectFieldRule>> CreateMany(
        IReadOnlyList<ObjectFieldRuleSpec>? specs)
    {
        if (specs is null || specs.Count == 0)
            return Array.Empty<ObjectFieldRule>();

        HashSet<string> seenDefinitionKeys = new(StringComparer.Ordinal);
        List<ObjectFieldRule> rules = [];
        for (int index = 0; index < specs.Count; index++)
        {
            ObjectFieldRuleSpec spec = specs[index];
            string definitionKey = spec.DefinitionKey.Trim();
            if (!seenDefinitionKeys.Add(definitionKey))
                return Result.Failure<IReadOnlyList<ObjectFieldRule>>(
                    "Field rules must be unique per field.");

            Result<ObjectFieldRule> rule = Create(ObjectFieldRuleId.New(), spec, index);
            if (rule.IsFailure)
                return Result.Failure<IReadOnlyList<ObjectFieldRule>>(rule.Error);

            rules.Add(rule.Value);
        }

        return rules;
    }

    public static Result<ObjectFieldRule> Create(
        ObjectFieldRuleId id,
        ObjectFieldRuleSpec spec,
        int order)
    {
        string definitionKey = spec.DefinitionKey.Trim();
        if (definitionKey.Length == 0)
            return Result.Failure<ObjectFieldRule>("Field rule definition key is required.");

        if (!DefinitionKeyPattern().IsMatch(definitionKey))
            return Result.Failure<ObjectFieldRule>("Field rule definition key format is invalid.");

        Result<Dictionary<string, string[]>> parameters = NormalizeParameters(spec.Parameters);
        if (parameters.IsFailure)
            return Result.Failure<ObjectFieldRule>(parameters.Error);

        return new ObjectFieldRule(id, definitionKey, order, parameters.Value);
    }

    public ObjectFieldRule Snapshot() =>
        new(ObjectFieldRuleId.New(), DefinitionKey, Order, _parameters);

    private static Result<Dictionary<string, string[]>> NormalizeParameters(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? parameters)
    {
        Dictionary<string, string[]> normalized = new(StringComparer.Ordinal);
        if (parameters is null)
            return normalized;

        foreach ((string rawKey, IReadOnlyList<string> rawValues) in parameters)
        {
            string key = rawKey.Trim();
            if (key.Length == 0)
                return Result.Failure<Dictionary<string, string[]>>(
                    "Field rule parameter key is required.");

            if (!ParameterKeyPattern().IsMatch(key))
                return Result.Failure<Dictionary<string, string[]>>(
                    "Field rule parameter key format is invalid.");

            if (normalized.ContainsKey(key))
                return Result.Failure<Dictionary<string, string[]>>(
                    "Field rule parameter keys must be unique.");

            string[] values = rawValues.Select(value => value.Trim()).ToArray();
            if (values.Any(string.IsNullOrWhiteSpace))
                return Result.Failure<Dictionary<string, string[]>>(
                    "Field rule parameter value is required.");

            normalized.Add(key, values);
        }

        return normalized;
    }

    private static Dictionary<string, string[]> Clone(IReadOnlyDictionary<string, string[]> parameters) =>
        parameters.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);

    [GeneratedRegex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex DefinitionKeyPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterKeyPattern();
}
