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
        new("length", 1, 1),
        new("contains", 2, 2),
        new("startsWith", 2, 2),
        new("endsWith", 2, 2),
        new("concat", 1, int.MaxValue),
        new("lower", 1, 1),
        new("upper", 1, 1),
        new("trim", 1, 1),
        new("abs", 1, 1),
        new("round", 2, 2),
        new("floor", 1, 1),
        new("ceiling", 1, 1),
        new("year", 1, 1),
        new("month", 1, 1),
        new("day", 1, 1),
        new("addDays", 2, 2),
        new("daysBetween", 2, 2),
        new("coalesce", 2, int.MaxValue),
        new("if", 3, 3),
        new("date", 1, 1),
        new("dateTime", 1, 1),
    ];

    private static readonly FunctionSignature[] _aggregates =
    [
        new("count", 1, 2) { IsAggregate = true },
        new("sum", 2, 2) { IsAggregate = true },
        new("min", 2, 2) { IsAggregate = true },
        new("max", 2, 2) { IsAggregate = true },
        new("any", 2, 2) { IsAggregate = true },
        new("all", 2, 2) { IsAggregate = true },
    ];

    private static readonly Dictionary<string, FunctionSignature> _byName =
        _signatures.Concat(_aggregates).ToDictionary(signature => signature.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The scalar function names, spelled as the reference spells them.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. _signatures.Select(signature => signature.Name)];

    /// <summary>The aggregate names, spelled as the reference spells them.</summary>
    public static IReadOnlyList<string> AggregateNames { get; } = [.. _aggregates.Select(signature => signature.Name)];

    /// <summary>Whether <paramref name="name"/> is a scalar function or an aggregate, ignoring letter case.</summary>
    public static bool IsFunction(string name) => _byName.ContainsKey(name);

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
    /// collection, and its second, when there is one, is evaluated once per row.
    /// </summary>
    public bool IsAggregate { get; init; }
}
