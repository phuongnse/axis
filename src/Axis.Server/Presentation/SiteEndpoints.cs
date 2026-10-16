using Axis.Presentation.Sites;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Users;

namespace Axis.Server.Presentation;

/// <summary>
/// Read-only endpoints that describe the sites of the tenant's active applications, their texts and
/// their pages to the SPA. A site is found by its path. The endpoints need a session: without one
/// they answer 401 before any 404, so a signed-out caller cannot learn which sites or pages exist.
/// A site's navigation leaves out the pages the user cannot read, such a page answers 403, and page
/// metadata lists only the fields, actions and access the user's grants allow. The texts endpoint
/// answers the texts of the active release, or of any stored release of the site's application when
/// it is given a <c>release</c> id, so a task page can show the texts of the release it started on.
/// </summary>
internal static class SiteEndpoints
{
    public static void MapSiteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sites = endpoints.MapGroup("/api/sites");

        sites.MapGet("", async (HttpContext context, ActiveApplicationResolver resolver, CancellationToken cancellationToken) =>
        {
            if (await CurrentUsers.FindAsync(context) is null)
            {
                return Grants.SignInRequired();
            }

            var applications = await resolver.ListAsync(cancellationToken);
            var items = applications
                .SelectMany(application => application.Sites.Select(site => ApplicationSites.ListItem(application, site)))
                .OrderBy(item => item.Path, StringComparer.Ordinal)
                .ToList();
            return Results.Ok(new SiteList(items));
        });

        sites.MapGet("/{path}", async (HttpContext context, string path, ActiveApplicationResolver resolver, CancellationToken cancellationToken) =>
        {
            if (await CurrentUsers.FindAsync(context) is not { } user)
            {
                return Grants.SignInRequired();
            }

            if (await resolver.ResolveBySitePathAsync(path, cancellationToken) is not { } active)
            {
                return SiteNotFound();
            }

            var site = ApplicationSites.Describe(active.Site);

            // The compiler resolves every navigation entry's page, so the lookup always succeeds.
            var readable = active.Site.Navigation
                .Where(entry => PageAccess.CanRead(active.Application, user.Roles, active.Application.Pages.First(page => page.Id == entry.Page.Id)))
                .Select(entry => entry.Page.Name)
                .ToHashSet(StringComparer.Ordinal);
            return Results.Ok(site with { Navigation = [.. site.Navigation.Where(item => readable.Contains(item.Page))] });
        });

        sites.MapGet("/{path}/texts/{locale}", async (HttpContext context, string path, string locale, string? release, ActiveApplicationResolver resolver, CancellationToken cancellationToken) =>
        {
            if (await CurrentUsers.FindAsync(context) is null)
            {
                return Grants.SignInRequired();
            }

            if (await resolver.ResolveBySitePathAsync(path, cancellationToken) is not { } active)
            {
                return SiteNotFound();
            }

            var model = active.Application;
            if (release is not null)
            {
                // Only the hyphenated form names a release; any other text is no release.
                if (!Guid.TryParseExact(release, "D", out var releaseId) || release.Length != 36
                    || await resolver.FindReleaseModelAsync(model.Manifest.Id, releaseId, cancellationToken) is not { } found)
                {
                    return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No release of the site's application exists with this id.");
                }

                model = found;
            }

            return ApplicationSites.FindTexts(model, locale) is { } texts
                ? Results.Ok(texts)
                : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No text resources exist for this locale.");
        });

        sites.MapGet("/{path}/pages/{page}", async (HttpContext context, string path, string page, ActiveApplicationResolver resolver, CancellationToken cancellationToken) =>
        {
            if (await CurrentUsers.FindAsync(context) is not { } user)
            {
                return Grants.SignInRequired();
            }

            if (await resolver.ResolveBySitePathAsync(path, cancellationToken) is not { } active)
            {
                return SiteNotFound();
            }

            if (ApplicationSites.FindPage(active.Application, page) is not { } metadata)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The site's application has no page with this name.");
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
