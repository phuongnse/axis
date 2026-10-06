namespace Axis.Presentation.Sites;

/// <summary>What the SPA needs to build its shell: name, title, locales and navigation.</summary>
/// <param name="TitleKey">Text key of the site title.</param>
public sealed record SiteMetadata(string Name, string TitleKey, SiteLocales Locales, IReadOnlyList<NavigationItem> Navigation);

/// <summary>The locales a site offers, with the one shown first and the one used for missing texts.</summary>
public sealed record SiteLocales(string Default, string Fallback, IReadOnlyList<string> Available);

/// <summary>One navigation entry. The label is a text key, never literal text.</summary>
public sealed record NavigationItem(string Key, string Path, string LabelKey);
