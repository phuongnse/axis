namespace Axis.Expressions.Syntax;

/// <summary>
/// A node of an expression's syntax tree. <see cref="Offset"/> is the zero-based UTF-16 index of
/// the token that defines the node: the literal or name, the call name, or the operator.
/// </summary>
public abstract record ExpressionNode(int Offset);

public sealed record IntegerLiteral(int Offset, long Value) : ExpressionNode(Offset);

public sealed record DecimalLiteral(int Offset, decimal Value) : ExpressionNode(Offset);

/// <summary>A text literal. <see cref="Value"/> holds one quote for each <c>''</c> in the source.</summary>
public sealed record TextLiteral(int Offset, string Value) : ExpressionNode(Offset);

public sealed record BooleanLiteral(int Offset, bool Value) : ExpressionNode(Offset);

public sealed record NullLiteral(int Offset) : ExpressionNode(Offset);

/// <summary>A bare name, kept as written. Matching names ignoring letter case is left to the type checker.</summary>
public sealed record NameNode(int Offset, string Name) : ExpressionNode(Offset);

/// <summary>One <c>.name</c> step of a path. <see cref="ExpressionNode.Offset"/> is the <c>.</c>.</summary>
public sealed record MemberNode(int Offset, ExpressionNode Target, string Name) : ExpressionNode(Offset);

/// <summary>A function or rule call, including <c>date('…')</c> and <c>dateTime('…')</c>.</summary>
public sealed record CallNode(int Offset, string Name, IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode(Offset);

public sealed record UnaryNode(int Offset, UnaryOperator Operator, ExpressionNode Operand) : ExpressionNode(Offset);

public sealed record BinaryNode(int Offset, BinaryOperator Operator, ExpressionNode Left, ExpressionNode Right) : ExpressionNode(Offset);

/// <summary><c>is null</c>, or <c>is not null</c> when <see cref="Negated"/>. <see cref="ExpressionNode.Offset"/> is the <c>is</c>.</summary>
public sealed record IsNullNode(int Offset, ExpressionNode Operand, bool Negated) : ExpressionNode(Offset);

/// <summary><c>x in (…)</c>. Each item is a literal, or a <c>date</c> or <c>dateTime</c> call on a text literal.</summary>
public sealed record InNode(int Offset, ExpressionNode Operand, IReadOnlyList<ExpressionNode> Items) : ExpressionNode(Offset);
