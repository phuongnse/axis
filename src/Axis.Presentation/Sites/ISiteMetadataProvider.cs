namespace Axis.Presentation.Sites;

/// <summary>Provides the metadata of the site the SPA renders.</summary>
public interface ISiteMetadataProvider
{
    SiteMetadata GetSite();
}
