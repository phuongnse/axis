namespace Axis.Expressions.Diagnostics;

/// <summary>
/// A problem in one expression. <see cref="Offset"/> is the zero-based UTF-16 index into the
/// expression text; the message shows it one-based. The caller that read the expression from a
/// resource file adds the file and the JSON Pointer of the expression string. Messages never
/// contain exception text, so they can be shown to application authors.
/// </summary>
public sealed record ExpressionDiagnostic(string Code, string Message, int Offset);
