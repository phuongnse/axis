using Axis.Configuration.Resources;
using Axis.Core.Tenancy;
using Axis.Server.Applications;
using Axis.Tenancy;

namespace Axis.Server.Tenancy;

/// <summary>
/// Gives a site its own origin. A host that belongs to a site serves only that site, and the tenant's other hosts
/// send the site's path to the site's host. See the sites and sign-in section of the identity provider reference.
/// </summary>
internal sealed class SiteHostMiddleware(
    RequestDelegate next,
    ITenantContextAccessor tenants,
    ITenantCatalog catalog,
    IWebHostEnvironment environment)
{
    private static readonly PathString _api = new("/api");

    public async Task InvokeAsync(HttpContext context, ActiveApplicationResolver resolver)
    {
        // Health probes resolve no tenant and belong to no site.
        if (tenants.Current is not { } tenant)
        {
            await next(context);
            return;
        }

        if (tenant.SitePath is { } site)
        {
            if (context.Request.Path == "/")
            {
                context.Response.Redirect($"{context.Request.PathBase}/{site}");
                return;
            }

            if (!await IsServedAsync(context, resolver, site))
            {
                await Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nothing is served at this path on this host.")
                    .ExecuteAsync(context);
                return;
            }
        }
        else if (SiteHostOf(context, tenant) is { } siteHost)
        {
            var request = context.Request;
            var port = request.Host.Port is { } value ? ":" + value : "";
            context.Response.Redirect($"{request.Scheme}://{siteHost}{port}{request.PathBase}{request.Path}{request.QueryString}");
            return;
        }

        await next(context);
    }

    // What a site's host serves: the SPA under the site's path, the web root's files, sign-in, who is signed in, what the
    // SPA shell loads on every host, the site's own API and its application's API.
    private async Task<bool> IsServedAsync(HttpContext context, ActiveApplicationResolver resolver, string site)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments(_api))
        {
            return FirstSegment(path) is { } first && string.Equals(first, site, StringComparison.OrdinalIgnoreCase)
                || IsWebRootFile(path);
        }

        if (path.StartsWithSegments("/api/auth") || path == "/api/me" || path == "/api/site" || path.StartsWithSegments("/api/texts") || path == "/api/about")
        {
            return true;
        }

        if (path.StartsWithSegments("/api/sites", out var rest))
        {
            return string.Equals(FirstSegment(rest), site, StringComparison.OrdinalIgnoreCase);
        }

        if (path.StartsWithSegments("/api/apps", out rest) && FirstSegment(rest) is { } app)
        {
            var active = await resolver.ResolveBySitePathAsync(site, context.RequestAborted);
            return active is not null && string.Equals(app, active.Application.Manifest.Name, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    // The site host of the site that the request's first path segment names, when the tenant has a host for it.
    private string? SiteHostOf(HttpContext context, TenantContext tenant)
    {
        if (context.Request.Path.StartsWithSegments(_api)
            || FirstSegment(context.Request.Path) is not { } segment
            || !SitePaths.IsValid(segment)
            || catalog.Find(tenant.TenantId)?.HostSites is not { } hostSites)
        {
            return null;
        }

        var site = segment.ToLowerInvariant();
        return hostSites.FirstOrDefault(pair => pair.Value == site).Key;
    }

    private bool IsWebRootFile(PathString path) =>
        environment.WebRootFileProvider.GetFileInfo(path.Value!) is { Exists: true, IsDirectory: false };

    private static string? FirstSegment(PathString path) =>
        path.Value?.Split('/', 2, StringSplitOptions.RemoveEmptyEntries) is [var first, ..] ? first : null;
}

internal static class SiteHostApplicationBuilderExtensions
{
    public static IApplicationBuilder UseSiteHosts(this IApplicationBuilder app) =>
        app.UseMiddleware<SiteHostMiddleware>();
}
