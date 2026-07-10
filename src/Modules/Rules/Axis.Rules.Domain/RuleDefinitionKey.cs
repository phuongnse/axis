using System.Globalization;
using System.Text;
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

    public static Result<RuleDefinitionKey> CreateWorkspaceFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<RuleDefinitionKey>("Rule definition name is required.");

        StringBuilder builder = new();
        foreach (char rawCharacter in name.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(rawCharacter) == UnicodeCategory.NonSpacingMark)
                continue;

            char character = char.ToLowerInvariant(rawCharacter);
            if (character == '\u0111')
                character = 'd';

            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '_')
                builder.Append('_');
        }

        string key = builder.ToString().Trim('_');
        if (key.Length == 0)
            key = "rule";

        if (key[0] is < 'a' or > 'z')
            key = $"rule_{key}";

        const int workspaceKeyMaxLength = 63;
        if (key.Length > workspaceKeyMaxLength)
            key = key[..workspaceKeyMaxLength].TrimEnd('_');

        return Create(key);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex RuleDefinitionKeyPattern();
}
