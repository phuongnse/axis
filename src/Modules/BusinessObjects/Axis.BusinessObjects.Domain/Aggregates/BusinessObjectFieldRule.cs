using System.Text.RegularExpressions;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.Aggregates;

public sealed partial class BusinessObjectFieldRule : Entity<BusinessObjectFieldRuleId>
{
    private Dictionary<string, string[]> _parameters = new(StringComparer.Ordinal);

    public string DefinitionKey { get; private set; }
    public int DefinitionVersion { get; private set; }
    public int Order { get; private set; }
    public IReadOnlyDictionary<string, string[]> Parameters =>
        _parameters.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);

    private BusinessObjectFieldRule(
        BusinessObjectFieldRuleId id,
        string definitionKey,
        int definitionVersion,
        int order)
        : this(
            id,
            definitionKey,
            definitionVersion,
            order,
            new Dictionary<string, string[]>(StringComparer.Ordinal))
    {
    }

    private BusinessObjectFieldRule(
        BusinessObjectFieldRuleId id,
        string definitionKey,
        int definitionVersion,
        int order,
        IReadOnlyDictionary<string, string[]> parameters)
        : base(id)
    {
        DefinitionKey = definitionKey;
        DefinitionVersion = definitionVersion;
        Order = order;
        _parameters = Clone(parameters);
    }

    public static Result<IReadOnlyList<BusinessObjectFieldRule>> CreateMany(
        IReadOnlyList<BusinessObjectFieldRuleSpec>? specs)
    {
        if (specs is null || specs.Count == 0)
            return Array.Empty<BusinessObjectFieldRule>();

        HashSet<string> seenDefinitionKeys = new(StringComparer.Ordinal);
        List<BusinessObjectFieldRule> rules = [];
        for (int index = 0; index < specs.Count; index++)
        {
            BusinessObjectFieldRuleSpec spec = specs[index];
            string definitionKey = spec.DefinitionKey.Trim();
            if (!seenDefinitionKeys.Add(definitionKey))
                return Result.Failure<IReadOnlyList<BusinessObjectFieldRule>>(
                    "Field rules must be unique per field.");

            Result<BusinessObjectFieldRule> rule = Create(
                spec.Id ?? BusinessObjectFieldRuleId.New(),
                spec,
                index);
            if (rule.IsFailure)
                return Result.Failure<IReadOnlyList<BusinessObjectFieldRule>>(rule.Error);

            rules.Add(rule.Value);
        }

        return rules;
    }

    public static Result<BusinessObjectFieldRule> Create(
        BusinessObjectFieldRuleId id,
        BusinessObjectFieldRuleSpec spec,
        int order)
    {
        string definitionKey = spec.DefinitionKey.Trim();
        if (definitionKey.Length == 0)
            return Result.Failure<BusinessObjectFieldRule>("Field rule definition key is required.");

        if (!DefinitionKeyPattern().IsMatch(definitionKey))
            return Result.Failure<BusinessObjectFieldRule>("Field rule definition key format is invalid.");

        if (spec.DefinitionVersion <= 0)
            return Result.Failure<BusinessObjectFieldRule>("Field rule definition version must be positive.");

        Result<Dictionary<string, string[]>> parameters = NormalizeParameters(spec.Parameters);
        if (parameters.IsFailure)
            return Result.Failure<BusinessObjectFieldRule>(parameters.Error);

        return new BusinessObjectFieldRule(id, definitionKey, spec.DefinitionVersion, order, parameters.Value);
    }

    internal void Apply(BusinessObjectFieldRule source)
    {
        DefinitionVersion = source.DefinitionVersion;
        Order = source.Order;
        _parameters = Clone(source._parameters);
    }

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
