namespace Axis.Configuration.Resources;

/// <summary>The locales that the platform itself offers, and the one it falls back to.</summary>
public static class PlatformLocales
{
    public const string Fallback = "en";

    public static readonly IReadOnlyList<string> All = ["en", "vi"];
}
