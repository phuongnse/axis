using System.Diagnostics.CodeAnalysis;
using Axis.Expressions.Diagnostics;

namespace Axis.Expressions.Sql;

/// <summary>
/// The outcome of translating one expression to SQL. On success <see cref="Sql"/> holds the
/// condition and <see cref="Parameters"/> the values it names, in order. On failure only
/// <see cref="Diagnostic"/> is set and <see cref="Parameters"/> is empty. <see cref="Lookups"/>
/// are the exact lookup tests that the condition names as <c>bytea[]</c> parameters.
/// </summary>
public sealed record SqlTranslationResult(string? Sql, IReadOnlyList<SqlValue> Parameters, ExpressionDiagnostic? Diagnostic)
{
    /// <summary>The exact lookup tests of the condition, in order. Empty without a lookup callback.</summary>
    public IReadOnlyList<SqlLookup> Lookups { get; init; } = [];

    [MemberNotNullWhen(true, nameof(Sql))]
    [MemberNotNullWhen(false, nameof(Diagnostic))]
    public bool Succeeded => Diagnostic is null;
}
