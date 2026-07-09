using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public readonly partial record struct RuleDefinitionKey(string Value)
{
    private const int MaxLength = 120;

    public static Result<RuleDefinitionKey> Create(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            return Result.Failure<RuleDefinitionKey>("Rule definition key is required.");

        if (normalized.Length > MaxLength)
            return Result.Failure<RuleDefinitionKey>("Rule definition key is too long.");

        if (!RuleDefinitionKeyPattern().IsMatch(normalized))
            return Result.Failure<RuleDefinitionKey>("Rule definition key format is invalid.");

        return new RuleDefinitionKey(normalized);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex RuleDefinitionKeyPattern();
}
