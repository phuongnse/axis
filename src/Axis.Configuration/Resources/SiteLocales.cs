namespace Axis.Configuration.Resources;

/// <summary>The locales a site offers: the one it starts in, the one it falls back to, and every one it offers.</summary>
public sealed record SiteLocales(string Default, string Fallback, IReadOnlyList<string> Available);
