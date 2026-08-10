using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using ContractExpressionCardinality = Axis.Rules.Contracts.RuleExpressionCardinality;
using ContractExpressionFunction = Axis.Rules.Contracts.RuleExpressionFunction;
using ContractLifecycleStatus = Axis.Rules.Contracts.RuleLifecycleStatus;
using ContractLogicalOperator = Axis.Rules.Contracts.RuleLogicalOperator;
using ContractOperandKind = Axis.Rules.Contracts.RuleOperandKind;
using ContractOrigin = Axis.Rules.Contracts.RuleOrigin;
using ContractPredicateOperator = Axis.Rules.Contracts.RulePredicateOperator;
using ContractValueType = Axis.Rules.Contracts.RuleValueType;
using DomainExpressionCardinality = Axis.Rules.Domain.RuleExpressionCardinality;
using DomainExpressionFunction = Axis.Rules.Domain.RuleExpressionFunction;
using DomainLogicalOperator = Axis.Rules.Domain.RuleLogicalOperator;
using DomainOperandKind = Axis.Rules.Domain.RuleOperandKind;
using DomainOrigin = Axis.Rules.Domain.RuleOrigin;
using DomainPredicateOperator = Axis.Rules.Domain.RulePredicateOperator;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application;

internal static class RuleContractMapper
{
    public static RuleDefinitionSummaryDto ToSummaryDto(RuleDefinition definition, bool canManage = false) =>
        new(
            definition.Key.Value,
            definition.Name,
            definition.Description,
            (ContractOrigin)definition.Origin,
            ToLifecycleStatus(definition),
            definition.ExpressionLanguageVersion,
            definition.Origin == DomainOrigin.BuiltIn ? null : definition.Revision,
            definition.LatestPublishedVersion,
            definition.ActiveVersion,
            definition.Inputs.Select(ToDto).ToArray(),
            ToDto(definition.Output),
            definition.Origin == DomainOrigin.BuiltIn ? null : definition.UpdatedAt,
            ToActions(definition, canManage),
            definition.Documentation is null ? null : ToDto(definition.Documentation));

    public static RuleDefinitionDetailDto ToDetailDto(RuleDefinition definition, bool canManage = false) =>
        new(
            definition.Key.Value,
            definition.Name,
            definition.Description,
            (ContractOrigin)definition.Origin,
            ToLifecycleStatus(definition),
            definition.ExpressionLanguageVersion,
            definition.Origin == DomainOrigin.BuiltIn ? null : definition.Revision,
            definition.LatestPublishedVersion,
            definition.ActiveVersion,
            definition.Inputs.Select(ToDto).ToArray(),
            ToDto(definition.Output),
            definition.Condition is null ? null : ToDto(definition.Condition),
            definition.Versions.OrderBy(version => version.Version).Select(ToDto).ToArray(),
            definition.Origin == DomainOrigin.BuiltIn ? null : definition.CreatedAt,
            definition.Origin == DomainOrigin.BuiltIn ? null : definition.UpdatedAt,
            definition.ArchivedAt,
            ToActions(definition, canManage),
            definition.Documentation is null ? null : ToDto(definition.Documentation));

    public static RuleDefinitionVersionDto ToDto(RuleDefinitionVersion version) =>
        new(
            version.Version,
            version.Name,
            version.Description,
            version.ExpressionLanguageVersion,
            version.Inputs.Select(ToDto).ToArray(),
            ToDto(version.Output),
            ToDto(version.Condition),
            version.PublishedBySubject is { } subject ? RuleSubjectReferenceMapper.ToDto(subject) : null,
            version.PublishedAt);

    private static ContractLifecycleStatus ToLifecycleStatus(RuleDefinition definition) =>
        definition.ArchivedAt is not null
            ? ContractLifecycleStatus.Archived
            : definition.ActiveVersion is not null
                ? ContractLifecycleStatus.Active
                : definition.LatestPublishedVersion is not null
                    ? ContractLifecycleStatus.Inactive
                    : ContractLifecycleStatus.Draft;

    private static RuleDefinitionActionsDto ToActions(RuleDefinition definition, bool canManage)
    {
        bool mutable = canManage && definition.Origin == DomainOrigin.Workspace && definition.ArchivedAt is null;
        return new RuleDefinitionActionsDto(
            CanEditDraft: mutable,
            CanCreateVersion: mutable && definition.Condition is not null,
            CanActivateVersion: mutable && definition.LatestPublishedVersion is not null,
            CanDeactivate: mutable && definition.ActiveVersion is not null,
            CanArchive: mutable);
    }

    public static RuleExpressionLanguageDto ToExpressionLanguageDto() =>
        new(
            RuleExpressionLanguage.Version,
            RuleExpressionLanguage.Operators.Select(ToDto).ToArray(),
            RuleExpressionLanguage.Functions.Select(ToDto).ToArray(),
            RuleExpressionLanguage.LogicalOperators.Select(ToDto).ToArray(),
            RuleExpressionLanguage.OperandKinds.Select(ToDto).ToArray(),
            RuleExpressionLanguage.ValueTypes.Select(ToDto).ToArray(),
            RuleExpressionLanguage.Cardinalities.Select(ToDto).ToArray(),
            RuleExpressionLanguage.Limits.Select(ToDto).ToArray(),
            new RuleExpressionLimitsDto(
                RuleEvaluationLimits.Default.MaxDepth,
                RuleEvaluationLimits.Default.MaxNodes,
                RuleEvaluationLimits.Default.MaxFunctionCalls,
                RuleEvaluationLimits.Default.MaxInputs,
                RuleEvaluationLimits.Default.MaxExecutionSteps));

    public static RuleConditionNodeDto ToDto(RuleConditionNode node) => node switch
    {
        RuleConditionGroup group => new(
            group.NodeId,
            (ContractLogicalOperator)group.Operator,
            null,
            null,
            null,
            group.Children.Select(ToDto).ToArray()),
        RulePredicateCondition predicate => new(
            predicate.NodeId,
            null,
            (ContractPredicateOperator)predicate.Operator,
            ToDto(predicate.Left),
            predicate.Right is null ? null : ToDto(predicate.Right),
            []),
        _ => throw new InvalidOperationException("Rule condition node type is not supported."),
    };

    public static RuleOperandDto ToDto(RuleOperand operand) =>
        new(
            (ContractOperandKind)operand.Kind,
            operand.Reference,
            operand.Literal is null ? null : ToDto(operand.Literal),
            operand.FunctionKind is null ? null : (ContractExpressionFunction)operand.FunctionKind.Value,
            operand.Arguments.Select(ToDto).ToArray());

    public static RuleInputDefinitionDto ToDto(RuleInputDefinition input) =>
        new(
            input.Key,
            input.Label,
            input.Types.Select(type => (ContractValueType)type).ToArray(),
            input.IsRequired,
            input.AllowMultiple,
            input.AllowedValues);

    public static RuleValueDto ToDto(RuleValue value) =>
        new((ContractValueType)value.Type, value.Values);

    public static RuleOutputContractDto ToDto(RuleOutputContract output) =>
        new((ContractValueType)output.Type, (ContractExpressionCardinality)output.Cardinality);

    public static Result<RuleInputDefinition> ToDomain(RuleInputDefinitionDto input) =>
        RuleInputDefinition.Restore(
            input.Key,
            input.Label,
            input.Types.Select(type => (DomainValueType)type).ToArray(),
            input.IsRequired,
            input.AllowMultiple,
            input.AllowedValues);

    public static Result<RuleValue> ToDomain(RuleValueDto value, bool allowMultiple = true) =>
        RuleValue.Create((DomainValueType)value.Type, value.Values, allowMultiple);

    public static Result<RuleConditionNode> ToDomain(RuleConditionNodeDto node)
    {
        if (node.LogicalOperator is not null)
        {
            List<RuleConditionNode> children = [];
            foreach (RuleConditionNodeDto childDto in node.Children)
            {
                Result<RuleConditionNode> child = ToDomain(childDto);
                if (child.IsFailure)
                    return Result.Failure<RuleConditionNode>(child.Error);
                children.Add(child.Value);
            }

            Result<RuleConditionGroup> group = RuleConditionGroup.Create(
                node.NodeId,
                (DomainLogicalOperator)node.LogicalOperator.Value,
                children);
            return group.IsSuccess ? group.Value : Result.Failure<RuleConditionNode>(group.Error);
        }

        if (node.PredicateOperator is null || node.Left is null)
            return Result.Failure<RuleConditionNode>("Rule condition node shape is invalid.");

        Result<RuleOperand> left = ToDomain(node.Left);
        if (left.IsFailure)
            return Result.Failure<RuleConditionNode>(left.Error);

        RuleOperand? right = null;
        if (node.Right is not null)
        {
            Result<RuleOperand> mappedRight = ToDomain(node.Right);
            if (mappedRight.IsFailure)
                return Result.Failure<RuleConditionNode>(mappedRight.Error);
            right = mappedRight.Value;
        }

        Result<RulePredicateCondition> predicate = RulePredicateCondition.Create(
            node.NodeId,
            (DomainPredicateOperator)node.PredicateOperator.Value,
            left.Value,
            right);
        return predicate.IsSuccess ? predicate.Value : Result.Failure<RuleConditionNode>(predicate.Error);
    }

    public static Result<RuleOperand> ToDomain(RuleOperandDto operand) => operand.Kind switch
    {
        ContractOperandKind.Input => RuleOperand.Input(operand.Reference ?? string.Empty),
        ContractOperandKind.Literal => ToDomainLiteral(operand.Literal),
        ContractOperandKind.Function => ToDomainFunction(operand),
        _ => Result.Failure<RuleOperand>("Rule operand kind is not supported."),
    };

    private static RulePredicateOperatorDefinitionDto ToDto(RulePredicateOperatorDefinition definition) =>
        new(
            (ContractPredicateOperator)definition.Operator,
            definition.LeftShapes.Select(ToDto).ToArray(),
            definition.RightShapes.Select(ToDto).ToArray(),
            definition.RequiresMatchingTypes,
            ToDto(definition.Documentation));

    private static RuleExpressionValueShapeDto ToDto(RuleExpressionValueShape shape) =>
        new((ContractValueType)shape.Type, (ContractExpressionCardinality)shape.Cardinality);

    private static RuleExpressionFunctionDefinitionDto ToDto(RuleExpressionFunctionDefinition definition) =>
        new(
            (ContractExpressionFunction)definition.Function,
            definition.Parameters.Select(ToDto).ToArray(),
            (ContractValueType)definition.ReturnType,
            (ContractExpressionCardinality)definition.ReturnCardinality,
            ToDto(definition.Documentation));

    private static RuleExpressionFunctionParameterDto ToDto(RuleExpressionFunctionParameter parameter) =>
        new(
            parameter.AcceptedTypes.Select(type => (ContractValueType)type).ToArray(),
            (ContractExpressionCardinality)parameter.Cardinality);

    private static RuleLogicalOperatorDefinitionDto ToDto(RuleLogicalOperatorDefinition definition) =>
        new(
            (ContractLogicalOperator)definition.Operator,
            definition.MinimumChildren,
            definition.MaximumChildren,
            ToDto(definition.Documentation));

    private static RuleOperandKindDefinitionDto ToDto(RuleOperandKindDefinition definition) =>
        new((ContractOperandKind)definition.Kind, ToDto(definition.Documentation));

    private static RuleValueTypeDefinitionDto ToDto(RuleValueTypeDefinition definition) =>
        new((ContractValueType)definition.Type, ToDto(definition.Documentation));

    private static RuleExpressionCardinalityDefinitionDto ToDto(RuleExpressionCardinalityDefinition definition) =>
        new((ContractExpressionCardinality)definition.Cardinality, ToDto(definition.Documentation));

    private static RuleExpressionLimitDefinitionDto ToDto(RuleExpressionLimitDefinition definition) =>
        new(definition.Key, definition.Value, ToDto(definition.Documentation));

    private static RuleReferenceDocumentationDto ToDto(RuleReferenceDocumentation documentation) =>
        new(documentation.Locales.ToDictionary(
            entry => entry.Key,
            entry => new RuleReferenceContentDto(
                entry.Value.DisplayName,
                entry.Value.Summary,
                entry.Value.Usage,
                entry.Value.Examples),
            StringComparer.OrdinalIgnoreCase));

    private static Result<RuleOperand> ToDomainLiteral(RuleValueDto? literal)
    {
        if (literal is null)
            return Result.Failure<RuleOperand>("Rule literal value is required.");

        Result<RuleValue> value = ToDomain(literal);
        return value.IsSuccess ? RuleOperand.LiteralValue(value.Value) : Result.Failure<RuleOperand>(value.Error);
    }

    private static Result<RuleOperand> ToDomainFunction(RuleOperandDto operand)
    {
        if (operand.Function is null || operand.Arguments is null)
            return Result.Failure<RuleOperand>("Rule expression function shape is invalid.");

        List<RuleOperand> arguments = [];
        foreach (RuleOperandDto argumentDto in operand.Arguments)
        {
            Result<RuleOperand> argument = ToDomain(argumentDto);
            if (argument.IsFailure)
                return argument;
            arguments.Add(argument.Value);
        }

        return RuleOperand.Function((DomainExpressionFunction)operand.Function.Value, arguments);
    }
}
