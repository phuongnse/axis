namespace Axis.Rules.Contracts;

public sealed record FieldRuleParameterDefinitionDto(
    string Key,
    FieldRuleParameterType Type,
    bool IsRequired,
    bool AllowMultiple);
