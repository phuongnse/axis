using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Contracts = Axis.Rules.Contracts;

namespace Axis.Rules.Application;

public sealed class SystemFieldRuleDefinitionProvider : IFieldRuleDefinitionProvider
{
    public IReadOnlyList<FieldRuleDefinitionDto> ListFieldRuleDefinitions() =>
        SystemFieldRuleCatalog.Definitions
            .OrderBy(definition => definition.Key.Value, StringComparer.Ordinal)
            .Select(ToDto)
            .ToList();

    public FieldRuleDefinitionDto? FindFieldRuleDefinition(string definitionKey) =>
        SystemFieldRuleCatalog.Find(definitionKey) is { } definition
            ? ToDto(definition)
            : null;

    private static FieldRuleDefinitionDto ToDto(FieldRuleDefinition definition) =>
        new(
            definition.Key.Value,
            definition.DisplayName,
            definition.Description,
            definition.SupportedFieldTypes,
            definition.Parameters.Select(ToDto).ToList());

    private static FieldRuleParameterDefinitionDto ToDto(FieldRuleParameterDefinition parameter) =>
        new(
            parameter.Key,
            (Contracts.FieldRuleParameterType)parameter.Type,
            parameter.IsRequired,
            parameter.AllowMultiple);
}
