namespace Axis.Expressions.Functions;

/// <summary>
/// The functions an expression can call: the scalar functions in the function table of
/// docs/reference/expressions.md, and the aggregates over a child collection in its aggregates
/// table. Names match ignoring letter case.
/// </summary>
public static class ExpressionFunctions
{
    private static readonly FunctionSignature[] _signatures =
    [
        new("length", 1, 1) { Sql = true },
        new("contains", 2, 2) { Sql = true },
        new("startsWith", 2, 2) { Sql = true },
        new("endsWith", 2, 2) { Sql = true },
        new("concat", 1, int.MaxValue) { Sql = true },
        new("lower", 1, 1),
        new("upper", 1, 1),
        new("trim", 1, 1),
        new("abs", 1, 1) { Sql = true },
        new("round", 2, 2) { Sql = true },
        new("floor", 1, 1) { Sql = true },
        new("ceiling", 1, 1) { Sql = true },
        new("year", 1, 1) { Sql = true },
        new("month", 1, 1) { Sql = true },
        new("day", 1, 1) { Sql = true },
        new("addDays", 2, 2) { Sql = true },
        new("daysBetween", 2, 2) { Sql = true },
        new("coalesce", 2, int.MaxValue) { Sql = true },
        new("if", 3, 3) { Sql = true },
        new("date", 1, 1) { Sql = true },
        new("dateTime", 1, 1) { Sql = true },
        new("now", 0, 0) { Sql = true },
        new("today", 0, 0) { Sql = true },
        new("toDate", 1, 1) { Sql = true },
        new("currentUser", 0, 0) { Sql = true },
        new("previous", 1, 1),
        new("accountableUser", 0, 0),
        new("decisionTask", 0, 0),
    ];

    private static readonly FunctionSignature[] _aggregates =
    [
        new("count", 1, 2) { IsAggregate = true, Sql = true },
        new("sum", 2, 2) { IsAggregate = true },
        new("avg", 2, 2) { IsAggregate = true },
        new("min", 2, 2) { IsAggregate = true },
        new("max", 2, 2) { IsAggregate = true },
        new("any", 2, 2) { IsAggregate = true, Sql = true },
        new("all", 2, 2) { IsAggregate = true },
        new("first", 3, 3) { IsAggregate = true },
    ];

    private static readonly Dictionary<string, FunctionSignature> _byName =
        _signatures.Concat(_aggregates).ToDictionary(signature => signature.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The scalar function names, spelled as the reference spells them.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. _signatures.Select(signature => signature.Name)];

    /// <summary>The aggregate names, spelled as the reference spells them.</summary>
    public static IReadOnlyList<string> AggregateNames { get; } = [.. _aggregates.Select(signature => signature.Name)];

    /// <summary>Whether <paramref name="name"/> is a scalar function or an aggregate, ignoring letter case.</summary>
    public static bool IsFunction(string name) => _byName.ContainsKey(name);

    /// <summary>
    /// Whether <paramref name="name"/> is in the SQL subset, see docs/reference/expressions.md#sql-subset.
    /// A name that is not a function is not.
    /// </summary>
    public static bool IsInSqlSubset(string name) => _byName.TryGetValue(name, out var signature) && signature.Sql;

    /// <summary>Finds a function by name, ignoring letter case.</summary>
    internal static bool TryGet(string name, out FunctionSignature signature) =>
        _byName.TryGetValue(name, out signature!);
}

/// <summary>
/// A function's canonical name and how many arguments it takes. <see cref="MaxArguments"/> is
/// <see cref="int.MaxValue"/> for a function that takes any number from <see cref="MinArguments"/>.
/// </summary>
internal sealed record FunctionSignature(string Name, int MinArguments, int MaxArguments)
{
    /// <summary>
    /// Whether the function aggregates over a child collection. Its first argument names the
    /// collection, and the arguments after it, when there are any, are evaluated once per row.
    /// </summary>
    public bool IsAggregate { get; init; }

    /// <summary>
    /// Whether the function is in the SQL subset, see docs/reference/expressions.md#sql-subset.
    /// A place whose expression runs as SQL offers only these, and the SQL translator refuses the rest.
    /// </summary>
    public bool Sql { get; init; }
}
