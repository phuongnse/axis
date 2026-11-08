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
        new("length", 1, 1, ["text"], "integer", "text", "length(title)") { Sql = true },
        new("contains", 2, 2, ["text", "text"], "boolean", "text", "contains(title, 'PR-')") { Sql = true },
        new("startsWith", 2, 2, ["text", "text"], "boolean", "text", "startsWith(title, 'PR-')") { Sql = true },
        new("endsWith", 2, 2, ["text", "text"], "boolean", "text", "endsWith(title, '.pdf')") { Sql = true },
        new("concat", 1, int.MaxValue, ["text", "…"], "text", "text", "concat(firstName, ' ', lastName)") { Sql = true },
        new("lower", 1, 1, ["text"], "text", "text", "lower(title)"),
        new("upper", 1, 1, ["text"], "text", "text", "upper(title)"),
        new("trim", 1, 1, ["text"], "text", "text", "trim(title)"),
        new("abs", 1, 1, ["n"], "n", "number", "abs(balance)") { Sql = true },
        new("round", 2, 2, ["decimal", "integer"], "decimal", "number", "round(amount, 2)") { Sql = true },
        new("floor", 1, 1, ["n"], "n", "number", "floor(amount)") { Sql = true },
        new("ceiling", 1, 1, ["n"], "n", "number", "ceiling(amount)") { Sql = true },
        new("year", 1, 1, ["date"], "integer", "date", "year(orderDate)") { Sql = true },
        new("month", 1, 1, ["date"], "integer", "date", "month(orderDate)") { Sql = true },
        new("day", 1, 1, ["date"], "integer", "date", "day(orderDate)") { Sql = true },
        new("addDays", 2, 2, ["date", "integer"], "date", "date", "addDays(orderDate, 30)") { Sql = true },
        new("daysBetween", 2, 2, ["date", "date"], "integer", "date", "daysBetween(orderDate, dueDate)") { Sql = true },
        new("coalesce", 2, int.MaxValue, ["a", "b", "…"], "a", "logic", "coalesce(nickname, name)") { Sql = true },
        new("if", 3, 3, ["condition", "then", "else"], "then", "logic", "if(urgent, 1, 3)") { Sql = true },
        new("date", 1, 1, ["text literal"], "date", "date", "date('2026-10-08')") { Sql = true },
        new("dateTime", 1, 1, ["text literal"], "date-time", "date", "dateTime('2026-10-08T09:30:00Z')") { Sql = true },
        new("now", 0, 0, [], "date-time", "date", "now()") { Sql = true },
        new("today", 0, 0, [], "date", "date", "today()") { Sql = true },
        new("toDate", 1, 1, ["date-time"], "date", "date", "toDate(createdAt)") { Sql = true },
        new("currentUser", 0, 0, [], "text", "context", "currentUser()") { Sql = true },
        new("previous", 1, 1, ["field"], "field", "context", "previous(status)"),
        new("accountableUser", 0, 0, [], "text", "context", "accountableUser()"),
        new("decisionTask", 0, 0, [], "text", "context", "decisionTask()"),
    ];

    private static readonly FunctionSignature[] _aggregates =
    [
        new("count", 1, 2, ["list", "condition"], "integer", "aggregate", "count(lines, quantity > 10)") { IsAggregate = true, Sql = true },
        new("sum", 2, 2, ["list", "item"], "item", "aggregate", "sum(lines, quantity * unitPrice)") { IsAggregate = true },
        new("avg", 2, 2, ["list", "item"], "decimal", "aggregate", "avg(lines, quantity)") { IsAggregate = true },
        new("min", 2, 2, ["list", "item"], "item", "aggregate", "min(lines, unitPrice)") { IsAggregate = true },
        new("max", 2, 2, ["list", "item"], "item", "aggregate", "max(lines, unitPrice)") { IsAggregate = true },
        new("any", 2, 2, ["list", "condition"], "boolean", "aggregate", "any(lines, quantity > 10)") { IsAggregate = true, Sql = true },
        new("all", 2, 2, ["list", "condition"], "boolean", "aggregate", "all(lines, quantity > 0)") { IsAggregate = true },
        new("first", 3, 3, ["list", "condition", "item"], "item", "aggregate", "first(lines, quantity > 10, description)") { IsAggregate = true },
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

    /// <summary>Finds a function or an aggregate by name, ignoring letter case. Throws for a name that is none.</summary>
    public static FunctionSignature Get(string name) => _byName[name];

    /// <summary>Finds a function by name, ignoring letter case.</summary>
    internal static bool TryGet(string name, out FunctionSignature signature) =>
        _byName.TryGetValue(name, out signature!);
}

/// <summary>
/// A function's canonical name and how many arguments it takes. <see cref="MaxArguments"/> is
/// <see cref="int.MaxValue"/> for a function that takes any number from <see cref="MinArguments"/>.
/// <see cref="Parameters"/> are written as the reference's signatures write them, and <see cref="Result"/> is a type
/// name, or the name of the parameter whose type the result takes.
/// </summary>
public sealed record FunctionSignature(
    string Name,
    int MinArguments,
    int MaxArguments,
    IReadOnlyList<string> Parameters,
    string Result,
    string Group,
    string Example)
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
