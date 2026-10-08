namespace Axis.Expressions.Evaluation;

/// <summary>
/// An error that stopped an evaluation. <see cref="Offset"/> is the zero-based UTF-16 index of the
/// node that failed; the message shows it one-based. Messages never contain exception text.
/// </summary>
public sealed record ExpressionRuntimeError(ExpressionRuntimeErrorKind Kind, string Message, int Offset);
