using System.Diagnostics.CodeAnalysis;

namespace Axis.Expressions.Sql;

/// <summary>
/// The SQL of a child collection or references field that <c>any</c> or <c>count</c> reads: the
/// <c>FROM</c> clause of its subquery, the condition that ties its rows to the outer row, and the
/// column of a name or path of the item expression.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "A child collection is the concept's name.")]
public sealed record SqlCollection(string From, string OwnerMatch, Func<IReadOnlyList<string>, string> Column);
