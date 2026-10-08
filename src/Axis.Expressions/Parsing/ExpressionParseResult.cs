using System.Diagnostics.CodeAnalysis;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Syntax;

namespace Axis.Expressions.Parsing;

/// <summary>The outcome of parsing one expression. Exactly one of the two values is set.</summary>
public sealed record ExpressionParseResult(ExpressionNode? Expression, ExpressionDiagnostic? Diagnostic)
{
    [MemberNotNullWhen(true, nameof(Expression))]
    [MemberNotNullWhen(false, nameof(Diagnostic))]
    public bool Succeeded => Diagnostic is null;
}
