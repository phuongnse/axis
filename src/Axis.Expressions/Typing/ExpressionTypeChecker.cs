using Axis.Expressions.Diagnostics;
using Axis.Expressions.Functions;
using Axis.Expressions.Syntax;

namespace Axis.Expressions.Typing;

/// <summary>
/// Finds the type of a parsed expression, following the typing rules in
/// docs/reference/expressions.md, and checks it against the type the use needs. It covers
/// literals, bare field names, every operator, every function in
/// <see cref="ExpressionFunctions"/> and calls to the named rules in the scope. A path through
/// reference fields resolves only in a scope that resolves paths, and is an unknown name in any
/// other scope. It stops at the first problem and reports only that one.
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
            var context = new Context(
                scope,
                new HashSet<CallNode>(ReferenceEqualityComparer.Instance),
                new Dictionary<CallNode, ExpressionRule>(ReferenceEqualityComparer.Instance));
            var actual = Infer(expression, context);
            if (!Fits(expression, actual, expected))
            {
                return new ExpressionCheckResult(null, new ExpressionDiagnostic(
                    ExpressionDiagnosticCodes.ResultTypeMismatch,
                    $"The expression must be {expected}, but it is {actual}.",
                    0));
            }

            return new ExpressionCheckResult(actual, null) { DecimalCalls = context.DecimalCalls, RuleCalls = context.RuleCalls };
        }
        catch (CheckFailure failure)
        {
            return new ExpressionCheckResult(null, failure.Diagnostic);
        }
    }

    private static ExpressionType Infer(ExpressionNode node, Context context) => node switch
    {
        IntegerLiteral => ExpressionType.Integer,
        DecimalLiteral => ExpressionType.Decimal,
        TextLiteral => ExpressionType.Text,
        BooleanLiteral => ExpressionType.Boolean,
        NullLiteral => ExpressionType.Null,
        NameNode name => context.Scope.TryGetField(name.Name, out var type)
            ? type
            : throw Fail(ExpressionDiagnosticCodes.UnknownName, $"Unknown field '{name.Name}'", name.Offset),
        MemberNode member => InferMember(member, context),
        CallNode call => InferCall(call, context),
        UnaryNode unary => InferUnary(unary, Infer(unary.Operand, context)),
        BinaryNode binary => InferBinary(binary, Infer(binary.Left, context), Infer(binary.Right, context)),
        IsNullNode isNull => InferIsNull(isNull, context),
        InNode inNode => InferIn(inNode, context),
        _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name, "Unknown node type."),
    };

    /// <summary>
    /// A path such as <c>department.manager.name</c>: it starts at a field of the scope, each name
    /// before the last is a reference field, and it takes at most <see cref="ExpressionLimits.MaxHops"/>
    /// hops. Each <c>.</c> is one hop. A reference parameter holds an id and not a row, so a path
    /// cannot start at it.
    /// </summary>
    private static ExpressionType InferMember(MemberNode member, Context context)
    {
        if (!context.Scope.ResolvesPaths)
        {
            throw Fail(ExpressionDiagnosticCodes.UnknownName, $"Unknown field '{member.Name}' after '.'", member.Offset);
        }

        if (member.Target is not (NameNode or MemberNode))
        {
            throw Fail(ExpressionDiagnosticCodes.TypeMismatch, "Operator '.' needs a field path", member.Offset);
        }

        var target = Infer(member.Target, context);
        var hops = 1;
        for (var node = member.Target; node is MemberNode inner; node = inner.Target)
        {
            hops++;
        }

        if (hops > ExpressionLimits.MaxHops)
        {
            throw Fail(
                ExpressionDiagnosticCodes.TooManyHops,
                $"The path takes {hops} hops, at most {ExpressionLimits.MaxHops} are allowed",
                member.Offset);
        }

        if (target.Kind != ExpressionTypeKind.Reference)
        {
            throw Fail(ExpressionDiagnosticCodes.TypeMismatch, $"Operator '.' needs a reference, found {target}", member.Offset);
        }

        if (target.IsParameter)
        {
            // A parameter is always a bare name, because no path ends at a parameter.
            throw Fail(
                ExpressionDiagnosticCodes.TypeMismatch,
                $"Operator '.' cannot follow the parameter '{((NameNode)member.Target).Name}'. A path starts at a field",
                member.Offset);
        }

        return context.Scope.TryGetReferenceField(target, member.Name, out var type)
            ? type
            : throw Fail(ExpressionDiagnosticCodes.UnknownName, $"Unknown field '{member.Name}' of '{target.Source}'", member.Offset);
    }

    private static ExpressionType InferCall(CallNode call, Context context)
    {
        if (!ExpressionFunctions.TryGet(call.Name, out var signature))
        {
            return context.Scope.TryGetRule(call.Name, out var rule)
                ? InferRuleCall(call, rule, context)
                : throw Fail(ExpressionDiagnosticCodes.UnknownFunction, $"Unknown function or rule '{call.Name}'", call.Offset);
        }

        var name = signature.Name;
        var count = call.Arguments.Count;
        if (count < signature.MinArguments || count > signature.MaxArguments)
        {
            var needs = signature.MaxArguments == int.MaxValue ? $"at least {signature.MinArguments}" : $"{signature.MinArguments}";
            var noun = signature.MinArguments == 1 ? "argument" : "arguments";
            throw Fail(
                ExpressionDiagnosticCodes.WrongArgumentCount,
                $"Function '{name}' needs {needs} {noun}, found {count}",
                call.Offset);
        }

        var types = new ExpressionType[count];
        for (var i = 0; i < count; i++)
        {
            types[i] = Infer(call.Arguments[i], context);
        }

        switch (name)
        {
            case "length":
                Need(call, name, types, 0, IsText, "text");
                return ExpressionType.Integer;

            case "contains" or "startsWith" or "endsWith":
                Need(call, name, types, 0, IsText, "text");
                Need(call, name, types, 1, IsText, "text");
                return ExpressionType.Boolean;

            case "concat" or "lower" or "upper" or "trim":
                for (var i = 0; i < count; i++)
                {
                    Need(call, name, types, i, IsText, "text");
                }

                return ExpressionType.Text;

            case "abs" or "floor" or "ceiling":
                Need(call, name, types, 0, IsNumber, "a number");
                return types[0];

            case "round":
                Need(call, name, types, 0, IsNumber, "a number");
                Need(call, name, types, 1, IsInteger, "integer");
                return ExpressionType.Decimal;

            case "year" or "month" or "day":
                Need(call, name, types, 0, IsDate, "date");
                return ExpressionType.Integer;

            case "addDays":
                Need(call, name, types, 0, IsDate, "date");
                Need(call, name, types, 1, IsInteger, "integer");
                return ExpressionType.Date;

            case "daysBetween":
                Need(call, name, types, 0, IsDate, "date");
                Need(call, name, types, 1, IsDate, "date");
                return ExpressionType.Integer;

            case "coalesce":
                return Combine(call, name, types, 0, context);

            case "if":
                Need(call, name, types, 0, type => type.Kind == ExpressionTypeKind.Boolean, "boolean");
                return Combine(call, name, types, 1, context);

            case "date" or "dateTime":
                if (call.Arguments is not [TextLiteral])
                {
                    throw Fail(ExpressionDiagnosticCodes.TypeMismatch, $"Function '{name}' needs one text literal", call.Offset);
                }

                return name == "date" ? ExpressionType.Date : ExpressionType.DateTime;

            default:
                throw new InvalidOperationException($"The type checker has no rule for function '{name}'.");
        }
    }

    /// <summary>
    /// A call to a named rule: it takes exactly one argument per parameter, and each argument must
    /// fit its parameter's type. The call is recorded, so that the interpreter runs the rule's body.
    /// </summary>
    private static ExpressionType InferRuleCall(CallNode call, ExpressionRule rule, Context context)
    {
        var count = call.Arguments.Count;
        var parameters = rule.Parameters;
        if (count != parameters.Count)
        {
            var noun = parameters.Count == 1 ? "argument" : "arguments";
            throw Fail(
                ExpressionDiagnosticCodes.WrongArgumentCount,
                $"Rule '{rule.Name}' needs {parameters.Count} {noun}, found {count}",
                call.Offset);
        }

        for (var i = 0; i < count; i++)
        {
            var argument = call.Arguments[i];
            var type = Infer(argument, context);
            if (!Fits(argument, type, parameters[i].Type))
            {
                throw Fail(
                    ExpressionDiagnosticCodes.TypeMismatch,
                    $"Rule '{rule.Name}' needs {parameters[i].Type} for argument {i + 1}, found {type}",
                    call.Offset);
            }
        }

        context.RuleCalls[call] = rule;
        return rule.ResultType;
    }

    /// <summary>Argument <paramref name="index"/> must be <c>null</c> or a type <paramref name="accepts"/> allows.</summary>
    private static void Need(
        CallNode call, string name, ExpressionType[] types, int index, Func<ExpressionType, bool> accepts, string needs)
    {
        var type = types[index];
        if (type.Kind != ExpressionTypeKind.Null && !accepts(type))
        {
            throw Fail(
                ExpressionDiagnosticCodes.TypeMismatch,
                $"Function '{name}' needs {needs} for argument {index + 1}, found {type}",
                call.Offset);
        }
    }

    /// <summary>
    /// The type of the <c>coalesce</c> arguments or <c>if</c> branches from <paramref name="first"/> on.
    /// Every two of them must fit each other under the rules of <c>==</c>. An integer and a decimal
    /// give a decimal, and an enum and a listed text literal give the enum. A decimal call is recorded,
    /// so that the interpreter returns an integer it picks as a decimal.
    /// </summary>
    private static ExpressionType Combine(CallNode call, string name, ExpressionType[] types, int first, Context context)
    {
        var result = ExpressionType.Null;
        for (var i = first; i < types.Length; i++)
        {
            for (var j = first; j < i; j++)
            {
                if (!Comparable(call.Arguments[j], types[j], call.Arguments[i], types[i]))
                {
                    throw Fail(
                        ExpressionDiagnosticCodes.TypeMismatch,
                        $"Function '{name}' cannot combine {types[j]} and {types[i]}",
                        call.Offset);
                }
            }

            if (result.Kind == ExpressionTypeKind.Null
                || (result.Kind == ExpressionTypeKind.Integer && types[i].Kind == ExpressionTypeKind.Decimal)
                || (result.Kind == ExpressionTypeKind.Text && types[i].Kind == ExpressionTypeKind.Enum))
            {
                result = types[i];
            }
        }

        if (result.Kind == ExpressionTypeKind.Decimal)
        {
            context.DecimalCalls.Add(call);
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

    private static ExpressionType InferIsNull(IsNullNode isNull, Context context)
    {
        Infer(isNull.Operand, context);
        return ExpressionType.Boolean;
    }

    private static ExpressionType InferIn(InNode inNode, Context context)
    {
        var operand = Infer(inNode.Operand, context);
        foreach (var item in inNode.Items)
        {
            var itemType = Infer(item, context);
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
    /// <paramref name="target"/>. <c>null</c> fits any type and an integer widens to a decimal. An
    /// enum parameter fits an enum when all of its values are in the enum's values. A text literal
    /// fits an enum only when it is one of the enum's values. When it is not, that is reported here.
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

        if (type.Kind == ExpressionTypeKind.Enum && target.Kind == ExpressionTypeKind.Enum && type.IsParameter)
        {
            return type.Values.All(value => target.Values.Contains(value, StringComparer.Ordinal));
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

    private static bool IsText(ExpressionType type) => type.Kind == ExpressionTypeKind.Text;

    private static bool IsInteger(ExpressionType type) => type.Kind == ExpressionTypeKind.Integer;

    private static bool IsDate(ExpressionType type) => type.Kind == ExpressionTypeKind.Date;

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

    /// <summary>What one check carries down the tree: the scope, and the decimal and rule calls found so far.</summary>
    private sealed record Context(
        ExpressionScope Scope, HashSet<CallNode> DecimalCalls, Dictionary<CallNode, ExpressionRule> RuleCalls);

    /// <summary>Stops checking at the first problem. Only <see cref="Check"/> catches it.</summary>
    private sealed class CheckFailure(ExpressionDiagnostic diagnostic) : Exception(diagnostic.Message)
    {
        public ExpressionDiagnostic Diagnostic { get; } = diagnostic;
    }
}
