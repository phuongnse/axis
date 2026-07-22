using System.Globalization;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed record RuleEvaluationLimits(
    int MaxDepth = 12,
    int MaxNodes = 200,
    int MaxFunctionCalls = 50,
    int MaxParameters = 100,
    int MaxExecutionSteps = 1000)
{
    public static RuleEvaluationLimits Default { get; } = new();
}

public sealed record RuleNodeDiagnostic(string NodeId, bool IsMatch);

public sealed record RuleConditionEvaluation(
    bool IsMatch,
    IReadOnlyList<RuleNodeDiagnostic> Diagnostics,
    int ExecutionSteps);

public static class RuleConditionEvaluator
{
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(250);

    public static Result<RuleConditionEvaluation> Evaluate(
        RuleConditionNode condition,
        RuleContextSchema schema,
        IReadOnlyDictionary<string, RuleValue> context,
        IReadOnlyDictionary<string, RuleValue>? parameters = null,
        RuleEvaluationLimits? limits = null)
    {
        RuleEvaluationLimits effectiveLimits = limits ?? RuleEvaluationLimits.Default;
        if (effectiveLimits.MaxDepth <= 0 || effectiveLimits.MaxNodes <= 0 ||
            effectiveLimits.MaxFunctionCalls <= 0 || effectiveLimits.MaxParameters <= 0 ||
            effectiveLimits.MaxExecutionSteps <= 0)
            return Result.Failure<RuleConditionEvaluation>("Rule evaluation limits must be positive.");

        if ((parameters?.Count ?? 0) > effectiveLimits.MaxParameters)
            return Result.Failure<RuleConditionEvaluation>("Rule evaluation exceeds the maximum parameter count.");

        EvaluationState state = new(
            schema,
            context,
            parameters ?? new Dictionary<string, RuleValue>(StringComparer.Ordinal),
            effectiveLimits);

        Result<bool> match = EvaluateNode(condition, depth: 1, state);
        return match.IsFailure
            ? Result.Failure<RuleConditionEvaluation>(match.Error)
            : new RuleConditionEvaluation(match.Value, state.Diagnostics.AsReadOnly(), state.ExecutionSteps);
    }

    private static Result<bool> EvaluateNode(RuleConditionNode node, int depth, EvaluationState state)
    {
        if (depth > state.Limits.MaxDepth)
            return Result.Failure<bool>("Rule condition exceeds the maximum depth.");

        state.NodeCount += 1;
        if (state.NodeCount > state.Limits.MaxNodes)
            return Result.Failure<bool>("Rule condition exceeds the maximum node count.");

        state.ExecutionSteps += 1;
        if (state.ExecutionSteps > state.Limits.MaxExecutionSteps)
            return Result.Failure<bool>("Rule evaluation exceeded the execution-step limit.");

        Result<bool> result = node switch
        {
            RuleConditionGroup group => EvaluateGroup(group, depth, state),
            RulePredicateCondition predicate => EvaluatePredicate(predicate, state),
            _ => Result.Failure<bool>("Rule condition node type is not supported."),
        };

        if (result.IsSuccess)
            state.Diagnostics.Add(new RuleNodeDiagnostic(node.NodeId, result.Value));

        return result;
    }

    private static Result<bool> EvaluateGroup(RuleConditionGroup group, int depth, EvaluationState state)
    {
        if (group.Operator == RuleLogicalOperator.Not)
        {
            Result<bool> child = EvaluateNode(group.Children[0], depth + 1, state);
            return child.IsFailure ? child : !child.Value;
        }

        bool expected = group.Operator == RuleLogicalOperator.All;
        foreach (RuleConditionNode childNode in group.Children)
        {
            Result<bool> child = EvaluateNode(childNode, depth + 1, state);
            if (child.IsFailure)
                return child;

            if (group.Operator == RuleLogicalOperator.All && !child.Value)
                return false;

            if (group.Operator == RuleLogicalOperator.Any && child.Value)
                return true;
        }

        return expected;
    }

    private static Result<bool> EvaluatePredicate(RulePredicateCondition predicate, EvaluationState state)
    {
        Result<ResolvedOperand> left = Resolve(predicate.Left, state);
        if (left.IsFailure)
            return Result.Failure<bool>(left.Error);

        if (predicate.Operator == RulePredicateOperator.IsNull)
            return left.Value.Value is null;

        if (predicate.Operator == RulePredicateOperator.IsNotNull)
            return left.Value.Value is not null;

        Result<ResolvedOperand> right = Resolve(predicate.Right!, state);
        if (right.IsFailure)
            return Result.Failure<bool>(right.Error);

        if (left.Value.Value is null || right.Value.Value is null)
            return predicate.Operator == RulePredicateOperator.NotEqual;

        RuleValue leftValue = left.Value.Value;
        RuleValue rightValue = right.Value.Value;
        if (leftValue.Type != rightValue.Type)
            return Result.Failure<bool>("Rule predicate operands must use the same value type.");

        return predicate.Operator switch
        {
            RulePredicateOperator.Equal => Equal(leftValue, rightValue),
            RulePredicateOperator.NotEqual => !Equal(leftValue, rightValue),
            RulePredicateOperator.GreaterThan => Compare(leftValue, rightValue, comparison => comparison > 0),
            RulePredicateOperator.GreaterThanOrEqual => Compare(leftValue, rightValue, comparison => comparison >= 0),
            RulePredicateOperator.LessThan => Compare(leftValue, rightValue, comparison => comparison < 0),
            RulePredicateOperator.LessThanOrEqual => Compare(leftValue, rightValue, comparison => comparison <= 0),
            RulePredicateOperator.Contains => Contains(leftValue, rightValue),
            RulePredicateOperator.StartsWith => TextCompare(leftValue, rightValue, (leftText, rightText) => leftText.StartsWith(rightText, StringComparison.Ordinal)),
            RulePredicateOperator.EndsWith => TextCompare(leftValue, rightValue, (leftText, rightText) => leftText.EndsWith(rightText, StringComparison.Ordinal)),
            _ => Result.Failure<bool>("Rule predicate operator is not supported."),
        };
    }

    private static Result<ResolvedOperand> Resolve(RuleOperand operand, EvaluationState state)
    {
        if (operand.Kind == RuleOperandKind.Literal)
            return new ResolvedOperand(operand.Literal);

        if (operand.Kind == RuleOperandKind.Function)
            return ResolveFunction(operand, state);

        if (operand.Kind == RuleOperandKind.Parameter)
            return state.Parameters.TryGetValue(operand.Reference!, out RuleValue? parameter)
                ? new ResolvedOperand(parameter)
                : new ResolvedOperand(null);

        RuleContextField? field = state.Schema.FindField(operand.Reference!);
        if (field is null)
            return Result.Failure<ResolvedOperand>($"Rule context path '{operand.Reference}' is not defined.");

        if (!state.Context.TryGetValue(field.Path, out RuleValue? contextValue))
            return new ResolvedOperand(null);

        if (contextValue.Type != field.Type || contextValue.IsMultiple && !field.AllowMultiple)
            return Result.Failure<ResolvedOperand>($"Rule context value '{field.Path}' does not match its schema.");

        return new ResolvedOperand(contextValue);
    }

    private static Result<ResolvedOperand> ResolveFunction(
        RuleOperand operand,
        EvaluationState state)
    {
        state.FunctionCallCount += 1;
        if (state.FunctionCallCount > state.Limits.MaxFunctionCalls)
            return Result.Failure<ResolvedOperand>("Rule expression exceeds the maximum function-call count.");

        state.ExecutionSteps += 1;
        if (state.ExecutionSteps > state.Limits.MaxExecutionSteps)
            return Result.Failure<ResolvedOperand>("Rule evaluation exceeded the execution-step limit.");

        if (operand.FunctionKind is null)
            return Result.Failure<ResolvedOperand>("Rule expression function is not supported.");

        List<RuleValue?> arguments = [];
        foreach (RuleOperand argumentOperand in operand.Arguments)
        {
            Result<ResolvedOperand> argument = Resolve(argumentOperand, state);
            if (argument.IsFailure)
                return argument;
            arguments.Add(argument.Value.Value);
        }

        return operand.FunctionKind.Value switch
        {
            RuleExpressionFunction.IsBlank => IsBlank(arguments),
            RuleExpressionFunction.Length => Length(arguments),
            RuleExpressionFunction.Precision => Precision(arguments),
            RuleExpressionFunction.Scale => Scale(arguments),
            RuleExpressionFunction.Count => Count(arguments),
            RuleExpressionFunction.MatchesPattern => MatchesPattern(arguments),
            RuleExpressionFunction.HasFormat => HasFormat(arguments),
            RuleExpressionFunction.ToDecimal => ToDecimal(arguments),
            _ => Result.Failure<ResolvedOperand>("Rule expression function is not supported."),
        };
    }

    private static Result<ResolvedOperand> IsBlank(IReadOnlyList<RuleValue?> arguments)
    {
        if (arguments.Count != 1)
            return Result.Failure<ResolvedOperand>("IsBlank requires one argument.");

        RuleValue? value = arguments[0];
        bool isBlank = value is null ||
            value.Type == RuleValueType.Text && value.Values.All(string.IsNullOrWhiteSpace);
        return Scalar(RuleValueType.Boolean, isBlank.ToString());
    }

    private static Result<ResolvedOperand> Length(IReadOnlyList<RuleValue?> arguments)
    {
        RuleValue? value = SingleArgument(arguments, RuleValueType.Text);
        return value is null
            ? new ResolvedOperand(null)
            : Scalar(
                RuleValueType.Integer,
                value.Values[0].EnumerateRunes().Count().ToString(CultureInfo.InvariantCulture));
    }

    private static Result<ResolvedOperand> Precision(IReadOnlyList<RuleValue?> arguments)
    {
        RuleValue? value = SingleArgument(arguments, RuleValueType.Decimal);
        if (value is null)
            return new ResolvedOperand(null);

        (int precision, _) = DecimalShape(value.Values[0]);
        return Scalar(RuleValueType.Integer, precision.ToString(CultureInfo.InvariantCulture));
    }

    private static Result<ResolvedOperand> Scale(IReadOnlyList<RuleValue?> arguments)
    {
        RuleValue? value = SingleArgument(arguments, RuleValueType.Decimal);
        if (value is null)
            return new ResolvedOperand(null);

        (_, int scale) = DecimalShape(value.Values[0]);
        return Scalar(RuleValueType.Integer, scale.ToString(CultureInfo.InvariantCulture));
    }

    private static Result<ResolvedOperand> Count(IReadOnlyList<RuleValue?> arguments)
    {
        if (arguments.Count != 1)
            return Result.Failure<ResolvedOperand>("Count requires one argument.");

        return arguments[0] is RuleValue value
            ? Scalar(RuleValueType.Integer, value.Values.Count.ToString(CultureInfo.InvariantCulture))
            : new ResolvedOperand(null);
    }

    private static Result<ResolvedOperand> MatchesPattern(IReadOnlyList<RuleValue?> arguments)
    {
        if (arguments.Count != 2 || arguments[0] is not RuleValue value ||
            arguments[1] is not RuleValue pattern)
            return new ResolvedOperand(null);

        try
        {
            bool matches = Regex.IsMatch(
                value.Values[0],
                pattern.Values[0],
                RegexOptions.CultureInvariant,
                PatternTimeout);
            return Scalar(RuleValueType.Boolean, matches.ToString());
        }
        catch (RegexMatchTimeoutException)
        {
            return Result.Failure<ResolvedOperand>("Rule text pattern exceeded its execution limit.");
        }
        catch (ArgumentException)
        {
            return Result.Failure<ResolvedOperand>("Rule text pattern is invalid.");
        }
    }

    private static Result<ResolvedOperand> HasFormat(IReadOnlyList<RuleValue?> arguments)
    {
        if (arguments.Count != 2 || arguments[0] is not RuleValue value ||
            arguments[1] is not RuleValue format)
            return new ResolvedOperand(null);

        bool matches = format.Values[0] switch
        {
            "Email" => MailAddress.TryCreate(value.Values[0], out _),
            "Url" => Uri.TryCreate(value.Values[0], UriKind.Absolute, out Uri? uri) &&
                uri.Scheme is "http" or "https",
            "Uuid" => Guid.TryParse(value.Values[0], out _),
            _ => false,
        };
        return Scalar(RuleValueType.Boolean, matches.ToString());
    }

    private static Result<ResolvedOperand> ToDecimal(IReadOnlyList<RuleValue?> arguments)
    {
        if (arguments.Count != 1 || arguments[0] is not RuleValue value)
            return new ResolvedOperand(null);

        return value.Type is RuleValueType.Integer or RuleValueType.Decimal
            ? Scalar(RuleValueType.Decimal, value.Values[0])
            : Result.Failure<ResolvedOperand>("ToDecimal requires a scalar integer or decimal.");
    }

    private static RuleValue? SingleArgument(
        IReadOnlyList<RuleValue?> arguments,
        RuleValueType expectedType) =>
        arguments.Count == 1 &&
        arguments[0] is RuleValue value &&
        value.Type == expectedType &&
        value.Values.Count == 1
            ? value
            : null;

    private static (int Precision, int Scale) DecimalShape(string value)
    {
        string canonical = decimal.Parse(value, CultureInfo.InvariantCulture)
            .ToString("G29", CultureInfo.InvariantCulture)
            .TrimStart('-');
        string[] parts = canonical.Split('.', 2);
        int scale = parts.Length == 2 ? parts[1].Length : 0;
        int precision = Math.Max(1, string.Concat(parts).TrimStart('0').Length);
        return (precision, scale);
    }

    private static Result<ResolvedOperand> Scalar(RuleValueType type, string value)
    {
        Result<RuleValue> result = RuleValue.Create(type, [value]);
        return result.IsSuccess
            ? new ResolvedOperand(result.Value)
            : Result.Failure<ResolvedOperand>(result.Error);
    }

    private static bool Equal(RuleValue left, RuleValue right) =>
        left.Values.SequenceEqual(right.Values, StringComparer.Ordinal);

    private static Result<bool> Compare(RuleValue left, RuleValue right, Func<int, bool> predicate)
    {
        if (left.Values.Count != 1 || right.Values.Count != 1)
            return Result.Failure<bool>("Ordered comparison requires scalar operands.");

        int comparison = left.Type switch
        {
            RuleValueType.Integer => long.Parse(left.Values[0], CultureInfo.InvariantCulture)
                .CompareTo(long.Parse(right.Values[0], CultureInfo.InvariantCulture)),
            RuleValueType.Decimal => decimal.Parse(left.Values[0], CultureInfo.InvariantCulture)
                .CompareTo(decimal.Parse(right.Values[0], CultureInfo.InvariantCulture)),
            RuleValueType.Date => DateOnly.ParseExact(left.Values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .CompareTo(DateOnly.ParseExact(right.Values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture)),
            RuleValueType.DateTime => DateTimeOffset.Parse(left.Values[0], CultureInfo.InvariantCulture)
                .CompareTo(DateTimeOffset.Parse(right.Values[0], CultureInfo.InvariantCulture)),
            RuleValueType.Text => string.Compare(left.Values[0], right.Values[0], StringComparison.Ordinal),
            _ => int.MinValue,
        };

        return comparison == int.MinValue
            ? Result.Failure<bool>("Rule value type does not support ordered comparison.")
            : predicate(comparison);
    }

    private static Result<bool> Contains(RuleValue left, RuleValue right)
    {
        if (right.Values.Count != 1)
            return Result.Failure<bool>("Contains requires a scalar right operand.");

        if (left.Type == RuleValueType.Text && left.Values.Count == 1)
            return left.Values[0].Contains(right.Values[0], StringComparison.Ordinal);

        return left.Values.Contains(right.Values[0], StringComparer.Ordinal);
    }

    private static Result<bool> TextCompare(
        RuleValue left,
        RuleValue right,
        Func<string, string, bool> predicate) =>
        left.Type == RuleValueType.Text && left.Values.Count == 1 && right.Values.Count == 1
            ? predicate(left.Values[0], right.Values[0])
            : Result.Failure<bool>("Text comparison requires scalar text operands.");

    private sealed record ResolvedOperand(RuleValue? Value);

    private sealed class EvaluationState(
        RuleContextSchema schema,
        IReadOnlyDictionary<string, RuleValue> context,
        IReadOnlyDictionary<string, RuleValue> parameters,
        RuleEvaluationLimits limits)
    {
        public RuleContextSchema Schema { get; } = schema;
        public IReadOnlyDictionary<string, RuleValue> Context { get; } = context;
        public IReadOnlyDictionary<string, RuleValue> Parameters { get; } = parameters;
        public RuleEvaluationLimits Limits { get; } = limits;
        public List<RuleNodeDiagnostic> Diagnostics { get; } = [];
        public int NodeCount { get; set; }
        public int FunctionCallCount { get; set; }
        public int ExecutionSteps { get; set; }
    }
}
