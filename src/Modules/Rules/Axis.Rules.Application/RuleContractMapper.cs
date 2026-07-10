using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using ContractDecision = Axis.Rules.Contracts.RuleDecision;
using ContractLifecycleStatus = Axis.Rules.Contracts.RuleLifecycleStatus;
using ContractLogicalOperator = Axis.Rules.Contracts.RuleLogicalOperator;
using ContractOperandKind = Axis.Rules.Contracts.RuleOperandKind;
using ContractOrigin = Axis.Rules.Contracts.RuleOrigin;
using ContractOutcomeKind = Axis.Rules.Contracts.RuleOutcomeKind;
using ContractPredicateOperator = Axis.Rules.Contracts.RulePredicateOperator;
using ContractScope = Axis.Rules.Contracts.RuleScope;
using ContractSeverity = Axis.Rules.Contracts.RuleSeverity;
using ContractValueType = Axis.Rules.Contracts.RuleValueType;
using DomainDecision = Axis.Rules.Domain.RuleDecision;
using DomainLogicalOperator = Axis.Rules.Domain.RuleLogicalOperator;
using DomainOperandKind = Axis.Rules.Domain.RuleOperandKind;
using DomainOutcomeKind = Axis.Rules.Domain.RuleOutcomeKind;
using DomainPredicateOperator = Axis.Rules.Domain.RulePredicateOperator;
using DomainScope = Axis.Rules.Domain.RuleScope;
using DomainSeverity = Axis.Rules.Domain.RuleSeverity;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application;

internal static class RuleContractMapper
{
    public static RuleDefinitionSummaryDto ToSummaryDto(SystemRuleDefinition definition) =>
        new(
            definition.Key.Value,
            definition.DisplayName,
            definition.Description,
            ContractOrigin.System,
            (ContractScope)definition.Scope,
            (ContractOutcomeKind)definition.OutcomeKind,
            ContractLifecycleStatus.Published,
            Revision: null,
            definition.Version,
            ContextKey: null,
            ContextSchemaVersion: null,
            ToDto(definition.Applicability),
            definition.Parameters.Select(ToDto).ToArray(),
            UpdatedAt: null);

    public static RuleDefinitionSummaryDto ToSummaryDto(RuleDefinition definition) =>
        new(
            definition.Key.Value,
            definition.Name,
            definition.Description,
            ContractOrigin.Workspace,
            (ContractScope)definition.Scope,
            (ContractOutcomeKind)definition.OutcomeKind,
            (ContractLifecycleStatus)definition.Status,
            definition.Revision,
            definition.LatestPublishedVersion,
            definition.ContextKey.Value,
            definition.ContextSchemaVersion,
            Applicability: null,
            definition.Parameters.Select(ToDto).ToArray(),
            definition.UpdatedAt);

    public static RuleDefinitionDetailDto ToDetailDto(RuleDefinition definition) =>
        new(
            definition.Key.Value,
            definition.Name,
            definition.Description,
            ContractOrigin.Workspace,
            (ContractScope)definition.Scope,
            (ContractOutcomeKind)definition.OutcomeKind,
            (ContractLifecycleStatus)definition.Status,
            definition.Revision,
            definition.LatestPublishedVersion,
            definition.ContextKey.Value,
            definition.ContextSchemaVersion,
            definition.Parameters.Select(ToDto).ToArray(),
            definition.Condition is null ? null : ToDto(definition.Condition),
            definition.Outcome is null ? null : ToDto(definition.Outcome),
            definition.Versions.OrderBy(version => version.Version).Select(ToDto).ToArray(),
            definition.CreatedAt,
            definition.UpdatedAt,
            definition.ArchivedAt);

    public static RuleDefinitionVersionDto ToDto(RuleDefinitionVersion version) =>
        new(
            version.Version,
            version.Name,
            version.Description,
            (ContractScope)version.Scope,
            (ContractOutcomeKind)version.OutcomeKind,
            version.ContextKey.Value,
            version.ContextSchemaVersion,
            version.Parameters.Select(ToDto).ToArray(),
            ToDto(version.Condition),
            ToDto(version.Outcome),
            version.PublishedByUserId,
            version.PublishedAt);

    public static RuleConditionNodeDto ToDto(RuleConditionNode node) => node switch
    {
        RuleConditionGroup group => new RuleConditionNodeDto(
            group.NodeId,
            (ContractLogicalOperator)group.Operator,
            PredicateOperator: null,
            Left: null,
            Right: null,
            group.Children.Select(ToDto).ToArray()),
        RulePredicateCondition predicate => new RuleConditionNodeDto(
            predicate.NodeId,
            LogicalOperator: null,
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
            operand.Literal is null ? null : ToDto(operand.Literal));

    public static RuleOutcomeDto ToDto(RuleOutcome outcome) => outcome switch
    {
        RuleValidationOutcome validation => new RuleOutcomeDto(
            ContractOutcomeKind.Validation,
            validation.Code,
            (ContractSeverity)validation.Severity,
            validation.Message,
            Decision: null),
        RuleDecisionOutcome decision => new RuleOutcomeDto(
            ContractOutcomeKind.Decision,
            ViolationCode: null,
            Severity: null,
            Message: null,
            (ContractDecision)decision.Decision),
        _ => throw new InvalidOperationException("Rule outcome type is not supported."),
    };

    public static RuleParameterDefinitionDto ToDto(RuleParameterDefinition parameter) =>
        new(
            parameter.Key,
            (ContractValueType)parameter.Type,
            parameter.IsRequired,
            parameter.AllowMultiple,
            parameter.AllowedValues);

    public static RuleValueDto ToDto(RuleValue value) =>
        new((ContractValueType)value.Type, value.Values);

    public static RuleContextSchemaDto ToDto(RuleContextSchema schema) =>
        new(
            schema.Key.Value,
            schema.Version,
            (ContractScope)schema.Scope,
            schema.DisplayName,
            schema.Fields.Select(field => new RuleContextFieldDto(
                field.Path,
                field.DisplayName,
                (ContractValueType)field.Type,
                field.AllowMultiple)).ToArray(),
            schema.TargetTypeKey,
            schema.Configuration);

    public static Result<RuleContextSchema> ToDomain(RuleContextSchemaDto schema)
    {
        List<RuleContextField> fields = [];
        foreach (RuleContextFieldDto fieldDto in schema.Fields)
        {
            Result<RuleContextField> field = RuleContextField.Create(
                fieldDto.Path,
                fieldDto.DisplayName,
                (DomainValueType)fieldDto.Type,
                fieldDto.AllowMultiple);
            if (field.IsFailure)
                return Result.Failure<RuleContextSchema>(field.Error);
            fields.Add(field.Value);
        }

        return RuleContextSchema.Create(
            schema.ContextKey,
            schema.Version,
            (DomainScope)schema.Scope,
            schema.DisplayName,
            fields,
            schema.TargetTypeKey,
            schema.Configuration);
    }

    public static Result<RuleParameterDefinition> ToDomain(RuleParameterDefinitionDto parameter) =>
        RuleParameterDefinition.Create(
            parameter.Key,
            (DomainValueType)parameter.Type,
            parameter.IsRequired,
            parameter.AllowMultiple,
            parameter.AllowedValues);

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
            return group.IsSuccess
                ? group.Value
                : Result.Failure<RuleConditionNode>(group.Error);
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
        return predicate.IsSuccess
            ? predicate.Value
            : Result.Failure<RuleConditionNode>(predicate.Error);
    }

    public static Result<RuleOperand> ToDomain(RuleOperandDto operand) => operand.Kind switch
    {
        ContractOperandKind.Context => RuleOperand.Context(operand.Reference ?? string.Empty),
        ContractOperandKind.Parameter => RuleOperand.Parameter(operand.Reference ?? string.Empty),
        ContractOperandKind.Literal => ToDomainLiteral(operand.Literal),
        _ => Result.Failure<RuleOperand>("Rule operand kind is not supported."),
    };

    public static Result<RuleOutcome> ToDomain(RuleOutcomeDto outcome)
    {
        if (outcome.Kind == ContractOutcomeKind.Validation)
        {
            if (outcome.Severity is null)
                return Result.Failure<RuleOutcome>("Rule validation severity is required.");
            Result<RuleValidationOutcome> validation = RuleValidationOutcome.Create(
                outcome.ViolationCode ?? string.Empty,
                (DomainSeverity)outcome.Severity.Value,
                outcome.Message ?? string.Empty);
            return validation.IsSuccess
                ? validation.Value
                : Result.Failure<RuleOutcome>(validation.Error);
        }

        if (outcome.Kind == ContractOutcomeKind.Decision && outcome.Decision is not null)
        {
            Result<RuleDecisionOutcome> decision = RuleDecisionOutcome.Create(
                (DomainDecision)outcome.Decision.Value);
            return decision.IsSuccess
                ? decision.Value
                : Result.Failure<RuleOutcome>(decision.Error);
        }

        return Result.Failure<RuleOutcome>("Rule outcome shape is invalid.");
    }

    private static RuleApplicabilityDto ToDto(RuleApplicability applicability) =>
        new(applicability.TargetTypeKeys, applicability.ConfigurationConstraints);

    private static Result<RuleOperand> ToDomainLiteral(RuleValueDto? literal)
    {
        if (literal is null)
            return Result.Failure<RuleOperand>("Rule literal value is required.");

        Result<RuleValue> value = ToDomain(literal);
        return value.IsSuccess
            ? RuleOperand.LiteralValue(value.Value)
            : Result.Failure<RuleOperand>(value.Error);
    }
}
