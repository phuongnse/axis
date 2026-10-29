using Axis.Configuration.Resources;

namespace Axis.Presentation.Sites;

/// <summary>
/// The built-in platform site. It is the only site until applications can define their own.
/// </summary>
public sealed class PlatformSiteMetadataProvider : ISiteMetadataProvider
{
    private static readonly SiteMetadata _site = new(
        Name: "platform",
        TitleKey: "shell.title",
        Locales: new SiteLocales(Default: PlatformLocales.Fallback, Fallback: PlatformLocales.Fallback, Available: [.. PlatformLocales.All]),
        Navigation: [new NavigationItem(Key: "home", Path: "/", LabelKey: "shell.nav.home", Icon: "home")]);

    public SiteMetadata GetSite() => _site;
}
