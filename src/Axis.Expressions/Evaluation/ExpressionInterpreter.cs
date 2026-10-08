using System.Text;
using Axis.Expressions.Functions;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Expressions.Evaluation;

/// <summary>
/// Evaluates a checked expression against one record's field values, following the null rules and
/// run-time errors in docs/reference/expressions.md. It covers literals, bare field names, every
/// operator and every function in <see cref="ExpressionFunctions"/>. It does no I/O. Arithmetic is exact, and an
/// evaluation stops after <see cref="ExpressionLimits.MaxSteps"/> steps. Values use the CLR types
/// listed on <see cref="ExpressionValues"/>.
/// </summary>
public static class ExpressionInterpreter
{
    /// <summary>
    /// Evaluates <paramref name="expression"/>, which must have passed
    /// <see cref="ExpressionTypeChecker"/> against the fields in <paramref name="values"/>.
    /// <paramref name="checkResult"/> is that checker's result. It is required because the checked
    /// type decides some run-time types: a <c>coalesce</c> or <c>if</c> checked as decimal returns
    /// an integer it picks as a decimal, even when the field that would make it decimal is empty.
    /// A failed check throws <see cref="ArgumentException"/>. A missing field, a value of an
    /// unexpected CLR type, or a node the interpreter does not build yet is a bug in the caller, and
    /// throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public static ExpressionEvaluationResult Evaluate(
        ExpressionNode expression, ExpressionCheckResult checkResult, ExpressionValues values)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(checkResult);
        ArgumentNullException.ThrowIfNull(values);
        if (!checkResult.Succeeded)
        {
            throw new ArgumentException("Only an expression that passed the type checker can be evaluated.", nameof(checkResult));
        }

        try
        {
            return new ExpressionEvaluationResult(new Evaluator(values, checkResult.DecimalCalls).Eval(expression), null);
        }
        catch (EvaluationFailure failure)
        {
            return new ExpressionEvaluationResult(null, failure.Error);
        }
    }

    private sealed class Evaluator(ExpressionValues values, IReadOnlySet<CallNode> decimalCalls)
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

        private object? Call(CallNode call)
        {
            if (!ExpressionFunctions.TryGet(call.Name, out var signature))
            {
                throw new InvalidOperationException($"The interpreter does not evaluate function '{call.Name}' yet.");
            }

            var arguments = call.Arguments;
            switch (signature.Name)
            {
                case "if":
                    // Only the picked branch is evaluated. A null condition picks the else branch.
                    return Widen(call, Eval(Boolean(Eval(arguments[0])) == true ? arguments[1] : arguments[2]));

                case "coalesce":
                    // Arguments after the first one that is not null are not evaluated.
                    foreach (var argument in arguments)
                    {
                        if (Eval(argument) is { } value)
                        {
                            return Widen(call, value);
                        }
                    }

                    return null;

                case "concat":
                    var builder = new StringBuilder();
                    foreach (var argument in arguments)
                    {
                        builder.Append(Eval(argument) is { } value ? Text(value) : string.Empty);
                    }

                    return builder.ToString();

                case "date" or "dateTime":
                    return DateLiteral(call, signature.Name == "date");
            }

            // Every argument is evaluated. A null one then gives null before any error is checked.
            var results = new object[arguments.Count];
            var anyNull = false;
            for (var i = 0; i < results.Length; i++)
            {
                if (Eval(arguments[i]) is { } value)
                {
                    results[i] = value;
                }
                else
                {
                    anyNull = true;
                }
            }

            if (anyNull)
            {
                return null;
            }

            return signature.Name switch
            {
                "length" => (long)Text(results[0]).EnumerateRunes().Count(),
                "contains" => Text(results[0]).Contains(Text(results[1]), StringComparison.Ordinal),
                "startsWith" => Text(results[0]).StartsWith(Text(results[1]), StringComparison.Ordinal),
                "endsWith" => Text(results[0]).EndsWith(Text(results[1]), StringComparison.Ordinal),
                "lower" => Text(results[0]).ToLowerInvariant(),
                "upper" => Text(results[0]).ToUpperInvariant(),
                "trim" => Text(results[0]).Trim(),
                "abs" => Abs(call, results[0]),
                "floor" => results[0] is decimal number ? decimal.Floor(number) : (object)Integer(results[0]),
                "ceiling" => results[0] is decimal number ? decimal.Ceiling(number) : (object)Integer(results[0]),
                "round" => Round(call, Number(results[0]), Integer(results[1])),
                "year" => (long)Date(results[0]).Year,
                "month" => (long)Date(results[0]).Month,
                "day" => (long)Date(results[0]).Day,
                "addDays" => AddDays(call, Date(results[0]), Integer(results[1])),
                "daysBetween" => (long)(Date(results[1]).DayNumber - Date(results[0]).DayNumber),
                _ => throw new InvalidOperationException($"The interpreter does not evaluate function '{signature.Name}' yet."),
            };
        }

        /// <summary>An integer picked by a <c>coalesce</c> or <c>if</c> checked as decimal comes back as a decimal.</summary>
        private object? Widen(CallNode call, object? value) =>
            value is long integer && decimalCalls.Contains(call) ? (decimal)integer : value;

        private object DateLiteral(CallNode call, bool isDate)
        {
            if (call.Arguments is not [TextLiteral argument])
            {
                throw new InvalidOperationException($"Function '{call.Name}' needs one text literal.");
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

        /// <summary>Keeps the type of its argument, so an integer stays an integer.</summary>
        private static object Abs(CallNode call, object value)
        {
            if (value is decimal number)
            {
                return Math.Abs(number);
            }

            var integer = Integer(value);
            return integer == long.MinValue
                ? throw Fail(ExpressionRuntimeErrorKind.IntegerOverflow, "The result of 'abs' is outside the integer range", call.Offset)
                : Math.Abs(integer);
        }

        /// <summary>Rounds half away from zero. A digit count outside 0 to 28 is an error, because <see cref="decimal"/> holds at most 28.</summary>
        private static decimal Round(CallNode call, decimal number, long digits) =>
            digits is < 0 or > 28
                ? throw Fail(
                    ExpressionRuntimeErrorKind.ArgumentOutOfRange,
                    $"The digit count of 'round' must be from 0 to 28, found {digits}",
                    call.Offset)
                : Math.Round(number, (int)digits, MidpointRounding.AwayFromZero);

        private static DateOnly AddDays(CallNode call, DateOnly date, long days)
        {
            var day = date.DayNumber;
            if (days < DateOnly.MinValue.DayNumber - day || days > DateOnly.MaxValue.DayNumber - day)
            {
                throw Fail(
                    ExpressionRuntimeErrorKind.DateOutOfRange,
                    "The result of 'addDays' is outside 0001-01-01 to 9999-12-31",
                    call.Offset);
            }

            return DateOnly.FromDayNumber((int)(day + days));
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

        private static string Text(object value) => value as string ?? throw Unexpected(value);

        private static long Integer(object value) => value is long integer ? integer : throw Unexpected(value);

        private static DateOnly Date(object value) => value is DateOnly date ? date : throw Unexpected(value);

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
