using System.Text.Json;

namespace Axis.WorkflowEngine.Application.Services.Condition;

/// <summary>
/// Pure Application-layer expression evaluator for Condition steps.
/// Supports: ==, !=, &lt;, &gt;, &lt;=, &gt;=, contains, starts_with, ends_with, is_empty, is_not_empty.
/// Logical: AND, OR, NOT.
/// No external dependencies — safe for Application layer.
/// </summary>
public static class ConditionEvaluator
{
    /// <summary>
    /// Evaluates a branch expression dictionary against the execution context.
    /// Returns the label of the first matching branch, or null if no branch matches.
    /// </summary>
    /// <param name="branches">
    /// Ordered list of branch configs. Each branch has:
    ///   <c>label</c> (string), <c>expression</c> (object, null for default), <c>isDefault</c> (bool).
    /// </param>
    public static string? EvaluateBranches(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> branches,
        IReadOnlyDictionary<string, object?> context)
    {
        // Default branch is a fallback — evaluate all non-default branches first.
        string? defaultLabel = null;
        foreach (IReadOnlyDictionary<string, object?> branch in branches)
        {
            branch.TryGetValue("isDefault", out object? isDefaultRaw);
            bool isDefault = isDefaultRaw is JsonElement { ValueKind: JsonValueKind.True } || isDefaultRaw is true;
            if (isDefault)
            {
                defaultLabel = GetLabel(branch);
                continue;
            }

            if (!branch.TryGetValue("expression", out object? exprRaw) || exprRaw is null)
                continue;

            IReadOnlyDictionary<string, object?>? expr = ExtractDict(exprRaw);
            if (expr is null) continue;

            if (EvaluateExpression(expr, context))
                return GetLabel(branch);
        }

        return defaultLabel;
    }

    // ─── Expression tree evaluation ──────────────────────────────────────────

    private static bool EvaluateExpression(
        IReadOnlyDictionary<string, object?> expr,
        IReadOnlyDictionary<string, object?> context)
    {
        if (!expr.TryGetValue("type", out object? typeRaw)) return false;
        string type = typeRaw?.ToString() ?? string.Empty;

        return type switch
        {
            "AND" => EvaluateLogical(expr, context, true),
            "OR" => EvaluateLogical(expr, context, false),
            "NOT" => EvaluateNot(expr, context),
            _ => EvaluateComparison(type, expr, context)
        };
    }

    private static bool EvaluateLogical(
        IReadOnlyDictionary<string, object?> expr,
        IReadOnlyDictionary<string, object?> context,
        bool isAnd)
    {
        if (!expr.TryGetValue("conditions", out object? conditionsRaw)) return false;

        IReadOnlyList<IReadOnlyDictionary<string, object?>>? conditions = ExtractList(conditionsRaw);
        if (conditions is null) return false;

        return isAnd
            ? conditions.All(c => EvaluateExpression(c, context))
            : conditions.Any(c => EvaluateExpression(c, context));
    }

    private static bool EvaluateNot(
        IReadOnlyDictionary<string, object?> expr,
        IReadOnlyDictionary<string, object?> context)
    {
        if (!expr.TryGetValue("condition", out object? condRaw)) return false;
        IReadOnlyDictionary<string, object?>? condition = ExtractDict(condRaw);
        return condition is not null && !EvaluateExpression(condition, context);
    }

    private static bool EvaluateComparison(
        string @operator,
        IReadOnlyDictionary<string, object?> expr,
        IReadOnlyDictionary<string, object?> context)
    {
        if (!expr.TryGetValue("field", out object? fieldRaw)) return false;
        string field = fieldRaw?.ToString() ?? string.Empty;

        object? leftValue = ResolveField(field, context);
        expr.TryGetValue("value", out object? rightValue);

        return @operator switch
        {
            "==" or "eq" => ValuesEqual(leftValue, rightValue),
            "!=" or "neq" => !ValuesEqual(leftValue, rightValue),
            "<" or "lt" => TryCompareNumbers(leftValue, rightValue, out int lt) && lt < 0,
            ">" or "gt" => TryCompareNumbers(leftValue, rightValue, out int gt) && gt > 0,
            "<=" or "lte" => TryCompareNumbers(leftValue, rightValue, out int lte) && lte <= 0,
            ">=" or "gte" => TryCompareNumbers(leftValue, rightValue, out int gte) && gte >= 0,
            "contains" => StringContains(leftValue, rightValue),
            "starts_with" => StringStartsWith(leftValue, rightValue),
            "ends_with" => StringEndsWith(leftValue, rightValue),
            "is_empty" => IsEmpty(leftValue),
            "is_not_empty" => !IsEmpty(leftValue),
            _ => false
        };
    }

    // ─── Value resolution ────────────────────────────────────────────────────

    private static object? ResolveField(string field, IReadOnlyDictionary<string, object?> context)
    {
        // Support dotted path: "step_id.field_name"
        string[] parts = field.Split('.', 2);
        if (parts.Length == 2)
        {
            if (!context.TryGetValue(parts[0], out object? parent)) return null;
            IReadOnlyDictionary<string, object?>? nested = ExtractDict(parent);
            return nested is not null && nested.TryGetValue(parts[1], out object? val) ? val : null;
        }

        return context.TryGetValue(field, out object? value) ? value : null;
    }

    // ─── Comparison helpers ──────────────────────────────────────────────────

    private static bool ValuesEqual(object? left, object? right)
    {
        string? l = Coerce(left);
        string? r = Coerce(right);
        return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryCompareNumbers(object? left, object? right, out int result)
    {
        double l = CoerceDouble(left);
        double r = CoerceDouble(right);
        // NaN means the value is missing or non-numeric — comparisons are indeterminate, not false-positive.
        if (double.IsNaN(l) || double.IsNaN(r)) { result = 0; return false; }
        result = l.CompareTo(r);
        return true;
    }

    private static bool StringContains(object? left, object? right)
    {
        string l = Coerce(left) ?? string.Empty;
        string r = Coerce(right) ?? string.Empty;
        return l.Contains(r, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StringStartsWith(object? left, object? right)
    {
        string l = Coerce(left) ?? string.Empty;
        string r = Coerce(right) ?? string.Empty;
        return l.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StringEndsWith(object? left, object? right)
    {
        string l = Coerce(left) ?? string.Empty;
        string r = Coerce(right) ?? string.Empty;
        return l.EndsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEmpty(object? value)
        => value is null || Coerce(value) == string.Empty;

    // ─── Type coercion ───────────────────────────────────────────────────────

    private static string? Coerce(object? value)
    {
        if (value is null) return null;
        if (value is JsonElement je) return je.ToString();
        return value.ToString();
    }

    private static double CoerceDouble(object? value)
    {
        // Return NaN for null or non-numeric values so callers can distinguish "missing" from zero.
        if (value is null) return double.NaN;
        if (value is JsonElement je && je.TryGetDouble(out double d)) return d;
        return double.TryParse(Coerce(value), out double r) ? r : double.NaN;
    }

    // ─── JSON helpers ────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, object?>? ExtractDict(object? raw)
    {
        if (raw is IReadOnlyDictionary<string, object?> dict) return dict;
        if (raw is Dictionary<string, object?> mdict) return mdict;
        if (raw is JsonElement { ValueKind: JsonValueKind.Object } je)
            return je.Deserialize<Dictionary<string, object?>>() as IReadOnlyDictionary<string, object?>;
        return null;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>>? ExtractList(object? raw)
    {
        if (raw is IReadOnlyList<IReadOnlyDictionary<string, object?>> list) return list;
        if (raw is JsonElement { ValueKind: JsonValueKind.Array } je)
        {
            return je.Deserialize<List<Dictionary<string, object?>>>()
                ?.Cast<IReadOnlyDictionary<string, object?>>()
                .ToList();
        }
        return null;
    }

    private static string GetLabel(IReadOnlyDictionary<string, object?> branch)
        => branch.TryGetValue("label", out object? l) ? l?.ToString() ?? string.Empty : string.Empty;
}
