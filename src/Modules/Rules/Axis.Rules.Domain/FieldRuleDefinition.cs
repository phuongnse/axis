using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed record FieldRuleDefinition
{
    private FieldRuleDefinition(
        RuleDefinitionKey key,
        string displayName,
        string description,
        IReadOnlyList<string> supportedFieldTypes,
        IReadOnlyList<FieldRuleParameterDefinition> parameters)
    {
        Key = key;
        DisplayName = displayName;
        Description = description;
        SupportedFieldTypes = supportedFieldTypes;
        Parameters = parameters;
    }

    public RuleDefinitionKey Key { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public IReadOnlyList<string> SupportedFieldTypes { get; }
    public IReadOnlyList<FieldRuleParameterDefinition> Parameters { get; }

    public static Result<FieldRuleDefinition> Create(
        string definitionKey,
        string displayName,
        string description,
        IReadOnlyList<string> supportedFieldTypes,
        IReadOnlyList<FieldRuleParameterDefinition> parameters)
    {
        Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(definitionKey);
        if (key.IsFailure)
            return Result.Failure<FieldRuleDefinition>(key.Error);

        if (string.IsNullOrWhiteSpace(displayName))
            return Result.Failure<FieldRuleDefinition>("Rule definition display name is required.");

        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure<FieldRuleDefinition>("Rule definition description is required.");

        string[] normalizedFieldTypes = supportedFieldTypes
            .Select(fieldType => fieldType.Trim())
            .Where(fieldType => fieldType.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedFieldTypes.Length == 0)
            return Result.Failure<FieldRuleDefinition>("Rule definition must support at least one field type.");

        if (parameters.Select(parameter => parameter.Key).Distinct(StringComparer.Ordinal).Count() != parameters.Count)
            return Result.Failure<FieldRuleDefinition>("Rule definition parameter keys must be unique.");

        return new FieldRuleDefinition(
            key.Value,
            displayName.Trim(),
            description.Trim(),
            normalizedFieldTypes,
            parameters.ToArray());
    }
}
