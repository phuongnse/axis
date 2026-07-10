using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application;

public sealed class BusinessObjectDefinitionInputPlanner(IRuleApplicationValidator ruleValidator)
    : IBusinessObjectDefinitionInputPlanner
{
    public async Task<Result<IReadOnlyList<BusinessObjectFieldDefinitionSpec>>> PlanAsync(
        Guid workspaceId,
        IReadOnlyList<BusinessObjectFieldDefinitionInput> fields,
        CancellationToken cancellationToken)
    {
        List<BusinessObjectFieldDefinitionSpec> specs = [];
        for (int index = 0; index < fields.Count; index++)
        {
            BusinessObjectFieldDefinitionInput field = fields[index];
            Result<BusinessObjectChoiceFieldConfigurationSpec?> choice = ToChoiceConfiguration(field);
            if (choice.IsFailure)
                return Result.Failure<IReadOnlyList<BusinessObjectFieldDefinitionSpec>>(choice.Error);

            Result<IReadOnlyList<BusinessObjectFieldRuleSpec>> rules = await PlanRulesAsync(
                workspaceId,
                field,
                choice.Value,
                cancellationToken);
            if (rules.IsFailure)
                return Result.Failure<IReadOnlyList<BusinessObjectFieldDefinitionSpec>>(rules.Error);

            specs.Add(new BusinessObjectFieldDefinitionSpec(
                field.FieldKey,
                field.Label,
                index,
                field.FieldType,
                rules.Value,
                choice.Value,
                field.Id is Guid fieldId ? BusinessObjectFieldDefinitionId.From(fieldId) : null));
        }

        return specs;
    }

    private async Task<Result<IReadOnlyList<BusinessObjectFieldRuleSpec>>> PlanRulesAsync(
        Guid workspaceId,
        BusinessObjectFieldDefinitionInput field,
        BusinessObjectChoiceFieldConfigurationSpec? choice,
        CancellationToken cancellationToken)
    {
        if (field.Rules is null || field.Rules.Count == 0)
            return Array.Empty<BusinessObjectFieldRuleSpec>();

        string contextKey;
        try
        {
            contextKey = BusinessObjectRuleContextSchemaProvider.ContextKeyFor(
                field.FieldType,
                choice?.SelectionMode);
        }
        catch (InvalidOperationException exception)
        {
            return Result.Failure<IReadOnlyList<BusinessObjectFieldRuleSpec>>(exception.Message);
        }

        Dictionary<string, IReadOnlyList<string>> targetConfiguration = new(StringComparer.Ordinal);
        if (choice is not null)
            targetConfiguration["selection_mode"] = [choice.SelectionMode.ToString()];

        List<BusinessObjectFieldRuleSpec> rules = [];
        foreach (BusinessObjectFieldRuleInput rule in field.Rules)
        {
            IReadOnlyDictionary<string, IReadOnlyList<string>> parameters =
                rule.Parameters ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            RuleApplicationValidationResult validation = await ruleValidator.ValidateAsync(
                new RuleApplicationValidationRequest(
                    workspaceId,
                    rule.DefinitionKey,
                    rule.DefinitionVersion,
                    new RuleApplicationTarget(
                        RuleScope.Field,
                        contextKey,
                        ContextSchemaVersion: 1,
                        field.FieldType.ToString(),
                        targetConfiguration),
                    parameters),
                cancellationToken);
            if (!validation.IsValid)
                return Result.Failure<IReadOnlyList<BusinessObjectFieldRuleSpec>>(validation.Error!);

            rules.Add(new BusinessObjectFieldRuleSpec(
                rule.DefinitionKey,
                rule.DefinitionVersion,
                validation.CanonicalParameters!,
                rule.Id is Guid ruleId ? BusinessObjectFieldRuleId.From(ruleId) : null));
        }

        return rules;
    }

    private static Result<BusinessObjectChoiceFieldConfigurationSpec?> ToChoiceConfiguration(
        BusinessObjectFieldDefinitionInput field)
    {
        if (field.FieldType != BusinessObjectFieldType.Choice)
        {
            return field.ChoiceConfiguration is null
                ? Result.Success<BusinessObjectChoiceFieldConfigurationSpec?>(null)
                : Result.Failure<BusinessObjectChoiceFieldConfigurationSpec?>(
                    "Choice configuration is only valid for Choice fields.");
        }

        if (field.ChoiceConfiguration is null)
            return Result.Failure<BusinessObjectChoiceFieldConfigurationSpec?>("Choice configuration is required.");

        return new BusinessObjectChoiceFieldConfigurationSpec(
            field.ChoiceConfiguration.SelectionMode,
            field.ChoiceConfiguration.Options
                .Select((option, index) => new BusinessObjectChoiceOptionSpec(
                    option.OptionKey,
                    option.Label,
                    index,
                    option.Id is Guid optionId ? BusinessObjectChoiceOptionId.From(optionId) : null))
                .ToArray());
    }
}
