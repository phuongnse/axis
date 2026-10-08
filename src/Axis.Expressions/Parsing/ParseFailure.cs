using Axis.Expressions.Diagnostics;

namespace Axis.Expressions.Parsing;

/// <summary>Stops parsing at the first problem. Only <see cref="ExpressionParser.Parse"/> catches it.</summary>
internal sealed class ParseFailure(ExpressionDiagnostic diagnostic) : Exception(diagnostic.Message)
{
    public ExpressionDiagnostic Diagnostic { get; } = diagnostic;
}
