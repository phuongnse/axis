using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Application;

internal sealed record RuleDraftInput(
    IReadOnlyList<RuleParameterDefinition> Parameters,
    RuleConditionNode Condition,
    RuleOutcome Outcome);

internal static class RuleDraftInputMapper
{
    public static Result<RuleDraftInput> Map(
        IReadOnlyList<RuleParameterDefinitionDto> parameters,
        RuleConditionNodeDto condition,
        RuleOutcomeDto outcome)
    {
        List<RuleParameterDefinition> mappedParameters = [];
        foreach (RuleParameterDefinitionDto parameterDto in parameters)
        {
            Result<RuleParameterDefinition> parameter = RuleContractMapper.ToDomain(parameterDto);
            if (parameter.IsFailure)
                return Result.Failure<RuleDraftInput>(parameter.Error);
            mappedParameters.Add(parameter.Value);
        }

        Result<RuleConditionNode> mappedCondition = RuleContractMapper.ToDomain(condition);
        if (mappedCondition.IsFailure)
            return Result.Failure<RuleDraftInput>(mappedCondition.Error);

        Result<RuleOutcome> mappedOutcome = RuleContractMapper.ToDomain(outcome);
        if (mappedOutcome.IsFailure)
            return Result.Failure<RuleDraftInput>(mappedOutcome.Error);

        return new RuleDraftInput(mappedParameters, mappedCondition.Value, mappedOutcome.Value);
    }
}
