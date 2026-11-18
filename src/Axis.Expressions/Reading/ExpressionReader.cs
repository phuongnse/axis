using Axis.Expressions.Functions;
using Axis.Expressions.Parsing;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;

namespace Axis.Expressions.Reading;

/// <summary>
/// Reads an expression as a tree of groups and phrases that Studio says in words, see
/// docs/reference/authoring.md#expression-check. The tree holds no wording: each phrase names a text, and the texts
/// hold the grammar of every locale. The reader takes the types and fields from the type check, so it keeps no copy of
/// the type rules or the scope rules.
/// </summary>
public static class ExpressionReader
{
    private const string TextType = "text";
    private const string EnumType = "enum";

    /// <summary>The phrase words the reader answers for operators, names and paths, apart from <c>fn.</c> words.</summary>
    public static IReadOnlyList<string> OperatorWords { get; } =
    [
        "equal", "not-equal",
        "less", "less.date", "less-or-equal", "less-or-equal.date",
        "greater", "greater.date", "greater-or-equal", "greater-or-equal.date",
        "is-null", "is-not-null", "in", "is-true", "is-false", "not",
        "add", "subtract", "multiply", "divide", "negate", "of", "rule",
    ];

    /// <summary>
    /// The words of the texts under <c>shell.studio.expression.reads.</c> that every locale holds: the
    /// <see cref="OperatorWords"/>, the texts of values and the group headings and joins.
    /// </summary>
    public static IReadOnlyList<string> TextKeys { get; } =
    [
        .. OperatorWords,
        "value.true", "value.false", "value.null",
        "group.all", "group.any", "group.notAll", "group.none",
        "join.and", "join.or",
    ];

    /// <summary>The reading words of <c>count</c> with a condition. Without one, <c>count</c> reads with <c>fn.count</c>.</summary>
    public const string CountWhereWords = "fn.count.where";

    /// <summary>
    /// The key, under <c>shell.studio.expression.reads.</c>, of the reading words of the function or aggregate
    /// <paramref name="name"/>, such as <c>fn.daysBetween</c>. The reader and the expression scope both name a function by it.
    /// </summary>
    public static string FunctionWords(string name) => $"fn.{ExpressionFunctions.Get(name).Name}";

    /// <summary>
    /// The number of slots that the reader fills for each function's reading words, so each text holds <c>{0}</c> up to
    /// the last slot. A function that takes any number of arguments fills one list slot. <c>count</c> fills one slot for
    /// its list and two for <see cref="CountWhereWords"/>, see docs/reference/authoring.md#expression-check.
    /// </summary>
    public static IReadOnlyDictionary<string, int> FunctionSlots { get; } =
        ExpressionFunctions.Names.Concat(ExpressionFunctions.AggregateNames)
            .Select(name => (Words: FunctionWords(name), Slots: SlotsOf(ExpressionFunctions.Get(name))))
            .Append((Words: CountWhereWords, Slots: 2))
            .ToDictionary(item => item.Words, item => item.Slots);

    private static int SlotsOf(FunctionSignature signature) =>
        signature.MaxArguments == int.MaxValue || signature.Name == "count" ? 1 : signature.MaxArguments;

    /// <summary>
    /// The reading of <paramref name="text"/>, or null when it does not parse or, with a place, does not type check there.
    /// With a place, the text is checked again with the place's own scope, expected type and flags, and the reading
    /// takes the types and the fields from that check. Without one, as at a place the compile checks without a type
    /// check, the names carry no label keys and a comparison of dates reads with the plain words.
    /// </summary>
    public static ReadingNode? Read(string text, ExpressionPlace? place)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parsed = ExpressionParser.Parse(text);
        if (!parsed.Succeeded)
        {
            return null;
        }

        ExpressionCheckResult? check = null;
        if (place is not null)
        {
            check = ExpressionTypeChecker.Check(
                parsed.Expression, place.Scope, place.Expected, place.StoredValue, place.CurrentUser, place.Contract, place.StepSet, place.Sql);
            if (!check.Succeeded)
            {
                return null;
            }
        }

        return new Walker(check).ReadItem(parsed.Expression);
    }

    private sealed class Walker(ExpressionCheckResult? check)
    {
        /// <summary>A node that stands alone: the root, a group item or the operand of <c>not</c>. A boolean field there is yes.</summary>
        public ReadingNode ReadItem(ExpressionNode node) =>
            node is NameNode or MemberNode && TypeOf(node)?.Kind == ExpressionTypeKind.Boolean
                ? One("is-true", ReadPart(node))
                : ReadPart(node);

        private ReadingNode ReadPart(ExpressionNode node) => node switch
        {
            IntegerLiteral literal => new ReadingValue("integer", literal.Value, null),
            DecimalLiteral literal => new ReadingValue("decimal", literal.Value, null),
            TextLiteral literal => new ReadingValue(TextType, literal.Value, null),
            BooleanLiteral literal => new ReadingValue("boolean", literal.Value, null),
            NullLiteral => new ReadingValue("null", null, null),
            NameNode name => ReadName(name),
            MemberNode member => ReadPath(member),
            CallNode call => ReadCall(call),
            UnaryNode { Operator: UnaryOperator.Not } unary => ReadNot(unary),
            UnaryNode unary => ReadNegate(unary),
            BinaryNode { Operator: BinaryOperator.And } binary => Group("all", binary, BinaryOperator.And),
            BinaryNode { Operator: BinaryOperator.Or } binary => Group("any", binary, BinaryOperator.Or),
            BinaryNode binary => ReadBinary(binary),
            IsNullNode isNull => One(isNull.Negated ? "is-not-null" : "is-null", ReadPart(isNull.Operand)),
            InNode inNode => ReadIn(inNode),
            _ => throw new ArgumentOutOfRangeException(nameof(node), node.GetType().Name, "Unknown node type."),
        };

        /// <summary>A run of one operator as one group, however the parser nested it.</summary>
        private ReadingGroup Group(string kind, ExpressionNode node, BinaryOperator op)
        {
            List<ReadingNode> items = [];
            Collect(node, op, items);
            return new ReadingGroup(kind, items);
        }

        private void Collect(ExpressionNode node, BinaryOperator op, List<ReadingNode> items)
        {
            if (node is BinaryNode binary && binary.Operator == op)
            {
                Collect(binary.Left, op, items);
                Collect(binary.Right, op, items);
            }
            else
            {
                items.Add(ReadItem(node));
            }
        }

        private ReadingNode ReadNot(UnaryNode not) => not.Operand switch
        {
            BinaryNode { Operator: BinaryOperator.And } and => Group("notAll", and, BinaryOperator.And),
            BinaryNode { Operator: BinaryOperator.Or } or => Group("none", or, BinaryOperator.Or),
            NameNode or MemberNode when TypeOf(not.Operand)?.Kind == ExpressionTypeKind.Boolean =>
                One("is-false", ReadPart(not.Operand)),
            var operand => One("not", ReadItem(operand)),
        };

        private ReadingNode ReadNegate(UnaryNode negate) => negate.Operand switch
        {
            IntegerLiteral literal => new ReadingValue("integer", -literal.Value, null),
            DecimalLiteral literal => new ReadingValue("decimal", -literal.Value, null),
            var operand => One("negate", ReadPart(operand)),
        };

        private ReadingPhrase ReadBinary(BinaryNode binary)
        {
            var words = binary.Operator switch
            {
                BinaryOperator.Equal => "equal",
                BinaryOperator.NotEqual => "not-equal",
                BinaryOperator.Less => Ordering("less", binary),
                BinaryOperator.LessOrEqual => Ordering("less-or-equal", binary),
                BinaryOperator.Greater => Ordering("greater", binary),
                BinaryOperator.GreaterOrEqual => Ordering("greater-or-equal", binary),
                BinaryOperator.Add => "add",
                BinaryOperator.Subtract => "subtract",
                BinaryOperator.Multiply => "multiply",
                BinaryOperator.Divide => "divide",
                _ => throw new ArgumentOutOfRangeException(nameof(binary), binary.Operator, "Unknown operator."),
            };
            var compares = binary.Operator is BinaryOperator.Equal or BinaryOperator.NotEqual;
            return compares
                ? Two(words, ReadCompared(binary.Left, binary.Right), ReadCompared(binary.Right, binary.Left))
                : Two(words, ReadPart(binary.Left), ReadPart(binary.Right));
        }

        /// <summary>The words of an ordering. A date or a date-time on either side reads as before or after.</summary>
        private string Ordering(string words, BinaryNode binary) =>
            IsDate(binary.Left) || IsDate(binary.Right) ? $"{words}.date" : words;

        private bool IsDate(ExpressionNode node) => TypeOf(node)?.Kind is ExpressionTypeKind.Date or ExpressionTypeKind.DateTime;

        private ReadingPhrase ReadIn(InNode inNode) =>
            new("in", [[ReadPart(inNode.Operand)], [.. inNode.Items.Select(item => ReadCompared(item, inNode.Operand))]]);

        /// <summary>
        /// A part compared with <paramref name="other"/>. A text literal compared with an enum is a value of type
        /// <c>enum</c> that carries the label key of that enum value, since the type check types it as text.
        /// </summary>
        private ReadingNode ReadCompared(ExpressionNode node, ExpressionNode other)
        {
            if (node is TextLiteral literal && TypeOf(other)?.Kind == ExpressionTypeKind.Enum)
            {
                string? labelKey = null;
                if (check is not null
                    && check.Fields.TryGetValue(other, out var enumField)
                    && enumField.ValueLabelKeys is { } keys)
                {
                    keys.TryGetValue(literal.Value, out labelKey);
                }

                return new ReadingValue(EnumType, literal.Value, labelKey);
            }

            return ReadPart(node);
        }

        private ReadingName ReadName(NameNode name) =>
            check is not null && check.Fields.TryGetValue(name, out var field)
                ? new ReadingName(field.Name, field.LabelKey)
                : new ReadingName(name.Name, null);

        /// <summary>
        /// A path <c>a.b</c> as <c>b</c> <c>of</c> <c>a</c>, each step named by the field it resolves to. A read of
        /// <c>outer.field</c> is only the field.
        /// </summary>
        private ReadingNode ReadPath(MemberNode member)
        {
            var step = check is not null && check.Fields.TryGetValue(member, out var field)
                ? new ReadingName(field.Name, field.LabelKey)
                : new ReadingName(member.Name, null);
            return member.Target is NameNode root && string.Equals(root.Name, ExpressionTypeChecker.OuterName, StringComparison.OrdinalIgnoreCase)
                ? step
                : Two("of", step, ReadPart(member.Target));
        }

        private ReadingNode ReadCall(CallNode call)
        {
            if (call.Module is null && ExpressionFunctions.TryGet(call.Name, out var signature))
            {
                if (signature.Name is "date" or "dateTime" && call.Arguments is [TextLiteral literal])
                {
                    return new ReadingValue(signature.Name == "date" ? "date" : "date-time", literal.Value, null);
                }

                var words = signature.Name == "count" && call.Arguments.Count == 2 ? CountWhereWords : FunctionWords(signature.Name);
                return new ReadingPhrase(words, ReadArguments(signature, call.Arguments));
            }

            var name = check is not null && check.RuleCalls.TryGetValue(call, out var rule) ? rule.Name : call.Name;
            return new ReadingPhrase("rule", [[new ReadingName(name, null)], [.. call.Arguments.Select(ReadPart)]]);
        }

        /// <summary>
        /// The slots of a function's arguments: one slot for each argument, or one list slot that holds all of them for a
        /// function that takes any number of arguments, see docs/reference/authoring.md#expression-check.
        /// </summary>
        private IReadOnlyList<IReadOnlyList<ReadingNode>> ReadArguments(FunctionSignature signature, IReadOnlyList<ExpressionNode> arguments) =>
            signature.MaxArguments == int.MaxValue
                ? [[.. arguments.Select(ReadPart)]]
                : [.. arguments.Select(argument => (IReadOnlyList<ReadingNode>)[ReadPart(argument)])];

        private ExpressionType? TypeOf(ExpressionNode node) =>
            check is not null && check.Types.TryGetValue(node, out var type) ? type : null;

        private static ReadingPhrase One(string words, ReadingNode first) => new(words, [[first]]);

        private static ReadingPhrase Two(string words, ReadingNode first, ReadingNode second) => new(words, [[first], [second]]);
    }
}
