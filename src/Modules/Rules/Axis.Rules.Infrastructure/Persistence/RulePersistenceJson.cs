using System.Text.Json;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Contracts = Axis.Rules.Contracts;
using DomainDecision = Axis.Rules.Domain.RuleDecision;
using DomainLogicalOperator = Axis.Rules.Domain.RuleLogicalOperator;
using DomainOperandKind = Axis.Rules.Domain.RuleOperandKind;
using DomainOutcomeKind = Axis.Rules.Domain.RuleOutcomeKind;
using DomainPredicateOperator = Axis.Rules.Domain.RulePredicateOperator;
using DomainSeverity = Axis.Rules.Domain.RuleSeverity;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Infrastructure.Persistence;

internal static class RulePersistenceJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string SerializeParameters(IReadOnlyList<RuleParameterDefinition> parameters) =>
        JsonSerializer.Serialize(parameters.Select(ToDto).ToArray(), Options);

    public static List<RuleParameterDefinition> DeserializeParameters(string json) =>
        (JsonSerializer.Deserialize<RuleParameterDefinitionDto[]>(json, Options) ?? [])
        .Select(ToDomain)
        .ToList();

    public static string SerializeCondition(RuleConditionNode? condition) =>
        JsonSerializer.Serialize(condition is null ? null : ToDto(condition), Options);

    public static RuleConditionNode? DeserializeCondition(string json)
    {
        RuleConditionNodeDto? dto = JsonSerializer.Deserialize<RuleConditionNodeDto>(json, Options);
        return dto is null ? null : ToDomain(dto);
    }

    public static string SerializeOutcome(RuleOutcome? outcome) =>
        JsonSerializer.Serialize(outcome is null ? null : ToDto(outcome), Options);

    public static RuleOutcome? DeserializeOutcome(string json)
    {
        RuleOutcomeDto? dto = JsonSerializer.Deserialize<RuleOutcomeDto>(json, Options);
        return dto is null ? null : ToDomain(dto);
    }

    private static RuleParameterDefinitionDto ToDto(RuleParameterDefinition parameter) =>
        new(
            parameter.Key,
            (Contracts.RuleValueType)parameter.Type,
            parameter.IsRequired,
            parameter.AllowMultiple,
            parameter.AllowedValues);

    private static RuleParameterDefinition ToDomain(RuleParameterDefinitionDto parameter)
    {
        Shared.Domain.Primitives.Result<RuleParameterDefinition> result = RuleParameterDefinition.Create(
            parameter.Key,
            (DomainValueType)parameter.Type,
            parameter.IsRequired,
            parameter.AllowMultiple,
            parameter.AllowedValues);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error);
    }

    private static RuleConditionNodeDto ToDto(RuleConditionNode node) => node switch
    {
        RuleConditionGroup group => new RuleConditionNodeDto(
            group.NodeId,
            (Contracts.RuleLogicalOperator)group.Operator,
            null,
            null,
            null,
            group.Children.Select(ToDto).ToArray()),
        RulePredicateCondition predicate => new RuleConditionNodeDto(
            predicate.NodeId,
            null,
            (Contracts.RulePredicateOperator)predicate.Operator,
            ToDto(predicate.Left),
            predicate.Right is null ? null : ToDto(predicate.Right),
            []),
        _ => throw new InvalidOperationException("Rule condition node type is not supported."),
    };

    private static RuleConditionNode ToDomain(RuleConditionNodeDto node)
    {
        bool isGroup = node.LogicalOperator is not null &&
            node.PredicateOperator is null &&
            node.Left is null &&
            node.Right is null &&
            node.Children is not null;
        bool isPredicate = node.LogicalOperator is null &&
            node.PredicateOperator is not null &&
            node.Left is not null &&
            node.Children is not null &&
            node.Children.Count == 0;
        if (!isGroup && !isPredicate)
            throw new InvalidOperationException("Persisted rule condition shape is invalid.");

        if (isGroup)
        {
            if (node.Children!.Any(child => child is null))
                throw new InvalidOperationException("Persisted rule condition shape is invalid.");

            RuleConditionNode[] children = node.Children!.Select(ToDomain).ToArray();
            Shared.Domain.Primitives.Result<RuleConditionGroup> group = RuleConditionGroup.Create(
                node.NodeId,
                (DomainLogicalOperator)node.LogicalOperator!.Value,
                children);
            return group.IsSuccess ? group.Value : throw new InvalidOperationException(group.Error);
        }

        RuleOperand left = ToDomain(node.Left!);
        RuleOperand? right = node.Right is null ? null : ToDomain(node.Right);
        Shared.Domain.Primitives.Result<RulePredicateCondition> predicate = RulePredicateCondition.Create(
            node.NodeId,
            (DomainPredicateOperator)node.PredicateOperator!.Value,
            left,
            right);
        return predicate.IsSuccess ? predicate.Value : throw new InvalidOperationException(predicate.Error);
    }

    private static RuleOperandDto ToDto(RuleOperand operand) =>
        new(
            (Contracts.RuleOperandKind)operand.Kind,
            operand.Reference,
            operand.Literal is null
                ? null
                : new RuleValueDto(
                    (Contracts.RuleValueType)operand.Literal.Type,
                    operand.Literal.Values));

    private static RuleOperand ToDomain(RuleOperandDto operand)
    {
        Shared.Domain.Primitives.Result<RuleOperand> result = (DomainOperandKind)operand.Kind switch
        {
            DomainOperandKind.Context => RuleOperand.Context(operand.Reference ?? string.Empty),
            DomainOperandKind.Parameter => RuleOperand.Parameter(operand.Reference ?? string.Empty),
            DomainOperandKind.Literal => Literal(operand.Literal),
            _ => throw new InvalidOperationException("Persisted rule operand kind is invalid."),
        };
        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error);
    }

    private static Axis.Shared.Domain.Primitives.Result<RuleOperand> Literal(RuleValueDto? literal)
    {
        if (literal is null)
            return Axis.Shared.Domain.Primitives.Result.Failure<RuleOperand>("Persisted rule literal is missing.");

        Shared.Domain.Primitives.Result<RuleValue> value = RuleValue.Create((DomainValueType)literal.Type, literal.Values, allowMultiple: true);
        return value.IsSuccess
            ? RuleOperand.LiteralValue(value.Value)
            : Axis.Shared.Domain.Primitives.Result.Failure<RuleOperand>(value.Error);
    }

    private static RuleOutcomeDto ToDto(RuleOutcome outcome) => outcome switch
    {
        RuleValidationOutcome validation => new RuleOutcomeDto(
            Contracts.RuleOutcomeKind.Validation,
            validation.Code,
            (Contracts.RuleSeverity)validation.Severity,
            validation.Message,
            null),
        RuleDecisionOutcome decision => new RuleOutcomeDto(
            Contracts.RuleOutcomeKind.Decision,
            null,
            null,
            null,
            (Contracts.RuleDecision)decision.Decision),
        _ => throw new InvalidOperationException("Rule outcome type is not supported."),
    };

    private static RuleOutcome ToDomain(RuleOutcomeDto outcome)
    {
        if ((DomainOutcomeKind)outcome.Kind == DomainOutcomeKind.Validation && outcome.Severity is not null)
        {
            Shared.Domain.Primitives.Result<RuleValidationOutcome> validation = RuleValidationOutcome.Create(
                outcome.ViolationCode ?? string.Empty,
                (DomainSeverity)outcome.Severity.Value,
                outcome.Message ?? string.Empty);
            return validation.IsSuccess
                ? validation.Value
                : throw new InvalidOperationException(validation.Error);
        }

        if ((DomainOutcomeKind)outcome.Kind == DomainOutcomeKind.Decision && outcome.Decision is not null)
        {
            Shared.Domain.Primitives.Result<RuleDecisionOutcome> decision = RuleDecisionOutcome.Create((DomainDecision)outcome.Decision.Value);
            return decision.IsSuccess
                ? decision.Value
                : throw new InvalidOperationException(decision.Error);
        }

        throw new InvalidOperationException("Persisted rule outcome shape is invalid.");
    }
}
