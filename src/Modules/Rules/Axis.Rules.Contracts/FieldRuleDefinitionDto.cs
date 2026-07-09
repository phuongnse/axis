namespace Axis.Rules.Contracts;

public sealed record FieldRuleDefinitionDto(
    string DefinitionKey,
    string DisplayName,
    string Description,
    IReadOnlyList<string> SupportedFieldTypes,
    IReadOnlyList<FieldRuleParameterDefinitionDto> Parameters);
