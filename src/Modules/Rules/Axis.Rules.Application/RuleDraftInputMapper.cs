using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using ContractOperandKind = Axis.Rules.Contracts.RuleOperandKind;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application;

internal sealed record RuleDraftInput(
    IReadOnlyList<RuleInputDefinition> Inputs,
    RuleConditionNode Condition);

internal static class RuleDraftInputMapper
{
    public static Result<RuleDraftInput> Map(
        IReadOnlyList<RuleDraftInputDefinitionDto> inputs,
        RuleConditionNodeDto condition)
    {
        List<RuleInputDefinition> mappedInputs = [];
        foreach (RuleDraftInputDefinitionDto inputDto in inputs)
        {
            Result<RuleInputDefinition> input = RuleInputDefinition.Create(
                inputDto.Key,
                inputDto.Label,
                inputDto.Types.Select(type => (DomainValueType)type).ToArray(),
                inputDto.IsRequired,
                inputDto.AllowMultiple,
                inputDto.AllowedValues);
            if (input.IsFailure)
                return Result.Failure<RuleDraftInput>(input.Error);
            mappedInputs.Add(input.Value);
        }

        if (mappedInputs.Select(input => input.Key).Distinct(StringComparer.Ordinal).Count() !=
            mappedInputs.Count)
        {
            return Result.Failure<RuleDraftInput>("Rule input keys must be unique.");
        }
        Result<RuleConditionNodeDto> canonicalCondition = CanonicalizeCondition(condition);
        if (canonicalCondition.IsFailure)
            return Result.Failure<RuleDraftInput>(canonicalCondition.Error);

        Result<RuleConditionNode> mappedCondition = RuleContractMapper.ToDomain(canonicalCondition.Value);
        if (mappedCondition.IsFailure)
            return Result.Failure<RuleDraftInput>(mappedCondition.Error);

        return new RuleDraftInput(mappedInputs, mappedCondition.Value);
    }

    private static Result<RuleConditionNodeDto> CanonicalizeCondition(
        RuleConditionNodeDto condition)
    {
        if (condition is null)
            return Result.Failure<RuleConditionNodeDto>("Rule condition is required.");

        if (condition.LogicalOperator is not null)
        {
            List<RuleConditionNodeDto> children = [];
            foreach (RuleConditionNodeDto child in condition.Children ?? [])
            {
                Result<RuleConditionNodeDto> canonicalChild = CanonicalizeCondition(child);
                if (canonicalChild.IsFailure)
                    return Result.Failure<RuleConditionNodeDto>(canonicalChild.Error);
                children.Add(canonicalChild.Value);
            }
            return condition with { Children = children };
        }

        if (condition.Left is null)
            return Result.Failure<RuleConditionNodeDto>("Rule condition left value is required.");

        Result<RuleOperandDto> left = CanonicalizeOperand(condition.Left);
        if (left.IsFailure)
            return Result.Failure<RuleConditionNodeDto>(left.Error);
        Result<RuleOperandDto>? right = condition.Right is null
            ? null
            : CanonicalizeOperand(condition.Right);
        if (right?.IsFailure == true)
            return Result.Failure<RuleConditionNodeDto>(right.Error);

        return condition with { Left = left.Value, Right = right?.Value };
    }

    private static Result<RuleOperandDto> CanonicalizeOperand(
        RuleOperandDto operand)
    {
        if (operand.Kind == ContractOperandKind.Input)
        {
            return operand with { Reference = operand.Reference?.Trim() };
        }

        List<RuleOperandDto> arguments = [];
        foreach (RuleOperandDto argument in operand.Arguments ?? [])
        {
            Result<RuleOperandDto> canonicalArgument = CanonicalizeOperand(argument);
            if (canonicalArgument.IsFailure)
                return Result.Failure<RuleOperandDto>(canonicalArgument.Error);
            arguments.Add(canonicalArgument.Value);
        }
        return operand with { Arguments = arguments };
    }
}
