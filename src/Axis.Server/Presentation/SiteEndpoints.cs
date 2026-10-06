using Axis.Presentation.Sites;
using Axis.Server.Applications;

namespace Axis.Server.Presentation;

/// <summary>
/// Read-only endpoints that describe the sites of the tenant's active applications, their texts and
/// their pages to the SPA. A site is found by its path.
/// </summary>
internal static class SiteEndpoints
{
    public static void MapSiteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sites = endpoints.MapGroup("/api/sites");

        sites.MapGet("", async (ActiveApplicationResolver resolver, CancellationToken cancellationToken) =>
        {
            var applications = await resolver.ListAsync(cancellationToken);
            var items = applications
                .SelectMany(application => application.Sites.Select(site => ApplicationSites.ListItem(application, site)))
                .OrderBy(item => item.Path, StringComparer.Ordinal)
                .ToList();
            return Results.Ok(new SiteList(items));
        });

        sites.MapGet("/{path}", async (string path, ActiveApplicationResolver resolver, CancellationToken cancellationToken) =>
            await resolver.ResolveBySitePathAsync(path, cancellationToken) is { } active
                ? Results.Ok(ApplicationSites.Describe(active.Site))
                : SiteNotFound());

        sites.MapGet("/{path}/texts/{locale}", async (string path, string locale, ActiveApplicationResolver resolver, CancellationToken cancellationToken) =>
        {
            if (await resolver.ResolveBySitePathAsync(path, cancellationToken) is not { } active)
            {
                return SiteNotFound();
            }

            return ApplicationSites.FindTexts(active.Application, locale) is { } texts
                ? Results.Ok(texts)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No text resources exist for this locale.");
        });

        sites.MapGet("/{path}/pages/{page}", async (string path, string page, ActiveApplicationResolver resolver, CancellationToken cancellationToken) =>
        {
            if (await resolver.ResolveBySitePathAsync(path, cancellationToken) is not { } active)
            {
                return SiteNotFound();
            }

            return ApplicationSites.FindPage(active.Application, page) is { } metadata
                ? Results.Ok(metadata)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The site's application has no page with this name.");
        });
    }

    private static IResult SiteNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No site is active under this path.");
}
