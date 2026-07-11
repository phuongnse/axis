using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public static class RuleDefinitionValidator
{
    public static Result Validate(
        RuleContextSchema schema,
        IReadOnlyList<RuleParameterDefinition> parameters,
        RuleConditionNode condition,
        RuleOutcome outcome,
        RuleOutcomeKind outcomeKind,
        RuleEvaluationLimits? limits = null)
    {
        if (outcome.Kind != outcomeKind)
            return Result.Failure("Rule outcome does not match the selected outcome kind.");

        if (parameters.Select(parameter => parameter.Key).Distinct(StringComparer.Ordinal).Count() != parameters.Count)
            return Result.Failure("Rule parameter keys must be unique.");

        RuleEvaluationLimits effectiveLimits = limits ?? RuleEvaluationLimits.Default;
        ValidationState state = new(schema, parameters, effectiveLimits);
        return ValidateNode(condition, depth: 1, state);
    }

    private static Result ValidateNode(RuleConditionNode node, int depth, ValidationState state)
    {
        if (depth > state.Limits.MaxDepth)
            return Result.Failure("Rule condition exceeds the maximum depth.");

        state.NodeCount += 1;
        if (state.NodeCount > state.Limits.MaxNodes)
            return Result.Failure("Rule condition exceeds the maximum node count.");

        if (!state.NodeIds.Add(node.NodeId))
            return Result.Failure("Rule condition node IDs must be unique.");

        if (node is RuleConditionGroup group)
        {
            foreach (RuleConditionNode child in group.Children)
            {
                Result childResult = ValidateNode(child, depth + 1, state);
                if (childResult.IsFailure)
                    return childResult;
            }
            return Result.Success();
        }

        if (node is not RulePredicateCondition predicate)
            return Result.Failure("Rule condition node type is not supported.");

        Result<OperandShape> left = ResolveShape(predicate.Left, state);
        if (left.IsFailure)
            return Result.Failure(left.Error);

        if (predicate.Operator is RulePredicateOperator.IsNull or RulePredicateOperator.IsNotNull)
            return Result.Success();

        Result<OperandShape> right = ResolveShape(predicate.Right!, state);
        if (right.IsFailure)
            return Result.Failure(right.Error);

        if (left.Value.Type != right.Value.Type)
            return Result.Failure("Rule predicate operands must use the same value type.");

        return predicate.Operator switch
        {
            RulePredicateOperator.Equal or RulePredicateOperator.NotEqual => Result.Success(),
            RulePredicateOperator.GreaterThan or
            RulePredicateOperator.GreaterThanOrEqual or
            RulePredicateOperator.LessThan or
            RulePredicateOperator.LessThanOrEqual =>
                ValidateOrdered(left.Value, right.Value),
            RulePredicateOperator.Contains => ValidateContains(left.Value, right.Value),
            RulePredicateOperator.StartsWith or RulePredicateOperator.EndsWith =>
                ValidateText(left.Value, right.Value),
            _ => Result.Failure("Rule predicate operator is not supported."),
        };
    }

    private static Result<OperandShape> ResolveShape(RuleOperand operand, ValidationState state)
    {
        if (operand.Kind == RuleOperandKind.Literal)
            return new OperandShape(operand.Literal!.Type, operand.Literal.IsMultiple);

        if (operand.Kind == RuleOperandKind.Context)
        {
            RuleContextField? field = state.Schema.FindField(operand.Reference!);
            return field is null
                ? Result.Failure<OperandShape>($"Rule context path '{operand.Reference}' is not defined.")
                : new OperandShape(field.Type, field.AllowMultiple);
        }

        RuleParameterDefinition? parameter = state.Parameters.SingleOrDefault(
            candidate => candidate.Key.Equals(operand.Reference, StringComparison.Ordinal));
        return parameter is null
            ? Result.Failure<OperandShape>($"Rule parameter '{operand.Reference}' is not defined.")
            : new OperandShape(parameter.Type, parameter.AllowMultiple);
    }

    private static Result ValidateOrdered(OperandShape left, OperandShape right)
    {
        if (left.IsMultiple || right.IsMultiple)
            return Result.Failure("Ordered comparison requires scalar operands.");

        return left.Type is RuleValueType.Text or RuleValueType.Integer or RuleValueType.Decimal or
            RuleValueType.Date or RuleValueType.DateTime
            ? Result.Success()
            : Result.Failure("Rule value type does not support ordered comparison.");
    }

    private static Result ValidateContains(OperandShape left, OperandShape right)
    {
        if (right.IsMultiple)
            return Result.Failure("Contains requires a scalar right operand.");

        return left.IsMultiple || left.Type == RuleValueType.Text
            ? Result.Success()
            : Result.Failure("Contains requires text or a multiple-value left operand.");
    }

    private static Result ValidateText(OperandShape left, OperandShape right) =>
        left.Type == RuleValueType.Text && !left.IsMultiple && !right.IsMultiple
            ? Result.Success()
            : Result.Failure("Text comparison requires scalar text operands.");

    private sealed record OperandShape(RuleValueType Type, bool IsMultiple);

    private sealed class ValidationState(
        RuleContextSchema schema,
        IReadOnlyList<RuleParameterDefinition> parameters,
        RuleEvaluationLimits limits)
    {
        public RuleContextSchema Schema { get; } = schema;
        public IReadOnlyList<RuleParameterDefinition> Parameters { get; } = parameters;
        public RuleEvaluationLimits Limits { get; } = limits;
        public HashSet<string> NodeIds { get; } = new(StringComparer.Ordinal);
        public int NodeCount { get; set; }
    }
}
