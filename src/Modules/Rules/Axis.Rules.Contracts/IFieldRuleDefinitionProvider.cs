namespace Axis.Rules.Contracts;

public interface IFieldRuleDefinitionProvider
{
    IReadOnlyList<FieldRuleDefinitionDto> ListFieldRuleDefinitions();

    FieldRuleDefinitionDto? FindFieldRuleDefinition(string definitionKey);
}
