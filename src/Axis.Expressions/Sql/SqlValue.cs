using Axis.Expressions.Typing;

namespace Axis.Expressions.Sql;

/// <summary>
/// One value a translated expression sends as a named SQL parameter. <see cref="Name"/> is the
/// parameter name without its <c>@</c>. <see cref="Value"/> has the CLR type of its kind:
/// <see cref="string"/> for text, <see cref="long"/> for integer, <see cref="decimal"/>,
/// <see cref="bool"/>, <see cref="DateOnly"/> for date and <see cref="DateTimeOffset"/> in UTC for date-time.
/// </summary>
public sealed record SqlValue(string Name, ExpressionTypeKind Kind, object Value);
