using System.Globalization;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Evaluation;
using Axis.Expressions.Functions;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Expressions.Sql;

/// <summary>
/// Translates a type-checked expression to a PostgreSQL condition, following the SQL subset in
/// docs/reference/expressions.md. It is the only definition of that subset: the compiler calls it
/// to report what does not translate, and the data source query calls it to build its
/// <c>WHERE</c> clause. Every literal value becomes a named parameter <c>@f0</c>, <c>@f1</c>, …,
/// so no value is ever part of the SQL text. Only the literal <c>null</c> is written, as the
/// keyword <c>NULL</c>. Every operator is wrapped in parentheses, so the SQL keeps the
/// expression's precedence. It stops at the first construct outside the subset and reports only that one.
/// </summary>
public static class SqlTranslator
{
    /// <summary>
    /// Translates <paramref name="expression"/>, which must have passed
    /// <see cref="ExpressionTypeChecker"/>. <paramref name="column"/> maps a bare name, as written,
    /// to the SQL text of its column.
    /// </summary>
    public static SqlTranslationResult Translate(ExpressionNode expression, Func<string, string> column)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(column);

        var parameters = new List<SqlValue>();
        try
        {
            var sql = new Translation(column, parameters).Render(expression);
            return new SqlTranslationResult(sql, parameters, null);
        }
        catch (TranslationFailure failure)
        {
            return new SqlTranslationResult(null, [], failure.Diagnostic);
        }
    }

    private sealed class Translation(Func<string, string> column, List<SqlValue> parameters)
    {
        public string Render(ExpressionNode node) => node switch
        {
            IntegerLiteral literal => Parameter(ExpressionTypeKind.Integer, literal.Value),
            DecimalLiteral literal => Parameter(ExpressionTypeKind.Decimal, literal.Value),
            TextLiteral literal => Parameter(ExpressionTypeKind.Text, literal.Value),
            BooleanLiteral literal => Parameter(ExpressionTypeKind.Boolean, literal.Value),
            NullLiteral => "NULL",
            NameNode name => column(name.Name),
            MemberNode member => throw Fail("Paths are not translated to SQL", member.Offset),
            CallNode call => Call(call),
            UnaryNode unary => unary.Operator == UnaryOperator.Not
                ? $"(NOT {Render(unary.Operand)})"
                : $"(-{Render(unary.Operand)})",
            BinaryNode binary => Binary(binary),
            IsNullNode isNull => $"({Render(isNull.Operand)} IS {(isNull.Negated ? "NOT " : "")}NULL)",
            InNode inNode => In(inNode),
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name, "Unknown node type."),
        };

        private string Binary(BinaryNode binary)
        {
            if (binary.Operator == BinaryOperator.Divide)
            {
                throw Fail("Operator '/' is not translated to SQL", binary.Offset);
            }

            var left = Render(binary.Left);
            var right = Render(binary.Right);
            var op = binary.Operator switch
            {
                BinaryOperator.Or => "OR",
                BinaryOperator.And => "AND",
                BinaryOperator.Equal => "IS NOT DISTINCT FROM",
                BinaryOperator.NotEqual => "IS DISTINCT FROM",
                BinaryOperator.Less => "<",
                BinaryOperator.LessOrEqual => "<=",
                BinaryOperator.Greater => ">",
                BinaryOperator.GreaterOrEqual => ">=",
                BinaryOperator.Add => "+",
                BinaryOperator.Subtract => "-",
                BinaryOperator.Multiply => "*",
                _ => throw new ArgumentOutOfRangeException(nameof(binary), binary.Operator, "Unknown operator."),
            };
            return $"({left} {op} {right})";
        }

        /// <summary>A <c>null</c> operand is false, never <c>null</c>, as in the interpreter. The operand is rendered once.</summary>
        private string In(InNode inNode)
        {
            var operand = Render(inNode.Operand);
            var items = string.Join(", ", inNode.Items.Select(Render));
            return $"({operand} IS NOT NULL AND {operand} IN ({items}))";
        }

        private string Call(CallNode call)
        {
            if (!ExpressionFunctions.TryGet(call.Name, out var signature))
            {
                throw NotTranslated(call.Name, call.Offset);
            }

            var name = signature.Name;
            if (signature.IsAggregate)
            {
                // A collection has no column, so no aggregate is in the subset.
                throw NotTranslated(name, call.Offset);
            }

            switch (name)
            {
                case "date" or "dateTime":
                    return DateLiteral(call, name);
                case "lower" or "upper" or "trim":
                    throw NotTranslated(name, call.Offset);
            }

            var arguments = call.Arguments.Select(Render).ToList();
            return name switch
            {
                "if" => $"(CASE WHEN {arguments[0]} THEN {arguments[1]} ELSE {arguments[2]} END)",
                "coalesce" => $"COALESCE({string.Join(", ", arguments)})",
                "concat" => $"concat({string.Join(", ", arguments)})",
                "length" => $"char_length({arguments[0]})",
                "contains" => $"(strpos({arguments[0]}, {arguments[1]}) > 0)",
                "startsWith" => $"starts_with({arguments[0]}, {arguments[1]})",
                "endsWith" => $"(right({arguments[0]}, char_length({arguments[1]})) = {arguments[1]})",
                "abs" => $"abs({arguments[0]})",
                // PostgreSQL takes floor(bigint) as double precision, so the argument is made exact first.
                "floor" => $"floor(({arguments[0]})::numeric)",
                "ceiling" => $"ceil(({arguments[0]})::numeric)",
                "round" => $"round(({arguments[0]})::numeric, ({arguments[1]})::int)",
                "year" or "month" or "day" => $"((extract({name} from {arguments[0]}))::bigint)",
                "addDays" => $"({arguments[0]} + ({arguments[1]})::int)",
                "daysBetween" => $"({arguments[1]} - {arguments[0]})",
                _ => throw NotTranslated(name, call.Offset),
            };
        }

        /// <summary>The text is parsed here, so the value is sent as a typed date or date-time parameter.</summary>
        private string DateLiteral(CallNode call, string name)
        {
            if (call.Arguments is not [TextLiteral { Value: var text }])
            {
                throw Fail($"Function '{name}' needs one text literal", call.Offset);
            }

            if (name == "date")
            {
                return DateLiterals.TryParseDate(text, out var date)
                    ? Parameter(ExpressionTypeKind.Date, date)
                    : throw Fail($"'{text}' is not a valid date", call.Offset);
            }

            return DateLiterals.TryParseDateTime(text, out var dateTime)
                ? Parameter(ExpressionTypeKind.DateTime, dateTime)
                : throw Fail($"'{text}' is not a valid date-time", call.Offset);
        }

        private string Parameter(ExpressionTypeKind kind, object value)
        {
            var name = "f" + parameters.Count.ToString(CultureInfo.InvariantCulture);
            parameters.Add(new SqlValue(name, kind, value));
            return "@" + name;
        }
    }

    private static TranslationFailure NotTranslated(string function, int offset) =>
        Fail($"Function '{function}' is not translated to SQL", offset);

    private static TranslationFailure Fail(string message, int offset) =>
        new(new ExpressionDiagnostic(ExpressionDiagnosticCodes.OutsideSqlSubset, $"{message} at character {offset + 1}.", offset));

    /// <summary>Stops translating at the first problem. Only <see cref="Translate"/> catches it.</summary>
    private sealed class TranslationFailure(ExpressionDiagnostic diagnostic) : Exception(diagnostic.Message)
    {
        public ExpressionDiagnostic Diagnostic { get; } = diagnostic;
    }
}
