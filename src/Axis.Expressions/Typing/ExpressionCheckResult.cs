using System.Diagnostics.CodeAnalysis;
using Axis.Expressions.Diagnostics;

namespace Axis.Expressions.Typing;

/// <summary>The outcome of type checking one expression. Exactly one of the two values is set.</summary>
public sealed record ExpressionCheckResult(ExpressionType? Type, ExpressionDiagnostic? Diagnostic)
{
    [MemberNotNullWhen(true, nameof(Type))]
    [MemberNotNullWhen(false, nameof(Diagnostic))]
    public bool Succeeded => Diagnostic is null;
}
