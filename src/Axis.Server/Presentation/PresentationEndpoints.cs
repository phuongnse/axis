using Axis.Presentation.Sites;
using Axis.Presentation.Texts;

namespace Axis.Server.Presentation;

/// <summary>Read-only endpoints that describe the site and its texts to the SPA.</summary>
internal static class PresentationEndpoints
{
    public static void MapPresentationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/site", (ISiteMetadataProvider sites) => Results.Ok(sites.GetSite()));

        endpoints.MapGet("/api/texts/{locale}", (string locale, ITextResourceProvider texts) =>
            texts.GetTexts(locale) is { } found
                ? Results.Ok(found)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No text resources exist for this locale."));
    }
}
