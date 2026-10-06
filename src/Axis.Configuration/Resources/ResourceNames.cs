namespace Axis.Configuration.Resources;

/// <summary>The rule for resource names, such as the application manifest name.</summary>
public static class ResourceNames
{
    public const int MaxLength = 60;

    /// <summary>
    /// Whether <paramref name="name"/> follows the same rule as the <c>name</c> pattern in the JSON
    /// Schemas: an ASCII letter, then ASCII letters or digits, at most <see cref="MaxLength"/> characters.
    /// </summary>
    public static bool IsValid(string? name) =>
        name is { Length: > 0 and <= MaxLength }
        && char.IsAsciiLetter(name[0])
        && name.All(char.IsAsciiLetterOrDigit);
}
