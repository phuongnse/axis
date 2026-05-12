using Axis.Shared.Domain.Primitives;
using System.Text.RegularExpressions;

namespace Axis.Identity.Domain.ValueObjects;

public sealed class Email : ValueObject
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Value { get; }

    private Email(string value) => Value = value;

    public static Result<Email> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<Email>.Failure("Email is required.");

        string normalized = value.Trim().ToLowerInvariant();

        if (!EmailRegex.IsMatch(normalized))
            return Result<Email>.Failure($"'{value}' is not a valid email address.");

        return Result<Email>.Success(new Email(normalized));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
