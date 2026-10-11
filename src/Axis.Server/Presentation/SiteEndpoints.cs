using Axis.Presentation.Sites;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Users;

namespace Axis.Server.Presentation;

/// <summary>
/// Read-only endpoints that describe the sites of the tenant's active applications, their texts and
/// their pages to the SPA. A site is found by its path. The endpoints need no session. For a
/// signed-in user, a site's navigation leaves out the pages they cannot read, such a page answers
/// 403, and page metadata lists only the actions and access their grants allow. Without a session
/// the metadata is not shaped, and the record API and the data source endpoint start sign-in.
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

        sites.MapGet("/{path}", async (HttpContext context, string path, ActiveApplicationResolver resolver, CancellationToken cancellationToken) =>
        {
            if (await resolver.ResolveBySitePathAsync(path, cancellationToken) is not { } active)
            {
                return SiteNotFound();
            }

            var site = ApplicationSites.Describe(active.Site);
            if (await CurrentUsers.FindAsync(context) is not { } user)
            {
                return Results.Ok(site);
            }

            // The compiler resolves every navigation entry's page, so the lookup always succeeds.
            var readable = active.Site.Navigation
                .Where(entry => PageAccess.CanRead(active.Application, user.Roles, active.Application.Pages.First(page => page.Id == entry.Page.Id)))
                .Select(entry => entry.Page.Name)
                .ToHashSet(StringComparer.Ordinal);
            return Results.Ok(site with { Navigation = [.. site.Navigation.Where(item => readable.Contains(item.Page))] });
        });

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

        sites.MapGet("/{path}/pages/{page}", async (HttpContext context, string path, string page, ActiveApplicationResolver resolver, CancellationToken cancellationToken) =>
        {
            if (await resolver.ResolveBySitePathAsync(path, cancellationToken) is not { } active)
            {
                return SiteNotFound();
            }

            if (ApplicationSites.FindPage(active.Application, page) is not { } metadata)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The site's application has no page with this name.");
            }

            if (await CurrentUsers.FindAsync(context) is not { } user)
            {
                return Results.Ok(metadata);
            }

            // The metadata holds the page's declared name, so the lookup always succeeds.
            var model = active.Application.Pages.First(candidate => candidate.Name == metadata.Name);
            return PageAccess.CanRead(active.Application, user.Roles, model)
                ? Results.Ok(PageAccess.Shape(active.Application, user.Roles, model, metadata))
                : Grants.Forbidden();
        });
    }

    private static IResult SiteNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No site is active under this path.");
}
