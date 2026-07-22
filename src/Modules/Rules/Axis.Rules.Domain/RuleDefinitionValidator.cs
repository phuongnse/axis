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
        if (effectiveLimits.MaxDepth <= 0 || effectiveLimits.MaxNodes <= 0 ||
            effectiveLimits.MaxFunctionCalls <= 0 || effectiveLimits.MaxParameters <= 0 ||
            effectiveLimits.MaxExecutionSteps <= 0)
            return Result.Failure("Rule validation limits must be positive.");

        if (parameters.Count > effectiveLimits.MaxParameters)
            return Result.Failure("Rule definition exceeds the maximum parameter count.");

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

        RulePredicateOperatorDefinition? definition = RuleExpressionLanguage.Find(predicate.Operator);
        if (definition is null || !MatchesAnyShape(definition.LeftShapes, left.Value))
            return Result.Failure("Rule predicate left operand is not type compatible.");

        if (definition.RightShapes.Count == 0)
            return Result.Success();

        Result<OperandShape> right = ResolveShape(predicate.Right!, state);
        if (right.IsFailure)
            return Result.Failure(right.Error);

        if (!MatchesAnyShape(definition.RightShapes, right.Value))
            return Result.Failure("Rule predicate right operand is not type compatible.");

        if (definition.RequiresMatchingTypes && left.Value.Type != right.Value.Type)
            return Result.Failure("Rule predicate operands must use the same value type.");

        return Result.Success();
    }

    private static Result<OperandShape> ResolveShape(RuleOperand operand, ValidationState state)
    {
        if (operand.Kind == RuleOperandKind.Literal)
            return new OperandShape(operand.Literal!.Type, operand.Literal.IsMultiple);

        if (operand.Kind == RuleOperandKind.Function)
            return ResolveFunctionShape(operand, state);

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

    private static Result<OperandShape> ResolveFunctionShape(
        RuleOperand operand,
        ValidationState state)
    {
        state.FunctionCallCount += 1;
        if (state.FunctionCallCount > state.Limits.MaxFunctionCalls)
            return Result.Failure<OperandShape>("Rule expression exceeds the maximum function-call count.");

        RuleExpressionFunctionDefinition? definition = operand.FunctionKind is null
            ? null
            : RuleExpressionLanguage.Find(operand.FunctionKind.Value);
        if (definition is null || definition.Parameters.Count != operand.Arguments.Count)
            return Result.Failure<OperandShape>("Rule expression function signature is invalid.");

        for (int index = 0; index < definition.Parameters.Count; index += 1)
        {
            Result<OperandShape> argument = ResolveShape(operand.Arguments[index], state);
            if (argument.IsFailure)
                return argument;

            RuleExpressionFunctionParameter parameter = definition.Parameters[index];
            if (!parameter.AcceptedTypes.Contains(argument.Value.Type) ||
                !MatchesCardinality(parameter.Cardinality, argument.Value.IsMultiple))
            {
                return Result.Failure<OperandShape>(
                    $"Rule expression function '{definition.Function}' arguments are not type compatible.");
            }
        }

        return new OperandShape(
            definition.ReturnType,
            definition.ReturnCardinality == RuleExpressionCardinality.Multiple);
    }

    private static bool MatchesCardinality(
        RuleExpressionCardinality cardinality,
        bool isMultiple) =>
        cardinality == RuleExpressionCardinality.Any ||
        cardinality == RuleExpressionCardinality.Multiple && isMultiple ||
        cardinality == RuleExpressionCardinality.Scalar && !isMultiple;

    private static bool MatchesAnyShape(
        IReadOnlyList<RuleExpressionValueShape> shapes,
        OperandShape operand) =>
        shapes.Any(shape =>
            shape.Type == operand.Type &&
            MatchesCardinality(shape.Cardinality, operand.IsMultiple));

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
        public int FunctionCallCount { get; set; }
    }
}
