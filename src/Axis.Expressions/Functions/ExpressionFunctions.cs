namespace Axis.Expressions.Functions;

/// <summary>
/// The scalar functions an expression can call, as listed in the function table of
/// docs/reference/expressions.md. Names match ignoring letter case.
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

    private static readonly Dictionary<string, FunctionSignature> _byName =
        _signatures.ToDictionary(signature => signature.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The function names, spelled as the reference spells them.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. _signatures.Select(signature => signature.Name)];

    /// <summary>Finds a function by name, ignoring letter case.</summary>
    internal static bool TryGet(string name, out FunctionSignature signature) =>
        _byName.TryGetValue(name, out signature!);
}

/// <summary>
/// A function's canonical name and how many arguments it takes. <see cref="MaxArguments"/> is
/// <see cref="int.MaxValue"/> for a function that takes any number from <see cref="MinArguments"/>.
/// </summary>
internal sealed record FunctionSignature(string Name, int MinArguments, int MaxArguments);
