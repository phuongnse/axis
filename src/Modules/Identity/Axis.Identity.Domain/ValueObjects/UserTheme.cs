using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.ValueObjects;

public sealed class UserTheme : ValueObject
{
    public const string DefaultValue = "system";
    public const int MaxLength = 16;

    private static readonly HashSet<string> SupportedValues = new(StringComparer.Ordinal)
    {
        DefaultValue,
        "light",
        "dark",
    };

    public string Value { get; }

    private UserTheme(string value) => Value = value;

    public static bool IsSupported(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return SupportedValues.Contains(value.Trim());
    }

    public static Result<UserTheme> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<UserTheme>.Failure("Theme is required.");

        string normalized = value.Trim();
        if (!SupportedValues.Contains(normalized))
            return Result<UserTheme>.Failure("Theme is not supported.");

        return Result<UserTheme>.Success(new UserTheme(normalized));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
