using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Application;

internal sealed record BusinessObjectRecordRuleContext(
    BusinessObjectFieldType FieldType,
    IReadOnlyList<string> Values);

internal sealed class BusinessObjectRecordRuleContextAdapter
    : IRuleContextAdapter<BusinessObjectRecordRuleContext>
{
    public string TargetType => BusinessObjectRecordRuleBindingContract.TargetType;

    public RuleContext CreateContext(BusinessObjectRecordRuleContext consumerContext)
    {
        if (consumerContext.Values.Count == 0)
            return new RuleContext(new Dictionary<string, RuleContextValue>(StringComparer.Ordinal));

        RuleValueType type = consumerContext.FieldType == BusinessObjectFieldType.Choice
            ? RuleValueType.Text
            : (RuleValueType)consumerContext.FieldType;
        return new RuleContext(
            new Dictionary<string, RuleContextValue>(StringComparer.Ordinal)
            {
                ["record.value"] = new RuleContextValue(type, consumerContext.Values),
            });
    }
}

public sealed class BusinessObjectRecordRuleEvaluator(IRuleBindingEvaluator bindingEvaluator)
{
    private readonly BusinessObjectRecordRuleContextAdapter _adapter = new();

    public async Task<Result<IReadOnlyList<BusinessObjectRecordRuleEvaluation>>> EvaluateAsync(
        Guid workspaceId,
        BusinessObjectDefinitionVersion definition,
        IReadOnlyDictionary<string, IReadOnlyList<string>> values,
        CancellationToken cancellationToken)
    {
        List<BusinessObjectRecordRuleEvaluation> evaluations = [];
        foreach (BusinessObjectDefinitionVersionField field in definition.Fields.OrderBy(field => field.Order))
        {
            IReadOnlyList<string> fieldValues = values.TryGetValue(field.Key.Value, out IReadOnlyList<string>? value)
                ? value
                : [];
            foreach (BusinessObjectDefinitionVersionFieldRule rule in field.Rules.OrderBy(rule => rule.Order))
            {
                RuleEvaluationResult result = await bindingEvaluator.EvaluateBindingAsync(
                    new RuleBindingEvaluationRequest(
                        workspaceId,
                        rule.BindingId,
                        _adapter.CreateContext(new BusinessObjectRecordRuleContext(field.FieldType, fieldValues)),
                        $"business-object-record:{Guid.NewGuid():N}",
                        rule.BindingRevision),
                    cancellationToken);
                if (!result.IsSuccess)
                {
                    return Result.Failure<IReadOnlyList<BusinessObjectRecordRuleEvaluation>>(
                        ErrorCodes.BusinessRule,
                        result.Error ?? "Rule evaluation failed.");
                }

                RuleEvaluationItemDto? item = result.Items.SingleOrDefault();
                if (item is null)
                {
                    return Result.Failure<IReadOnlyList<BusinessObjectRecordRuleEvaluation>>(
                        ErrorCodes.BusinessRule,
                        "Rule evaluation returned no result.");
                }

                evaluations.Add(new BusinessObjectRecordRuleEvaluation(
                    field.Key.Value,
                    rule.BindingId,
                    rule.BindingRevision,
                    item.DefinitionKey,
                    item.DefinitionVersion,
                    item.IsMatch,
                    item.Diagnostics
                        .Select(diagnostic => new BusinessObjectRecordRuleDiagnostic(
                            diagnostic.NodeId,
                            diagnostic.IsMatch))
                        .ToArray()));
            }
        }

        return evaluations;
    }
}
