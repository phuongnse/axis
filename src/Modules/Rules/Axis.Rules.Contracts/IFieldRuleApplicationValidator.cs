namespace Axis.Rules.Contracts;

public interface IFieldRuleApplicationValidator
{
    FieldRuleApplicationValidationResult ValidateFieldRuleApplication(
        string definitionKey,
        string fieldType,
        IReadOnlyDictionary<string, IReadOnlyList<string>> parameters);
}
