using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed partial record RuleInputMapping
{
    private RuleInputMapping(
        RuleInputMappingKind kind,
        string? contextKey,
        IReadOnlyList<string> literalValues)
    {
        Kind = kind;
        ContextKey = contextKey;
        LiteralValues = literalValues.ToArray();
    }

    public RuleInputMappingKind Kind { get; }
    public string? ContextKey { get; }
    public IReadOnlyList<string> LiteralValues { get; }

    public static Result<RuleInputMapping> FromContext(string contextKey)
    {
        string normalized = contextKey?.Trim() ?? string.Empty;
        if (normalized.Length > 120)
            return Result.Failure<RuleInputMapping>("Rule context mapping key is too long.");

        return ContextKeyPattern().IsMatch(normalized)
            ? new RuleInputMapping(RuleInputMappingKind.Context, normalized, [])
            : Result.Failure<RuleInputMapping>("Rule context mapping key is invalid.");
    }

    public static Result<RuleInputMapping> FromLiteral(IReadOnlyList<string> values)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
            return Result.Failure<RuleInputMapping>("Rule literal mapping values are required.");
        if (values.Count > RuleValue.MaximumValueCount)
            return Result.Failure<RuleInputMapping>("Rule literal mapping contains too many values.");
        if (values.Any(value => value.Length > RuleValue.MaximumTextLength))
            return Result.Failure<RuleInputMapping>("Rule literal mapping value is too long.");

        string[] normalized = values.Select(value => value.Trim()).ToArray();
        return new RuleInputMapping(RuleInputMappingKind.Literal, null, normalized);
    }

    [GeneratedRegex("^[a-z][a-z0-9_.-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ContextKeyPattern();
}
