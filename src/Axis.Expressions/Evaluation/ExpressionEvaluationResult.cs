using System.Diagnostics.CodeAnalysis;

namespace Axis.Expressions.Evaluation;

/// <summary>
/// The outcome of evaluating one expression. When <see cref="Succeeded"/>, <see cref="Value"/> holds
/// the result, which may be <c>null</c>. Otherwise <see cref="Error"/> says why it stopped.
/// </summary>
public sealed record ExpressionEvaluationResult(object? Value, ExpressionRuntimeError? Error)
{
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Succeeded => Error is null;
}
