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
/// expression's precedence. A call to a named rule is inlined: the rule's body is rendered with
/// each parameter replaced by the SQL of its argument. Before any SQL is built, the filter with
/// every rule body inlined is counted against <see cref="ExpressionLimits.MaxInlinedNodes"/>. It
/// stops at the first construct outside the subset and reports only that one.
/// </summary>
public static class SqlTranslator
{
    /// <summary>
    /// Translates <paramref name="expression"/>, which must have passed
    /// <see cref="ExpressionTypeChecker"/>. <paramref name="column"/> maps a field path to the SQL
    /// text of its column. The path is the names as written, so a bare name is a one-item list and
    /// <c>department.name</c> is <c>["department", "name"]</c>. <paramref name="check"/> is the type
    /// checker's result, which says the rule each rule call resolved to. Without it no rule call is
    /// translated.
    /// </summary>
    public static SqlTranslationResult Translate(
        ExpressionNode expression,
        Func<IReadOnlyList<string>, string> column,
        ExpressionCheckResult? check = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(column);

        var ruleCalls = check?.RuleCalls ?? new Dictionary<CallNode, ExpressionRule>(ReferenceEqualityComparer.Instance);
        var parameters = new List<SqlValue>();
        try
        {
            InlinedSize.Check(expression, ruleCalls);
            var sql = new Translation(column, parameters, ruleCalls).Render(expression);
            return new SqlTranslationResult(sql, parameters, null);
        }
        catch (TranslationFailure failure)
        {
            return new SqlTranslationResult(null, [], failure.Diagnostic);
        }
    }

    private sealed class Translation(
        Func<IReadOnlyList<string>, string> column,
        List<SqlValue> parameters,
        IReadOnlyDictionary<CallNode, ExpressionRule> ruleCalls)
    {
        /// <summary>The rule calls of the expression being rendered: the filter's, or the body's inside a rule.</summary>
        private IReadOnlyDictionary<CallNode, ExpressionRule> _ruleCalls = ruleCalls;

        /// <summary>The SQL of each parameter of the rule body being rendered, or null at the filter's top level.</summary>
        private Dictionary<string, string>? _bindings;

        public string Render(ExpressionNode node) => node switch
        {
            IntegerLiteral literal => Parameter(ExpressionTypeKind.Integer, literal.Value),
            DecimalLiteral literal => Parameter(ExpressionTypeKind.Decimal, literal.Value),
            TextLiteral literal => Parameter(ExpressionTypeKind.Text, literal.Value),
            BooleanLiteral literal => Parameter(ExpressionTypeKind.Boolean, literal.Value),
            NullLiteral => "NULL",
            NameNode name => Name(name),
            MemberNode member => _bindings is null
                ? column(Path(member))
                : throw Fail("Paths that do not start at a field are not translated to SQL", member.Offset),
            CallNode call => Call(call),
            UnaryNode unary => unary.Operator == UnaryOperator.Not
                ? $"(NOT {Render(unary.Operand)})"
                : $"(-{Render(unary.Operand)})",
            BinaryNode binary => Binary(binary),
            IsNullNode isNull => $"({Render(isNull.Operand)} IS {(isNull.Negated ? "NOT " : "")}NULL)",
            InNode inNode => In(inNode),
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name, "Unknown node type."),
        };

        /// <summary>A field or data source parameter at the top level, or the argument's SQL inside a rule body.</summary>
        private string Name(NameNode name)
        {
            if (_bindings is null)
            {
                return column([name.Name]);
            }

            return _bindings.TryGetValue(name.Name, out var sql)
                ? sql
                : throw new InvalidOperationException($"The rule body names '{name.Name}', which is not one of its parameters.");
        }

        /// <summary>The names of a path, first to last. A path must start at a name.</summary>
        private static List<string> Path(MemberNode member)
        {
            var names = new List<string>();
            ExpressionNode node = member;
            while (node is MemberNode inner)
            {
                names.Add(inner.Name);
                node = inner.Target;
            }

            if (node is not NameNode first)
            {
                throw Fail("Paths that do not start at a field are not translated to SQL", member.Offset);
            }

            names.Add(first.Name);
            names.Reverse();
            return names;
        }

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
            if (_ruleCalls.TryGetValue(call, out var rule))
            {
                return RuleCall(call, rule);
            }

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

        /// <summary>
        /// Renders every argument once, in the caller's context, then the rule's body with each
        /// parameter bound to its argument's SQL. An argument for a decimal parameter and the result of
        /// a decimal rule are cast to numeric, as the interpreter widens an integer to a decimal there.
        /// A problem inside the body is reported at the call, naming the rule.
        /// </summary>
        private string RuleCall(CallNode call, ExpressionRule rule)
        {
            if (rule.Body is null || rule.BodyCheck is null)
            {
                throw Fail($"Rule '{rule.Name}' has no checked expression", call.Offset);
            }

            var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < call.Arguments.Count; i++)
            {
                var parameter = rule.Parameters[i];
                var argument = Render(call.Arguments[i]);
                bindings[parameter.Name] = parameter.Type.Kind == ExpressionTypeKind.Decimal ? $"({argument})::numeric" : argument;
            }

            var outerBindings = _bindings;
            var outerRuleCalls = _ruleCalls;
            _bindings = bindings;
            _ruleCalls = rule.BodyCheck.RuleCalls;
            string body;
            try
            {
                body = Render(rule.Body);
            }
            catch (TranslationFailure inner)
            {
                throw Fail($"Rule '{rule.Name}' is not translated to SQL: {inner.Reason}", call.Offset);
            }
            finally
            {
                _bindings = outerBindings;
                _ruleCalls = outerRuleCalls;
            }

            return rule.ResultType.Kind == ExpressionTypeKind.Decimal ? $"(({body})::numeric)" : $"({body})";
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

    /// <summary>
    /// The problem <paramref name="reason"/> at <paramref name="offset"/>. A <paramref name="hint"/>
    /// follows the position, as in the parser's messages.
    /// </summary>
    private static TranslationFailure Fail(string reason, int offset, string? hint = null) =>
        new(
            reason,
            new ExpressionDiagnostic(
                ExpressionDiagnosticCodes.OutsideSqlSubset,
                $"{reason} at character {offset + 1}.{(hint is null ? "" : $" {hint}.")}",
                offset));

    /// <summary>Stops translating at the first problem. Only <see cref="Translate"/> catches it.</summary>
    private sealed class TranslationFailure(string reason, ExpressionDiagnostic diagnostic) : Exception(diagnostic.Message)
    {
        /// <summary>The message without its position, so a rule call can quote it.</summary>
        public string Reason { get; } = reason;

        public ExpressionDiagnostic Diagnostic { get; } = diagnostic;
    }

    /// <summary>
    /// Counts the syntax nodes of an expression with every rule body inlined, as the parser counts
    /// them: each literal, name, operator, call, path step, <c>is null</c>, <c>in</c> and <c>in</c>
    /// item is one. A call's arguments count once, however often the body uses them, and a body
    /// counts once per call. It stops at the first node past
    /// <see cref="ExpressionLimits.MaxInlinedNodes"/>, so the cost is bounded however deep the
    /// rules go. A rule without a checked body adds only its call and arguments.
    /// </summary>
    private sealed class InlinedSize
    {
        private int _count;

        public static void Check(ExpressionNode node, IReadOnlyDictionary<CallNode, ExpressionRule> ruleCalls) =>
            new InlinedSize().Walk(node, ruleCalls, outer: null);

        /// <summary><paramref name="outer"/> is the call the filter makes, and its rule, while inside a body.</summary>
        private void Walk(
            ExpressionNode node, IReadOnlyDictionary<CallNode, ExpressionRule> ruleCalls, (CallNode Call, ExpressionRule Rule)? outer)
        {
            if (++_count > ExpressionLimits.MaxInlinedNodes)
            {
                // A filter alone is at most ExpressionLimits.MaxNodes, so the cap is passed inside a body.
                var (call, rule) = outer ?? throw new InvalidOperationException("The filter passed the inlined node cap outside any rule.");
                throw Fail(
                    $"Rule '{rule.Name}' inlines more than {ExpressionLimits.MaxInlinedNodes.ToString("N0", CultureInfo.InvariantCulture)} syntax nodes",
                    call.Offset,
                    "Simplify the rule");
            }

            switch (node)
            {
                case MemberNode member:
                    Walk(member.Target, ruleCalls, outer);
                    break;
                case CallNode call:
                    foreach (var argument in call.Arguments)
                    {
                        Walk(argument, ruleCalls, outer);
                    }

                    if (ruleCalls.TryGetValue(call, out var rule) && rule.Body is not null && rule.BodyCheck is not null)
                    {
                        Walk(rule.Body, rule.BodyCheck.RuleCalls, outer ?? (call, rule));
                    }

                    break;
                case UnaryNode unary:
                    Walk(unary.Operand, ruleCalls, outer);
                    break;
                case BinaryNode binary:
                    Walk(binary.Left, ruleCalls, outer);
                    Walk(binary.Right, ruleCalls, outer);
                    break;
                case IsNullNode isNull:
                    Walk(isNull.Operand, ruleCalls, outer);
                    break;
                case InNode inNode:
                    Walk(inNode.Operand, ruleCalls, outer);
                    foreach (var item in inNode.Items)
                    {
                        Walk(item, ruleCalls, outer);
                    }

                    break;
            }
        }
    }
}
