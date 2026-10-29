using Axis.Core.Tenancy;
using Axis.Presentation.Sites;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Users;
using Microsoft.AspNetCore.Authentication;

namespace Axis.Server.Presentation;

/// <summary>
/// Read-only endpoints that describe the sites of the tenant's active applications, their texts and
/// their pages to the SPA. A site is found by its path. The endpoints need a session: without one
/// they answer 401 before any 404, so a signed-out caller cannot learn which sites or pages exist.
/// A site's navigation leaves out the pages the user cannot read, such a page answers 403, and page
/// metadata lists only the fields, actions and access the user's grants allow. A session that came through a provider the
/// site does not allow answers 401 on the site, its texts and its pages, after the 401 for no session and before any 404.
/// A tenant without site clients checks nothing. The list of sites is not filtered. The texts endpoint
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

        sites.MapGet("/{path}", async (HttpContext context, string path, ActiveApplicationResolver resolver, SiteSignIn signIn, ITenantContextAccessor tenants, CancellationToken cancellationToken) =>
        {
            if (await CurrentUsers.FindAsync(context) is not { } user)
            {
                return Grants.SignInRequired();
            }

            if (!await ServesSessionAsync(context, signIn, tenants, path, cancellationToken))
            {
                return Grants.SignInRequired();
            }

            if (await resolver.ResolveBySitePathAsync(path, cancellationToken) is not { } active)
            {
                return SiteNotFound();
            }

            var site = ApplicationSites.Describe(active.Application, active.Site);

            // The compiler resolves every navigation entry's page, so the lookup always succeeds.
            var readable = active.Site.Navigation
                .Where(entry => PageAccess.CanRead(active.Application, user.RolesIn(active.Application), active.Application.Pages.First(page => page.Id == entry.Page.Id)))
                .Select(entry => entry.Page.Name)
                .ToHashSet(StringComparer.Ordinal);
            return Results.Ok(site with { Navigation = [.. site.Navigation.Where(item => readable.Contains(item.Page))] });
        });

        sites.MapGet("/{path}/texts/{locale}", async (HttpContext context, string path, string locale, string? release, ActiveApplicationResolver resolver, SiteSignIn signIn, ITenantContextAccessor tenants, CancellationToken cancellationToken) =>
        {
            if (await CurrentUsers.FindAsync(context) is null)
            {
                return Grants.SignInRequired();
            }

            if (!await ServesSessionAsync(context, signIn, tenants, path, cancellationToken))
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

        sites.MapGet("/{path}/pages/{page}", async (HttpContext context, string path, string page, ActiveApplicationResolver resolver, SiteSignIn signIn, ITenantContextAccessor tenants, CancellationToken cancellationToken) =>
        {
            if (await CurrentUsers.FindAsync(context) is not { } user)
            {
                return Grants.SignInRequired();
            }

            if (!await ServesSessionAsync(context, signIn, tenants, path, cancellationToken))
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
            return PageAccess.CanRead(active.Application, user.RolesIn(active.Application), model)
                ? Results.Ok(PageAccess.Shape(active.Application, user.RolesIn(active.Application), model, metadata))
                : Grants.Forbidden();
        }).WithQueryTimeout(QueryTimeoutSettings.Interactive);
    }

    // The user check has already passed, and the site routes are no service account routes, so the user came from the session.
    private static async Task<bool> ServesSessionAsync(
        HttpContext context,
        SiteSignIn signIn,
        ITenantContextAccessor tenants,
        string path,
        CancellationToken cancellationToken)
    {
        var tenant = tenants.Current ?? throw new InvalidOperationException("No tenant context is set.");
        var session = await context.AuthenticateAsync(Session.Scheme);
        return await signIn.ServesAsync(tenant.TenantId, path, session.Succeeded ? Session.ReadIdentityProvider(session.Principal) : null, cancellationToken);
    }

    private static IResult SiteNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No site is active under this path.");
}
