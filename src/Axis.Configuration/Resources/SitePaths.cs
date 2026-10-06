namespace Axis.Configuration.Resources;

/// <summary>The rule for site paths, and the paths the platform keeps for itself.</summary>
public static class SitePaths
{
    public const int MaxLength = 60;

    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal) { "api", "health", "assets" };

    /// <summary>
    /// Whether <paramref name="path"/> follows the <c>path</c> pattern of the site JSON Schema,
    /// ignoring letter case: an ASCII letter, then ASCII letters, digits or hyphens, at most
    /// <see cref="MaxLength"/> characters. It does not check <see cref="Reserved"/>.
    /// </summary>
    public static bool IsValid(string? path) =>
        path is { Length: > 0 and <= MaxLength }
        && char.IsAsciiLetter(path[0])
        && path.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
}
