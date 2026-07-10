using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public readonly partial record struct RuleContextKey(string Value)
{
    private const int MaxLength = 120;

    public static Result<RuleContextKey> Create(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            return Result.Failure<RuleContextKey>("Rule context key is required.");

        if (normalized.Length > MaxLength)
            return Result.Failure<RuleContextKey>("Rule context key is too long.");

        if (!ContextKeyPattern().IsMatch(normalized))
            return Result.Failure<RuleContextKey>("Rule context key format is invalid.");

        return new RuleContextKey(normalized);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ContextKeyPattern();
}
