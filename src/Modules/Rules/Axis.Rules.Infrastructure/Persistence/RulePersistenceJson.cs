using System.Text.Json;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using ContractExpressionCardinality = Axis.Rules.Contracts.RuleExpressionCardinality;
using ContractExpressionFunction = Axis.Rules.Contracts.RuleExpressionFunction;
using ContractInputMappingKind = Axis.Rules.Contracts.RuleInputMappingKind;
using ContractLogicalOperator = Axis.Rules.Contracts.RuleLogicalOperator;
using ContractOperandKind = Axis.Rules.Contracts.RuleOperandKind;
using ContractPredicateOperator = Axis.Rules.Contracts.RulePredicateOperator;
using ContractValueType = Axis.Rules.Contracts.RuleValueType;
using DomainBindingFailureBehavior = Axis.Rules.Domain.RuleBindingFailureBehavior;
using DomainExpressionCardinality = Axis.Rules.Domain.RuleExpressionCardinality;
using DomainExpressionFunction = Axis.Rules.Domain.RuleExpressionFunction;
using DomainLogicalOperator = Axis.Rules.Domain.RuleLogicalOperator;
using DomainOperandKind = Axis.Rules.Domain.RuleOperandKind;
using DomainPredicateOperator = Axis.Rules.Domain.RulePredicateOperator;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Infrastructure.Persistence;

internal static class RulePersistenceJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string SerializeInputs(IReadOnlyList<RuleInputDefinition> inputs) =>
        JsonSerializer.Serialize(inputs.Select(ToDto).ToArray(), Options);

    public static List<RuleInputDefinition> DeserializeInputs(string json) =>
        (JsonSerializer.Deserialize<RuleInputDefinitionDto[]>(json, Options) ?? [])
        .Select(ToDomain)
        .ToList();

    public static string SerializeCondition(RuleConditionNode? condition) =>
        JsonSerializer.Serialize(condition is null ? null : ToDto(condition), Options);

    public static RuleConditionNode? DeserializeCondition(string json)
    {
        RuleConditionNodeDto? dto = JsonSerializer.Deserialize<RuleConditionNodeDto>(json, Options);
        return dto is null ? null : ToDomain(dto);
    }

    public static string SerializeOutput(RuleOutputContract output) =>
        JsonSerializer.Serialize(ToDto(output), Options);

    public static string SerializeInputMappings(IReadOnlyDictionary<string, RuleInputMapping> mappings)
    {
        SortedDictionary<string, RuleInputMappingDto> serialized = new(StringComparer.Ordinal);
        foreach ((string key, RuleInputMapping mapping) in mappings)
        {
            serialized.Add(
                key,
                new RuleInputMappingDto(
                    (ContractInputMappingKind)mapping.Kind,
                    mapping.ContextKey,
                    mapping.LiteralValues));
        }
        return JsonSerializer.Serialize(serialized, Options);
    }

    public static Dictionary<string, RuleInputMapping> DeserializeInputMappings(string json)
    {
        Dictionary<string, RuleInputMappingDto> dtos =
            JsonSerializer.Deserialize<Dictionary<string, RuleInputMappingDto>>(json, Options)
            ?? new(StringComparer.Ordinal);
        Dictionary<string, RuleInputMapping> mappings = new(StringComparer.Ordinal);
        foreach ((string key, RuleInputMappingDto dto) in dtos)
        {
            Result<RuleInputMapping> mapping = dto.Kind switch
            {
                ContractInputMappingKind.Context => RuleInputMapping.FromContext(dto.ContextKey ?? string.Empty),
                ContractInputMappingKind.Literal => RuleInputMapping.FromLiteral(dto.LiteralValues),
                _ => Result.Failure<RuleInputMapping>("Persisted rule input mapping kind is invalid."),
            };
            if (mapping.IsFailure)
                throw new InvalidOperationException(mapping.Error);
            mappings.Add(key, mapping.Value);
        }
        return mappings;
    }

    public static string SerializeBindingRevisionHistory(IReadOnlyList<RuleBindingRevision> revisions)
    {
        BindingRevisionDto[] serialized = revisions
            .OrderBy(revision => revision.Revision)
            .Select(revision => new BindingRevisionDto(
                revision.Revision,
                revision.DefinitionKey.Value,
                revision.DefinitionVersion,
                revision.TargetType,
                revision.TargetId,
                revision.UseCaseOrTrigger,
                JsonSerializer.Deserialize<Dictionary<string, RuleInputMappingDto>>(
                    SerializeInputMappings(revision.InputMappings),
                    Options) ?? new(StringComparer.Ordinal),
                revision.Priority,
                revision.Enabled,
                (ContractRuleBindingFailureBehavior)revision.FailureBehavior,
                revision.UpdatedByUserId,
                revision.UpdatedAt))
            .ToArray();
        return JsonSerializer.Serialize(serialized, Options);
    }

    public static List<RuleBindingRevision> DeserializeBindingRevisionHistory(string json)
    {
        BindingRevisionDto[] revisions = JsonSerializer.Deserialize<BindingRevisionDto[]>(json, Options) ?? [];
        List<RuleBindingRevision> result = [];
        foreach (BindingRevisionDto revision in revisions.OrderBy(item => item.Revision))
        {
            Dictionary<string, RuleInputMapping> mappings = DeserializeInputMappings(
                JsonSerializer.Serialize(revision.InputMappings, Options));
            Result<RuleDefinitionKey> key = RuleDefinitionKey.Create(revision.DefinitionKey);
            if (key.IsFailure)
                throw new InvalidOperationException(key.Error);
            result.Add(new RuleBindingRevision(
                revision.Revision,
                key.Value,
                revision.DefinitionVersion,
                revision.TargetType,
                revision.TargetId,
                revision.UseCaseOrTrigger,
                mappings,
                revision.Priority,
                revision.Enabled,
                (DomainBindingFailureBehavior)revision.FailureBehavior,
                revision.UpdatedByUserId,
                revision.UpdatedAt));
        }
        return result;
    }

    public static RuleOutputContract DeserializeOutput(string json)
    {
        RuleOutputContractDto dto = JsonSerializer.Deserialize<RuleOutputContractDto>(json, Options)
            ?? throw new InvalidOperationException("Persisted rule output contract is missing.");
        Result<RuleOutputContract> output = RuleOutputContract.Create(
            (DomainValueType)dto.Type,
            (DomainExpressionCardinality)dto.Cardinality);
        return output.IsSuccess ? output.Value : throw new InvalidOperationException(output.Error);
    }

    private static RuleInputDefinitionDto ToDto(RuleInputDefinition input) =>
        new(
            input.Key,
            input.Label,
            input.Types.Select(type => (ContractValueType)type).ToArray(),
            input.IsRequired,
            input.AllowMultiple,
            input.AllowedValues);

    private static RuleOutputContractDto ToDto(RuleOutputContract output) =>
        new((ContractValueType)output.Type, (ContractExpressionCardinality)output.Cardinality);

    private static RuleInputDefinition ToDomain(RuleInputDefinitionDto input)
    {
        Result<RuleInputDefinition> result = RuleInputDefinition.Restore(
            input.Key,
            input.Label,
            input.Types.Select(type => (DomainValueType)type).ToArray(),
            input.IsRequired,
            input.AllowMultiple,
            input.AllowedValues);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error);
    }

    private static RuleConditionNodeDto ToDto(RuleConditionNode node) => node switch
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
            RuleConditionNode[] children = node.Children!.Select(ToDomain).ToArray();
            Result<RuleConditionGroup> group = RuleConditionGroup.Create(
                node.NodeId,
                (DomainLogicalOperator)node.LogicalOperator!.Value,
                children);
            return group.IsSuccess ? group.Value : throw new InvalidOperationException(group.Error);
        }

        RuleOperand left = ToDomain(node.Left!);
        RuleOperand? right = node.Right is null ? null : ToDomain(node.Right);
        Result<RulePredicateCondition> predicate = RulePredicateCondition.Create(
            node.NodeId,
            (DomainPredicateOperator)node.PredicateOperator!.Value,
            left,
            right);
        return predicate.IsSuccess ? predicate.Value : throw new InvalidOperationException(predicate.Error);
    }

    private static RuleOperandDto ToDto(RuleOperand operand) =>
        new(
            (ContractOperandKind)operand.Kind,
            operand.Reference,
            operand.Literal is null
                ? null
                : new RuleValueDto((ContractValueType)operand.Literal.Type, operand.Literal.Values),
            operand.FunctionKind is null ? null : (ContractExpressionFunction)operand.FunctionKind.Value,
            operand.Arguments.Select(ToDto).ToArray());

    private static RuleOperand ToDomain(RuleOperandDto operand)
    {
        Result<RuleOperand> result = (DomainOperandKind)operand.Kind switch
        {
            DomainOperandKind.Input => RuleOperand.Input(operand.Reference ?? string.Empty),
            DomainOperandKind.Literal => Literal(operand.Literal),
            DomainOperandKind.Function => Function(operand),
            _ => throw new InvalidOperationException("Persisted rule operand kind is invalid."),
        };
        return result.IsSuccess ? result.Value : throw new InvalidOperationException(result.Error);
    }

    private static Result<RuleOperand> Literal(RuleValueDto? literal)
    {
        if (literal is null)
            return Result.Failure<RuleOperand>("Persisted rule literal is missing.");

        Result<RuleValue> value = RuleValue.Create(
            (DomainValueType)literal.Type,
            literal.Values,
            allowMultiple: true);
        return value.IsSuccess
            ? RuleOperand.LiteralValue(value.Value)
            : Result.Failure<RuleOperand>(value.Error);
    }

    private static Result<RuleOperand> Function(RuleOperandDto operand)
    {
        if (operand.Function is null || operand.Arguments is null)
            return Result.Failure<RuleOperand>("Persisted rule function is invalid.");

        List<RuleOperand> arguments = [];
        foreach (RuleOperandDto argumentDto in operand.Arguments)
            arguments.Add(ToDomain(argumentDto));

        return RuleOperand.Function((DomainExpressionFunction)operand.Function.Value, arguments);
    }

    private sealed record BindingRevisionDto(
        int Revision,
        string DefinitionKey,
        int DefinitionVersion,
        string TargetType,
        string TargetId,
        string UseCaseOrTrigger,
        Dictionary<string, RuleInputMappingDto> InputMappings,
        int Priority,
        bool Enabled,
        ContractRuleBindingFailureBehavior FailureBehavior,
        Guid UpdatedByUserId,
        DateTime UpdatedAt);

    private enum ContractRuleBindingFailureBehavior
    {
        FailClosed = 0,
        FailOpen = 1,
    }

}
