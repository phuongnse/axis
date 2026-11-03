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
/// or with another prefix the caller gives, so no value is ever part of the SQL text. Only the literal <c>null</c> is written, as the
/// keyword <c>NULL</c>. <c>today()</c> and <c>toDate</c> convert with <c>AT TIME ZONE</c> and a text parameter
/// that holds the application's time zone id, UTC when the caller gives none. <c>currentUser()</c> becomes the named parameter
/// <see cref="CurrentUserParameter"/>, which is not in the result's parameters. Every operator is wrapped in parentheses, so the SQL keeps the
/// expression's precedence. A call to a named rule is inlined: the rule's body is rendered with
/// each parameter replaced by the SQL of its argument. Before any SQL is built, the filter with
/// every rule body inlined is counted against <see cref="ExpressionLimits.MaxInlinedNodes"/>. It
/// stops at the first construct outside the subset and reports only that one. <c>any(list, condition)</c>,
/// <c>count(list)</c> and <c>count(list, condition)</c> over a child collection or a references field
/// at the filter's top level are subqueries, <c>EXISTS</c> or <c>count(*)</c>, over the source the
/// caller's <c>collection</c> callback gives. A row whose condition is null does not count. Without that
/// callback, and for every other aggregate, an aggregate is not in the subset. With a
/// <c>lookupColumn</c> callback, <c>field == literal</c> and <c>field != literal</c> at the filter's top level,
/// in either order and with a literal other than <c>null</c>, over a field the callback gives a lookup column for,
/// compare that column with a <c>bytea[]</c> parameter <c>@fl0</c>, <c>@fl1</c>, … (the parameter prefix and <c>l</c>) instead of the field. The literal
/// is not a parameter then, it is reported in <see cref="SqlTranslationResult.Lookups"/> for the caller to hash.
/// The other side may also be a bare name that is not a lookup field: the caller's <c>column</c> callback renders it, as the
/// SQL of a data source parameter, and the lookup reports its name in <see cref="SqlLookup.Parameter"/> for the caller to hash the
/// parameter's value. A <c>NULL</c> parameter then makes <c>==</c> match an empty field. <c>==</c> is never null,
/// and <c>!=</c> also holds for an empty field, as <c>IS DISTINCT FROM</c> does.
/// </summary>
public static class SqlTranslator
{
    /// <summary>
    /// The name of the parameter <c>currentUser()</c> becomes, without its <c>@</c>. The caller
    /// binds it as text to the signed-in user's id.
    /// </summary>
    public const string CurrentUserParameter = "currentUser";

    /// <summary>
    /// The SQL that <c>now()</c> becomes: the PostgreSQL session setting <c>axis.now</c> when it is
    /// set, and the transaction start time otherwise. Only a test scenario sets it, to fix the time.
    /// </summary>
    public const string NowSql = "coalesce(nullif(current_setting('axis.now', true), '')::timestamptz, now())";

    /// <summary>
    /// Translates <paramref name="expression"/>, which must have passed
    /// <see cref="ExpressionTypeChecker"/>. Without a <paramref name="check"/> the expression need
    /// only parse: a call with the wrong number of arguments is reported as
    /// <see cref="ExpressionDiagnosticCodes.WrongArgumentCount"/>, and no rule call is translated.
    /// <paramref name="column"/> maps a field path to the SQL
    /// text of its column. The path is the names as written, so a bare name is a one-item list and
    /// <c>department.name</c> is <c>["department", "name"]</c>. <paramref name="check"/> is the type
    /// checker's result, which says the rule each rule call resolved to. Without it no rule call is
    /// translated. <paramref name="parameterPrefix"/> starts each parameter name, so several
    /// translations can share one statement with different prefixes. <paramref name="timeZone"/> is the
    /// application's time zone, which <c>today()</c> and <c>toDate</c> use. It is UTC when null.
    /// <paramref name="collection"/> gives the subquery source of the child collection or references
    /// field named in <c>any</c> or <c>count</c>. Without it, every aggregate is not translated.
    /// <paramref name="lookupColumn"/> maps a field path to the SQL text of its lookup column, or to
    /// null when the field has none.
    /// </summary>
    public static SqlTranslationResult Translate(
        ExpressionNode expression,
        Func<IReadOnlyList<string>, string> column,
        ExpressionCheckResult? check = null,
        string parameterPrefix = "f",
        TimeZoneInfo? timeZone = null,
        Func<string, SqlCollection>? collection = null,
        Func<IReadOnlyList<string>, string?>? lookupColumn = null) =>
        TranslateCountingNodes(expression, column, check, parameterPrefix, timeZone, collection, lookupColumn).Result;

    /// <summary>
    /// Translates like <see cref="Translate"/> and also gives the syntax nodes counted for the
    /// inlining cap, so a test can show that the cap stops a rule chain without timing it.
    /// </summary>
    internal static (SqlTranslationResult Result, int InlinedNodes) TranslateCountingNodes(
        ExpressionNode expression,
        Func<IReadOnlyList<string>, string> column,
        ExpressionCheckResult? check = null,
        string parameterPrefix = "f",
        TimeZoneInfo? timeZone = null,
        Func<string, SqlCollection>? collection = null,
        Func<IReadOnlyList<string>, string?>? lookupColumn = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(column);
        ArgumentException.ThrowIfNullOrEmpty(parameterPrefix);

        var ruleCalls = check?.RuleCalls ?? new Dictionary<CallNode, ExpressionRule>(ReferenceEqualityComparer.Instance);
        var parameters = new List<SqlValue>();
        var lookups = new List<SqlLookup>();
        var size = new InlinedSize();
        try
        {
            size.Check(expression, ruleCalls);
            var sql = new Translation(
                column,
                collection,
                parameters,
                ruleCalls,
                parameterPrefix,
                timeZone ?? TimeZoneInfo.Utc,
                lookupColumn,
                lookups).Render(expression);
            return (new SqlTranslationResult(sql, parameters, null) { Lookups = lookups }, size.Nodes);
        }
        catch (TranslationFailure failure)
        {
            return (new SqlTranslationResult(null, [], failure.Diagnostic), size.Nodes);
        }
    }

    private sealed class Translation(
        Func<IReadOnlyList<string>, string> column,
        Func<string, SqlCollection>? collection,
        List<SqlValue> parameters,
        IReadOnlyDictionary<CallNode, ExpressionRule> ruleCalls,
        string parameterPrefix,
        TimeZoneInfo timeZone,
        Func<IReadOnlyList<string>, string?>? lookupColumn,
        List<SqlLookup> lookups)
    {
        /// <summary>The rule calls of the expression being rendered: the filter's, or the body's inside a rule.</summary>
        private IReadOnlyDictionary<CallNode, ExpressionRule> _ruleCalls = ruleCalls;

        /// <summary>The SQL of each parameter of the rule body being rendered, or null at the filter's top level.</summary>
        private Dictionary<string, string>? _bindings;

        /// <summary>The column callback: the filter's, or the item columns of the collection whose condition is being rendered.</summary>
        private Func<IReadOnlyList<string>, string> _column = column;

        /// <summary>The filter's own column callback, which <see cref="_column"/> is while no collection's condition is being rendered.</summary>
        private readonly Func<IReadOnlyList<string>, string> _filterColumn = column;

        public string Render(ExpressionNode node) => node switch
        {
            IntegerLiteral literal => Parameter(ExpressionTypeKind.Integer, literal.Value),
            DecimalLiteral literal => Parameter(ExpressionTypeKind.Decimal, literal.Value),
            TextLiteral literal => Parameter(ExpressionTypeKind.Text, literal.Value),
            BooleanLiteral literal => Parameter(ExpressionTypeKind.Boolean, literal.Value),
            NullLiteral => "NULL",
            NameNode name => Name(name),
            MemberNode member => _bindings is null
                ? _column(Path(member))
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
                return _column([name.Name]);
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

            if (string.Equals(first.Name, ExpressionTypeChecker.OuterName, StringComparison.OrdinalIgnoreCase))
            {
                throw Fail("'outer' is not translated to SQL", member.Offset);
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

            if (Lookup(binary) is { } lookup)
            {
                return lookup;
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

        /// <summary>
        /// The exact lookup form of <c>field == value</c> or <c>field != value</c>, in either order, or null when
        /// <paramref name="binary"/> is not one the callback gives a lookup column for. The value is a literal or a
        /// bare name that is not a lookup field, which the caller's column callback renders as a data source parameter.
        /// Neither form is ever SQL <c>NULL</c>, so both keep the meaning of <c>IS NOT DISTINCT FROM</c> and
        /// <c>IS DISTINCT FROM</c>, also under <c>NOT</c>. Against a name, a <c>NULL</c> value matches an empty field.
        /// </summary>
        private string? Lookup(BinaryNode binary)
        {
            if (lookupColumn is null
                || binary.Operator is not (BinaryOperator.Equal or BinaryOperator.NotEqual)
                || _bindings is not null
                || !ReferenceEquals(_column, _filterColumn))
            {
                return null;
            }

            return LookupOf(binary.Left, binary.Right, binary.Operator) ?? LookupOf(binary.Right, binary.Left, binary.Operator);
        }

        /// <summary>The lookup form of the test of <paramref name="field"/> against <paramref name="value"/>, or null when it is not one.</summary>
        private string? LookupOf(ExpressionNode field, ExpressionNode value, BinaryOperator op)
        {
            if (field is not NameNode name)
            {
                return null;
            }

            IReadOnlyList<string> path = [name.Name];
            if (lookupColumn!(path) is not { } sql)
            {
                return null;
            }

            var parameter = parameterPrefix + "l" + lookups.Count.ToString(CultureInfo.InvariantCulture);
            if (LiteralValue(value) is var (kind, literal))
            {
                lookups.Add(new SqlLookup(parameter, path, kind, literal));
                return op == BinaryOperator.Equal
                    ? $"({sql} IS NOT NULL AND {sql} = ANY(@{parameter}))"
                    : $"({sql} IS NULL OR NOT ({sql} = ANY(@{parameter})))";
            }

            // A name that is itself a lookup field is not a data source parameter.
            if (value is not NameNode valueName || lookupColumn([valueName.Name]) is not null)
            {
                return null;
            }

            var given = Name(valueName);
            lookups.Add(new SqlLookup(parameter, path, default, null) { Parameter = valueName.Name });
            var equal = $"(({given} IS NULL AND {sql} IS NULL) OR ({given} IS NOT NULL AND {sql} IS NOT NULL AND {sql} = ANY(@{parameter})))";
            return op == BinaryOperator.Equal ? equal : $"(NOT {equal})";
        }

        private static (ExpressionTypeKind Kind, object Value)? LiteralValue(ExpressionNode node) => node switch
        {
            IntegerLiteral literal => (ExpressionTypeKind.Integer, literal.Value),
            DecimalLiteral literal => (ExpressionTypeKind.Decimal, literal.Value),
            TextLiteral literal => (ExpressionTypeKind.Text, literal.Value),
            BooleanLiteral literal => (ExpressionTypeKind.Boolean, literal.Value),
            _ => null,
        };

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

            if (call.Module is { } module)
            {
                throw NotTranslated($"{module}:{call.Name}", call.Offset);
            }

            if (!ExpressionFunctions.TryGet(call.Name, out var signature))
            {
                throw NotTranslated(call.Name, call.Offset);
            }

            var name = signature.Name;
            var count = call.Arguments.Count;
            if (count < signature.MinArguments || count > signature.MaxArguments)
            {
                // A checked expression never gets here. An expression translated without a check can.
                var needs = signature.MaxArguments == int.MaxValue ? $"at least {signature.MinArguments}" : $"{signature.MinArguments}";
                var noun = signature.MinArguments == 1 ? "argument" : "arguments";
                throw Fail($"Function '{name}' needs {needs} {noun}, found {count}", call.Offset, code: ExpressionDiagnosticCodes.WrongArgumentCount);
            }

            if (signature.IsAggregate)
            {
                return name is "any" or "count" && collection is not null
                    ? Aggregate(call, name)
                    : throw NotTranslated(name, call.Offset);
            }

            switch (name)
            {
                case "date" or "dateTime":
                    return DateLiteral(call, name);
                case "lower" or "upper" or "trim" or "previous" or "accountableUser" or "decisionTask":
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
                "now" => NowSql,
                "today" => $"(({NowSql}) AT TIME ZONE {Parameter(ExpressionTypeKind.Text, timeZone.Id)})::date",
                // The cast keeps a bare NULL argument unambiguous.
                "toDate" => $"((({arguments[0]})::timestamptz) AT TIME ZONE {Parameter(ExpressionTypeKind.Text, timeZone.Id)})::date",
                "currentUser" => "@" + CurrentUserParameter,
                _ => throw NotTranslated(name, call.Offset),
            };
        }

        /// <summary>
        /// <c>any</c> as <c>EXISTS</c> and <c>count</c> as <c>count(*)</c> over the rows of the collection
        /// that belong to the outer row and, with a condition, make it true, so a null condition keeps no row.
        /// Only the filter's top level translates one: a rule body has no collections, and an item expression has none either.
        /// A path to a collection is not translated.
        /// </summary>
        private string Aggregate(CallNode call, string name)
        {
            if (call.Arguments[0] is MemberNode)
            {
                throw Fail("A path to a collection is not translated to SQL", call.Offset);
            }

            if (_bindings is not null || !ReferenceEquals(_column, _filterColumn) || call.Arguments[0] is not NameNode list)
            {
                throw Fail("Aggregates are only translated at the filter's top level", call.Offset);
            }

            var source = collection!(list.Name);
            var where = source.OwnerMatch;
            if (call.Arguments.Count > 1)
            {
                var outer = _column;
                _column = source.Column;
                try
                {
                    where = $"{where} AND ({Render(call.Arguments[1])})";
                }
                finally
                {
                    _column = outer;
                }
            }

            return name == "any"
                ? $"EXISTS (SELECT 1 FROM {source.From} WHERE {where})"
                : $"(SELECT count(*) FROM {source.From} WHERE {where})";
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
            var name = parameterPrefix + parameters.Count.ToString(CultureInfo.InvariantCulture);
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
    private static TranslationFailure Fail(string reason, int offset, string? hint = null, string code = ExpressionDiagnosticCodes.OutsideSqlSubset) =>
        new(
            reason,
            new ExpressionDiagnostic(
                code,
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

        /// <summary>The last rule call the filter makes itself whose body was counted.</summary>
        private (CallNode Call, ExpressionRule Rule)? _lastOuter;

        /// <summary>The nodes counted so far, one past the cap when the cap stopped the walk.</summary>
        public int Nodes => _count;

        public void Check(ExpressionNode node, IReadOnlyDictionary<CallNode, ExpressionRule> ruleCalls) =>
            Walk(node, ruleCalls, outer: null);

        /// <summary><paramref name="outer"/> is the call the filter makes, and its rule, while inside a body.</summary>
        private void Walk(
            ExpressionNode node, IReadOnlyDictionary<CallNode, ExpressionRule> ruleCalls, (CallNode Call, ExpressionRule Rule)? outer)
        {
            if (++_count > ExpressionLimits.MaxInlinedNodes)
            {
                var cap = ExpressionLimits.MaxInlinedNodes.ToString("N0", CultureInfo.InvariantCulture);

                // The count is cumulative, so the cap can be passed by a node of the filter itself after
                // a large body. Then the last rule the filter called is the one to simplify.
                if ((outer ?? _lastOuter) is var (call, rule))
                {
                    throw Fail($"Rule '{rule.Name}' inlines more than {cap} syntax nodes", call.Offset, "Simplify the rule");
                }

                // A filter alone is at most ExpressionLimits.MaxNodes, so this needs a hand-built tree.
                throw Fail($"The filter has more than {cap} syntax nodes", node.Offset, "Simplify the filter");
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
                        if (outer is null)
                        {
                            _lastOuter = (call, rule);
                        }

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
