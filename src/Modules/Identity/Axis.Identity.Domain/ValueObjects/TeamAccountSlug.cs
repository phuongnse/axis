using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.ValueObjects;

/// <summary>
/// URL-safe identifier for a team account. Used as the PostgreSQL schema suffix.
/// Rules: lowercase letters, digits, and hyphens only; cannot start or end with a hyphen; max 63 chars.
/// </summary>
public sealed class TeamAccountSlug : ValueObject
{
    private const int MaxLength = 63;

    private static readonly Regex SlugRegex = new(
        @"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$",
        RegexOptions.Compiled);

    public string Value { get; }

    private TeamAccountSlug(string value) => Value = value;

    public static Result<TeamAccountSlug> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<TeamAccountSlug>.Failure("TeamAccount slug is required.");

        if (value.Length > MaxLength)
            return Result<TeamAccountSlug>.Failure(
                $"TeamAccount slug must be {MaxLength} characters or fewer.");

        if (!SlugRegex.IsMatch(value))
            return Result<TeamAccountSlug>.Failure(
                "TeamAccount slug may only contain lowercase letters, digits, and hyphens, " +
                "and cannot start or end with a hyphen.");

        return Result<TeamAccountSlug>.Success(new TeamAccountSlug(value));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
