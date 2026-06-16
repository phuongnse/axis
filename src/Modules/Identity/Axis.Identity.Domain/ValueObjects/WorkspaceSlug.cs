using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.ValueObjects;

/// <summary>
/// URL-safe identifier for a workspace. Used as the PostgreSQL schema suffix.
/// Rules: lowercase letters, digits, and hyphens only; cannot start or end with a hyphen; max 63 chars.
/// </summary>
public sealed class WorkspaceSlug : ValueObject
{
    private const int MaxLength = 63;

    private static readonly Regex SlugRegex = new(
        @"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$",
        RegexOptions.Compiled);

    public string Value { get; }

    private WorkspaceSlug(string value) => Value = value;

    public static Result<WorkspaceSlug> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result<WorkspaceSlug>.Failure("workspace slug is required.");

        if (value.Length > MaxLength)
            return Result<WorkspaceSlug>.Failure(
                $"workspace slug must be {MaxLength} characters or fewer.");

        if (!SlugRegex.IsMatch(value))
            return Result<WorkspaceSlug>.Failure(
                "workspace slug may only contain lowercase letters, digits, and hyphens, " +
                "and cannot start or end with a hyphen.");

        return Result<WorkspaceSlug>.Success(new WorkspaceSlug(value));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
