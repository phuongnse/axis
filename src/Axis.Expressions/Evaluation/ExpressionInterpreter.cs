using Axis.Expressions.Syntax;

namespace Axis.Expressions.Evaluation;

/// <summary>
/// Evaluates a checked expression against one record's field values, following the null rules and
/// run-time errors in docs/reference/expressions.md. It covers literals, bare field names,
/// <c>date</c> and <c>dateTime</c>, and every operator. It does no I/O. Arithmetic is exact, and an
/// evaluation stops after <see cref="ExpressionLimits.MaxSteps"/> steps. Values use the CLR types
/// listed on <see cref="ExpressionValues"/>.
/// </summary>
public static class ExpressionInterpreter
{
    /// <summary>
    /// Evaluates <paramref name="expression"/>, which must have passed
    /// <see cref="Typing.ExpressionTypeChecker"/> against the fields in <paramref name="values"/>.
    /// A missing field, a value of an unexpected CLR type, or a node the interpreter does not build
    /// yet is a bug in the caller, and throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public static ExpressionEvaluationResult Evaluate(ExpressionNode expression, ExpressionValues values)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(values);

        try
        {
            return new ExpressionEvaluationResult(new Evaluator(values).Eval(expression), null);
        }
        catch (EvaluationFailure failure)
        {
            return new ExpressionEvaluationResult(null, failure.Error);
        }
    }

    private sealed class Evaluator(ExpressionValues values)
    {
        private int _steps;

        public object? Eval(ExpressionNode node)
        {
            if (++_steps > ExpressionLimits.MaxSteps)
            {
                throw Fail(
                    ExpressionRuntimeErrorKind.StepBudgetExhausted,
                    $"The expression ran past its budget of {ExpressionLimits.MaxSteps} steps",
                    node.Offset);
            }

            return node switch
            {
                IntegerLiteral literal => literal.Value,
                DecimalLiteral literal => literal.Value,
                TextLiteral literal => literal.Value,
                BooleanLiteral literal => literal.Value,
                NullLiteral => null,
                NameNode name => Name(name),
                CallNode call => Call(call),
                UnaryNode unary => Unary(unary),
                BinaryNode binary => Binary(binary),
                IsNullNode isNull => (Eval(isNull.Operand) is null) != isNull.Negated,
                InNode inNode => In(inNode),
                _ => throw new InvalidOperationException($"The interpreter does not evaluate {node.GetType().Name} yet."),
            };
        }

        private object? Name(NameNode name)
        {
            if (!values.TryGetValue(name.Name, out var value))
            {
                throw new InvalidOperationException($"No value was given for field '{name.Name}'.");
            }

            return value is null or string or long or decimal or bool or DateOnly or DateTimeOffset or Guid
                ? value
                : throw Unexpected(value);
        }

        private object Call(CallNode call)
        {
            var isDate = string.Equals(call.Name, "date", StringComparison.OrdinalIgnoreCase);
            if ((!isDate && !string.Equals(call.Name, "dateTime", StringComparison.OrdinalIgnoreCase))
                || call.Arguments is not [TextLiteral argument])
            {
                throw new InvalidOperationException($"The interpreter does not evaluate function '{call.Name}' yet.");
            }

            var text = (string)Eval(argument)!;
            if (isDate)
            {
                return DateLiterals.TryParseDate(text, out var date)
                    ? date
                    : throw Fail(ExpressionRuntimeErrorKind.InvalidDateLiteral, $"'{text}' is not a valid date", call.Offset);
            }

            return DateLiterals.TryParseDateTime(text, out var dateTime)
                ? dateTime
                : throw Fail(ExpressionRuntimeErrorKind.InvalidDateLiteral, $"'{text}' is not a valid date-time", call.Offset);
        }

        private object? Unary(UnaryNode unary)
        {
            var operand = Eval(unary.Operand);
            if (unary.Operator == UnaryOperator.Not)
            {
                return !Boolean(operand);
            }

            return operand switch
            {
                null => null,
                long integer => integer == long.MinValue
                    ? throw Fail(ExpressionRuntimeErrorKind.IntegerOverflow, "The result of '-' is outside the integer range", unary.Offset)
                    : -integer,
                decimal number => -number,
                _ => throw Unexpected(operand),
            };
        }

        private object? Binary(BinaryNode binary)
        {
            switch (binary.Operator)
            {
                case BinaryOperator.And:
                    return And(binary);
                case BinaryOperator.Or:
                    return Or(binary);
            }

            var left = Eval(binary.Left);
            var right = Eval(binary.Right);
            return binary.Operator switch
            {
                BinaryOperator.Equal => AreEqual(left, right),
                BinaryOperator.NotEqual => !AreEqual(left, right),
                BinaryOperator.Less => Compare(left, right) is { } order ? order < 0 : null,
                BinaryOperator.LessOrEqual => Compare(left, right) is { } order ? order <= 0 : null,
                BinaryOperator.Greater => Compare(left, right) is { } order ? order > 0 : null,
                BinaryOperator.GreaterOrEqual => Compare(left, right) is { } order ? order >= 0 : null,
                _ => Arithmetic(binary, left, right),
            };
        }

        /// <summary><c>false and x</c> is false without evaluating <c>x</c>.</summary>
        private bool? And(BinaryNode binary)
        {
            var left = Boolean(Eval(binary.Left));
            if (left == false)
            {
                return false;
            }

            var right = Boolean(Eval(binary.Right));
            if (right == false)
            {
                return false;
            }

            return left is null || right is null ? null : true;
        }

        /// <summary><c>true or x</c> is true without evaluating <c>x</c>.</summary>
        private bool? Or(BinaryNode binary)
        {
            var left = Boolean(Eval(binary.Left));
            if (left == true)
            {
                return true;
            }

            var right = Boolean(Eval(binary.Right));
            if (right == true)
            {
                return true;
            }

            return left is null || right is null ? null : false;
        }

        /// <summary>A <c>null</c> operand is false, never <c>null</c>. Items are evaluated in order until one matches.</summary>
        private bool In(InNode inNode)
        {
            var operand = Eval(inNode.Operand);
            if (operand is null)
            {
                return false;
            }

            foreach (var item in inNode.Items)
            {
                if (AreEqual(operand, Eval(item)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A <c>null</c> operand gives <c>null</c> before any error is checked, so <c>null / 0</c> is <c>null</c>.</summary>
        private static object? Arithmetic(BinaryNode binary, object? left, object? right)
        {
            if (left is null || right is null)
            {
                return null;
            }

            if (left is long a && right is long b && binary.Operator != BinaryOperator.Divide)
            {
                try
                {
                    return binary.Operator switch
                    {
                        BinaryOperator.Add => checked(a + b),
                        BinaryOperator.Subtract => checked(a - b),
                        BinaryOperator.Multiply => checked(a * b),
                        _ => throw new ArgumentOutOfRangeException(nameof(binary), binary.Operator, "Unknown operator."),
                    };
                }
                catch (OverflowException)
                {
                    throw Fail(
                        ExpressionRuntimeErrorKind.IntegerOverflow,
                        $"The result of '{Symbol(binary.Operator)}' is outside the integer range",
                        binary.Offset);
                }
            }

            var x = Number(left);
            var y = Number(right);
            try
            {
                return binary.Operator switch
                {
                    BinaryOperator.Add => DecimalMath.Add(x, y),
                    BinaryOperator.Subtract => DecimalMath.Subtract(x, y),
                    BinaryOperator.Multiply => DecimalMath.Multiply(x, y),
                    BinaryOperator.Divide => DecimalMath.Divide(x, y),
                    _ => throw new ArgumentOutOfRangeException(nameof(binary), binary.Operator, "Unknown operator."),
                };
            }
            catch (DivideByZeroException)
            {
                throw Fail(ExpressionRuntimeErrorKind.DivisionByZero, "Division by zero", binary.Offset);
            }
            catch (OverflowException)
            {
                throw Fail(
                    ExpressionRuntimeErrorKind.DecimalOverflow,
                    $"The result of '{Symbol(binary.Operator)}' cannot be held exactly as a decimal",
                    binary.Offset);
            }
        }

        /// <summary>The order of two values, or <c>null</c> when either is <c>null</c>.</summary>
        private static int? Compare(object? left, object? right) => (left, right) switch
        {
            (null, _) or (_, null) => null,
            (long a, long b) => a.CompareTo(b),
            (long or decimal, long or decimal) => Number(left).CompareTo(Number(right)),
            (DateOnly a, DateOnly b) => a.CompareTo(b),
            (DateTimeOffset a, DateTimeOffset b) => a.CompareTo(b),
            _ => throw Unexpected(left, right),
        };

        /// <summary>Null-safe equality: two <c>null</c>s are equal, and <c>null</c> never equals a value.</summary>
        private static bool AreEqual(object? left, object? right) => (left, right) switch
        {
            (null, null) => true,
            (null, _) or (_, null) => false,
            (long a, long b) => a == b,
            (long or decimal, long or decimal) => Number(left) == Number(right),
            (string a, string b) => string.Equals(a, b, StringComparison.Ordinal),
            (bool or DateOnly or DateTimeOffset or Guid, _) when left.GetType() == right.GetType() => left.Equals(right),
            _ => throw Unexpected(left, right),
        };

        private static bool? Boolean(object? value) => value switch
        {
            null => null,
            bool boolean => boolean,
            _ => throw Unexpected(value),
        };

        private static decimal Number(object value) => value switch
        {
            long integer => integer,
            decimal number => number,
            _ => throw Unexpected(value),
        };
    }

    private static InvalidOperationException Unexpected(object value) =>
        new($"A value of type {value.GetType().Name} is not an expression value here.");

    private static InvalidOperationException Unexpected(object? left, object? right) =>
        new($"Values of type {left?.GetType().Name} and {right?.GetType().Name} cannot be compared.");

    private static EvaluationFailure Fail(ExpressionRuntimeErrorKind kind, string message, int offset) =>
        new(new ExpressionRuntimeError(kind, $"{message} at character {offset + 1}.", offset));

    private static string Symbol(BinaryOperator op) => op switch
    {
        BinaryOperator.Add => "+",
        BinaryOperator.Subtract => "-",
        BinaryOperator.Multiply => "*",
        BinaryOperator.Divide => "/",
        _ => op.ToString(),
    };

    /// <summary>Stops evaluation at the first run-time error. Only <see cref="Evaluate"/> catches it.</summary>
    private sealed class EvaluationFailure(ExpressionRuntimeError error) : Exception(error.Message)
    {
        public ExpressionRuntimeError Error { get; } = error;
    }
}
