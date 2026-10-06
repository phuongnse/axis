namespace Axis.Configuration.Resources;

/// <summary>The site paths the platform keeps for itself.</summary>
public static class SitePaths
{
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal) { "api", "health", "assets" };
}
