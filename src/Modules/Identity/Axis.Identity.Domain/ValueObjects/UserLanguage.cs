using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.ValueObjects;

public sealed class UserLanguage : ValueObject
{
    public const string DefaultValue = "en";
    public const int MaxLength = 8;

    private static readonly HashSet<string> SupportedValues = new(StringComparer.Ordinal)
    {
        DefaultValue,
        "vi",
    };

    public string Value { get; }

    private UserLanguage(string value) => Value = value;

    public static bool IsSupported(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return SupportedValues.Contains(value.Trim());
    }

    public static Result<UserLanguage> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<UserLanguage>.Failure("Language is required.");

        string normalized = value.Trim();
        if (!SupportedValues.Contains(normalized))
            return Result<UserLanguage>.Failure("Language is not supported.");

        return Result<UserLanguage>.Success(new UserLanguage(normalized));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
