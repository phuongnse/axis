using Axis.Expressions.Diagnostics;
using Axis.Expressions.Syntax;

namespace Axis.Expressions.Typing;

/// <summary>
/// Finds the type of a parsed expression, following the typing rules in
/// docs/reference/expressions.md, and checks it against the type the use needs. It covers
/// literals, bare field names, <c>date</c> and <c>dateTime</c>, and every operator. Other
/// functions and paths are reported as unknown names until they are built. It stops at the first
/// problem and reports only that one.
/// </summary>
public static class ExpressionTypeChecker
{
    public static ExpressionCheckResult Check(ExpressionNode expression, ExpressionScope scope, ExpressionType expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(expected);

        try
        {
            var actual = Infer(expression, scope);
            if (!Fits(expression, actual, expected))
            {
                return new ExpressionCheckResult(null, new ExpressionDiagnostic(
                    ExpressionDiagnosticCodes.ResultTypeMismatch,
                    $"The expression must be {expected}, but it is {actual}.",
                    0));
            }

            return new ExpressionCheckResult(actual, null);
        }
        catch (CheckFailure failure)
        {
            return new ExpressionCheckResult(null, failure.Diagnostic);
        }
    }

    private static ExpressionType Infer(ExpressionNode node, ExpressionScope scope) => node switch
    {
        IntegerLiteral => ExpressionType.Integer,
        DecimalLiteral => ExpressionType.Decimal,
        TextLiteral => ExpressionType.Text,
        BooleanLiteral => ExpressionType.Boolean,
        NullLiteral => ExpressionType.Null,
        NameNode name => scope.TryGetField(name.Name, out var type)
            ? type
            : throw Fail(ExpressionDiagnosticCodes.UnknownName, $"Unknown field '{name.Name}'", name.Offset),
        MemberNode member => throw Fail(
            ExpressionDiagnosticCodes.UnknownName, $"Unknown field '{member.Name}' after '.'", member.Offset),
        CallNode call => InferCall(call),
        UnaryNode unary => InferUnary(unary, Infer(unary.Operand, scope)),
        BinaryNode binary => InferBinary(binary, Infer(binary.Left, scope), Infer(binary.Right, scope)),
        IsNullNode isNull => InferIsNull(isNull, scope),
        InNode inNode => InferIn(inNode, scope),
        _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name, "Unknown node type."),
    };

    private static ExpressionType InferCall(CallNode call)
    {
        ExpressionType result;
        string name;
        if (string.Equals(call.Name, "date", StringComparison.OrdinalIgnoreCase))
        {
            (result, name) = (ExpressionType.Date, "date");
        }
        else if (string.Equals(call.Name, "dateTime", StringComparison.OrdinalIgnoreCase))
        {
            (result, name) = (ExpressionType.DateTime, "dateTime");
        }
        else
        {
            throw Fail(ExpressionDiagnosticCodes.UnknownName, $"Unknown function '{call.Name}'", call.Offset);
        }

        if (call.Arguments is not [TextLiteral])
        {
            throw Fail(ExpressionDiagnosticCodes.TypeMismatch, $"Function '{name}' needs one text literal", call.Offset);
        }

        return result;
    }

    private static ExpressionType InferUnary(UnaryNode unary, ExpressionType operand)
    {
        if (unary.Operator == UnaryOperator.Not)
        {
            return IsBooleanOrNull(operand)
                ? ExpressionType.Boolean
                : throw Fail(ExpressionDiagnosticCodes.TypeMismatch, $"Operator 'not' needs boolean, found {operand}", unary.Offset);
        }

        return IsNumberOrNull(operand)
            ? operand
            : throw Fail(ExpressionDiagnosticCodes.TypeMismatch, $"Operator '-' needs a number, found {operand}", unary.Offset);
    }

    private static ExpressionType InferBinary(BinaryNode binary, ExpressionType left, ExpressionType right)
    {
        switch (binary.Operator)
        {
            case BinaryOperator.Add or BinaryOperator.Subtract or BinaryOperator.Multiply or BinaryOperator.Divide:
                if (!IsNumberOrNull(left) || !IsNumberOrNull(right))
                {
                    throw Mismatch(binary, "cannot combine", left, right);
                }

                if (binary.Operator == BinaryOperator.Divide
                    || left.Kind == ExpressionTypeKind.Decimal
                    || right.Kind == ExpressionTypeKind.Decimal)
                {
                    return ExpressionType.Decimal;
                }

                return left.Kind == ExpressionTypeKind.Null && right.Kind == ExpressionTypeKind.Null
                    ? ExpressionType.Null
                    : ExpressionType.Integer;

            case BinaryOperator.Less or BinaryOperator.LessOrEqual or BinaryOperator.Greater or BinaryOperator.GreaterOrEqual:
                return CanOrder(left, right) ? ExpressionType.Boolean : throw Mismatch(binary, "cannot compare", left, right);

            case BinaryOperator.Equal or BinaryOperator.NotEqual:
                return Comparable(binary.Left, left, binary.Right, right)
                    ? ExpressionType.Boolean
                    : throw Mismatch(binary, "cannot compare", left, right);

            case BinaryOperator.And or BinaryOperator.Or:
                var wrong = IsBooleanOrNull(left) ? right : left;
                return IsBooleanOrNull(wrong)
                    ? ExpressionType.Boolean
                    : throw Fail(
                        ExpressionDiagnosticCodes.TypeMismatch,
                        $"Operator '{Symbol(binary.Operator)}' needs boolean, found {wrong}",
                        binary.Offset);

            default:
                throw new ArgumentOutOfRangeException(nameof(binary), binary.Operator, "Unknown operator.");
        }
    }

    private static ExpressionType InferIsNull(IsNullNode isNull, ExpressionScope scope)
    {
        Infer(isNull.Operand, scope);
        return ExpressionType.Boolean;
    }

    private static ExpressionType InferIn(InNode inNode, ExpressionScope scope)
    {
        var operand = Infer(inNode.Operand, scope);
        foreach (var item in inNode.Items)
        {
            var itemType = Infer(item, scope);
            if (!Comparable(inNode.Operand, operand, item, itemType))
            {
                throw Fail(
                    ExpressionDiagnosticCodes.TypeMismatch,
                    $"Operator 'in' cannot compare {operand} and {itemType}",
                    inNode.Offset);
            }
        }

        return ExpressionType.Boolean;
    }

    /// <summary>Whether <c>==</c> may compare the two values: either side fits the other.</summary>
    private static bool Comparable(ExpressionNode left, ExpressionType leftType, ExpressionNode right, ExpressionType rightType) =>
        Fits(right, rightType, leftType) || Fits(left, leftType, rightType);

    /// <summary>
    /// Whether a value of <paramref name="type"/>, computed by <paramref name="node"/>, fits
    /// <paramref name="target"/>. <c>null</c> fits any type and an integer widens to a decimal. A
    /// text literal fits an enum only when it is one of the enum's values. When it is not, that is
    /// reported here.
    /// </summary>
    private static bool Fits(ExpressionNode node, ExpressionType type, ExpressionType target)
    {
        if (type.Kind == ExpressionTypeKind.Null || target.Kind == ExpressionTypeKind.Null || type == target)
        {
            return true;
        }

        if (type.Kind == ExpressionTypeKind.Integer && target.Kind == ExpressionTypeKind.Decimal)
        {
            return true;
        }

        if (target.Kind == ExpressionTypeKind.Enum && node is TextLiteral literal)
        {
            return target.Values.Contains(literal.Value, StringComparer.Ordinal)
                ? true
                : throw Fail(
                    ExpressionDiagnosticCodes.UnknownEnumValue,
                    $"'{literal.Value}' is not a value of {target}",
                    literal.Offset);
        }

        return false;
    }

    /// <summary>Ordering needs every side that is not <c>null</c> to be a number, or all dates, or all date-times.</summary>
    private static bool CanOrder(ExpressionType left, ExpressionType right)
    {
        if (left.Kind == ExpressionTypeKind.Null || right.Kind == ExpressionTypeKind.Null)
        {
            var other = left.Kind == ExpressionTypeKind.Null ? right : left;
            return other.Kind is ExpressionTypeKind.Null or ExpressionTypeKind.Integer or ExpressionTypeKind.Decimal
                or ExpressionTypeKind.Date or ExpressionTypeKind.DateTime;
        }

        return (IsNumber(left) && IsNumber(right))
            || (left.Kind == right.Kind && left.Kind is ExpressionTypeKind.Date or ExpressionTypeKind.DateTime);
    }

    private static bool IsNumber(ExpressionType type) =>
        type.Kind is ExpressionTypeKind.Integer or ExpressionTypeKind.Decimal;

    private static bool IsNumberOrNull(ExpressionType type) => IsNumber(type) || type.Kind == ExpressionTypeKind.Null;

    private static bool IsBooleanOrNull(ExpressionType type) =>
        type.Kind is ExpressionTypeKind.Boolean or ExpressionTypeKind.Null;

    private static CheckFailure Mismatch(BinaryNode binary, string verb, ExpressionType left, ExpressionType right) =>
        Fail(ExpressionDiagnosticCodes.TypeMismatch, $"Operator '{Symbol(binary.Operator)}' {verb} {left} and {right}", binary.Offset);

    private static CheckFailure Fail(string code, string message, int offset) =>
        new(new ExpressionDiagnostic(code, $"{message} at character {offset + 1}.", offset));

    private static string Symbol(BinaryOperator op) => op switch
    {
        BinaryOperator.Or => "or",
        BinaryOperator.And => "and",
        BinaryOperator.Equal => "==",
        BinaryOperator.NotEqual => "!=",
        BinaryOperator.Less => "<",
        BinaryOperator.LessOrEqual => "<=",
        BinaryOperator.Greater => ">",
        BinaryOperator.GreaterOrEqual => ">=",
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        _ => op.ToString(),
    };

    /// <summary>Stops checking at the first problem. Only <see cref="Check"/> catches it.</summary>
    private sealed class CheckFailure(ExpressionDiagnostic diagnostic) : Exception(diagnostic.Message)
    {
        public ExpressionDiagnostic Diagnostic { get; } = diagnostic;
    }
}
