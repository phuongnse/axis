using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.BusinessObjects.Domain.ValueObjects;

public readonly partial record struct BusinessObjectChoiceOptionKey(string Value)
{
    public static Result<BusinessObjectChoiceOptionKey> Create(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (!OptionKeyPattern().IsMatch(normalized) || normalized.Length > 63)
        {
            return Result.Failure<BusinessObjectChoiceOptionKey>(
                "Choice option key must be 1-63 characters, start with a lowercase letter, " +
                "and contain only lowercase letters, digits, and underscores.");
        }

        return new BusinessObjectChoiceOptionKey(normalized);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex OptionKeyPattern();
}
