using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public sealed partial record RuleInputDefinition
{
    private RuleInputDefinition(
        string key,
        string label,
        IReadOnlyList<RuleValueType> types,
        bool isRequired,
        bool allowMultiple,
        IReadOnlyList<string> allowedValues)
    {
        Key = key;
        Label = label;
        Types = types;
        IsRequired = isRequired;
        AllowMultiple = allowMultiple;
        AllowedValues = allowedValues;
    }

    public string Key { get; }
    public string Label { get; }
    public IReadOnlyList<RuleValueType> Types { get; }
    public bool IsRequired { get; }
    public bool AllowMultiple { get; }
    public IReadOnlyList<string> AllowedValues { get; }

    public static Result<RuleInputDefinition> Create(
        string key,
        IReadOnlyList<RuleValueType> types,
        bool isRequired,
        bool allowMultiple = false,
        IReadOnlyList<string>? allowedValues = null)
        => CreateWithKey(key, key, types, isRequired, allowMultiple, allowedValues);

    public static Result<RuleInputDefinition> CreateFromLabel(
        string label,
        IReadOnlyList<RuleValueType> types,
        bool isRequired,
        bool allowMultiple = false,
        IReadOnlyList<string>? allowedValues = null)
    {
        Result<string> key = DeriveKey(label);
        return key.IsSuccess
            ? CreateWithKey(key.Value, label, types, isRequired, allowMultiple, allowedValues)
            : Result.Failure<RuleInputDefinition>(key.Error);
    }

    public static Result<RuleInputDefinition> CreateFromLabel(
        string label,
        RuleValueType type,
        bool isRequired,
        bool allowMultiple = false,
        IReadOnlyList<string>? allowedValues = null) =>
        CreateFromLabel(label, [type], isRequired, allowMultiple, allowedValues);

    public static Result<RuleInputDefinition> CreateSystem(
        string key,
        string label,
        IReadOnlyList<RuleValueType> types,
        bool isRequired,
        bool allowMultiple = false,
        IReadOnlyList<string>? allowedValues = null) =>
        CreateWithKey(key, label, types, isRequired, allowMultiple, allowedValues);

    public static Result<RuleInputDefinition> Restore(
        string key,
        string label,
        IReadOnlyList<RuleValueType> types,
        bool isRequired,
        bool allowMultiple = false,
        IReadOnlyList<string>? allowedValues = null) =>
        CreateWithKey(key, label, types, isRequired, allowMultiple, allowedValues);

    private static Result<RuleInputDefinition> CreateWithKey(
        string key,
        string label,
        IReadOnlyList<RuleValueType> types,
        bool isRequired,
        bool allowMultiple,
        IReadOnlyList<string>? allowedValues)
    {
        string normalizedKey = key?.Trim() ?? string.Empty;
        if (!InputKeyPattern().IsMatch(normalizedKey))
            return Result.Failure<RuleInputDefinition>("Rule input key format is invalid.");

        string normalizedLabel = label?.Trim() ?? string.Empty;
        if (normalizedLabel.Length is 0 or > 120)
            return Result.Failure<RuleInputDefinition>("Rule input label is required and cannot exceed 120 characters.");

        if (types is null || types.Count == 0 || types.Any(type => !Enum.IsDefined(type)))
            return Result.Failure<RuleInputDefinition>("Rule input types are not supported.");

        RuleValueType[] normalizedTypes = types.Distinct().Order().ToArray();
        List<string> normalizedAllowedValues = [];
        if (allowedValues is { Count: > 0 })
        {
            if (normalizedTypes.Length != 1)
                return Result.Failure<RuleInputDefinition>(
                    "Rule input allowed values require exactly one accepted type.");
            if (allowedValues.Count > RuleValue.MaximumValueCount)
                return Result.Failure<RuleInputDefinition>("Rule input contains too many allowed values.");
        }

        foreach (string allowedValue in allowedValues ?? [])
        {
            Result<RuleValue> normalized = RuleValue.Create(normalizedTypes[0], [allowedValue]);
            if (normalized.IsFailure)
                return Result.Failure<RuleInputDefinition>(normalized.Error);

            normalizedAllowedValues.Add(normalized.Value.Values[0]);
        }

        if (normalizedAllowedValues.Count != normalizedAllowedValues.Distinct(StringComparer.Ordinal).Count())
            return Result.Failure<RuleInputDefinition>("Rule input allowed values must be unique.");

        return new RuleInputDefinition(
            normalizedKey,
            normalizedLabel,
            Array.AsReadOnly(normalizedTypes),
            isRequired,
            allowMultiple,
            Array.AsReadOnly(normalizedAllowedValues.ToArray()));
    }

    public static Result<RuleInputDefinition> Create(
        string key,
        RuleValueType type,
        bool isRequired,
        bool allowMultiple = false,
        IReadOnlyList<string>? allowedValues = null) =>
        Create(key, [type], isRequired, allowMultiple, allowedValues);

    public static Result<string> DeriveKey(string label)
    {
        string normalizedLabel = label?.Trim() ?? string.Empty;
        if (normalizedLabel.Length is 0 or > 120)
            return Result.Failure<string>("Rule input label is required and cannot exceed 120 characters.");

        StringBuilder slug = new();
        bool pendingSeparator = false;
        foreach (char character in normalizedLabel.Normalize(NormalizationForm.FormD))
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            char normalized = char.ToLowerInvariant(character) == 'đ'
                ? 'd'
                : char.ToLowerInvariant(character);
            if (normalized is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingSeparator && slug.Length > 0)
                    slug.Append('_');
                slug.Append(normalized);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = slug.Length > 0;
            }
        }

        string stem = slug.Length == 0 ? "input" : slug.ToString();
        if (stem[0] is >= '0' and <= '9')
            stem = $"input_{stem}";
        const int maximumStemLength = 70;
        if (stem.Length > maximumStemLength)
            stem = stem[..maximumStemLength].TrimEnd('_');

        string hash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(normalizedLabel.Normalize(NormalizationForm.FormKC).ToLowerInvariant())))
            [..8]
            .ToLowerInvariant();
        return $"{stem}_{hash}";
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex InputKeyPattern();
}
