using Axis.Expressions.Typing;

namespace Axis.Expressions.Sql;

/// <summary>
/// One exact lookup test a translated expression makes. The test compares the lookup column of
/// the field at <see cref="Path"/> with a <c>bytea[]</c> parameter that holds the hashes of
/// <see cref="Value"/>, so the caller must hash the value and bind that parameter.
/// <see cref="Name"/> is the parameter name without its <c>@</c>. <see cref="Value"/> has the CLR
/// type of its kind, as in <see cref="SqlValue"/>.
/// </summary>
public sealed record SqlLookup(string Name, IReadOnlyList<string> Path, ExpressionTypeKind Kind, object Value);
