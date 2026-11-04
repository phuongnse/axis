using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// The routes of the cell's provisioning API under <c>/api/cell/</c>, see <c>docs/reference/tenant-lifecycle.md#provisioning-api</c>.
/// They answer only on a platform host, see <see cref="CellHosts"/>, and a tenant host answers <c>404</c> for every path under
/// the group. Every route of the group but sign-in and sign-out needs a bearer token of the operator realm, or the
/// session of an operator who signed in to the console, with the operator role, see <see cref="CellOperator"/> and
/// <see cref="CellOperatorSession"/>: <c>401</c> without a valid token or session, <c>403</c> without the role.
/// <para>
/// <c>GET /api/cell/auth/sign-in?returnUrl</c> sends the operator to the operator realm's login page, see
/// <see cref="CellConsoleSignIn"/>, and answers <c>404</c> when the console offers no sign-in or the host is not
/// <see cref="CellOptions.ConsoleHost"/>. <c>POST /api/cell/auth/sign-out</c> deletes the session and answers <c>204</c>.
/// The two skip the operator policy, as they must work for a caller who has no session. Writes need the CSRF header.
/// </para>
/// <para>
/// <c>GET /api/cell/tenants</c> answers <c>{ "items": [...] }</c>, ordered by id, with each tenant's <c>id</c>,
/// <c>status</c>, <c>application</c> and <c>hosts</c>. <c>GET /api/cell/tenants/{id}</c> answers one such tenant. Both read the
/// platform tables over the reader role. A preview tenant belongs to its main tenant's Studio and is not a tenant of the
/// cell, so the list leaves it out and its id is a <c>404</c>. The <c>application</c> is <see langword="null"/> until the
/// create job records it.
/// </para>
/// </summary>
internal static class CellEndpoints
{
    public static void MapCellEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var cell = endpoints.MapGroup("/api/cell")
            .WithMetadata(CellOperatorRoute.Instance)
            .RequireAuthorization(CellOperator.Policy);
        cell.MapGet("/tenants", ListTenantsAsync);
        cell.MapGet("/tenants/{id}", GetTenantAsync);

        // Sign-in and sign-out are the only routes that skip the operator policy. They keep the route marker, so the console
        // host serves them and tenant resolution has no second way through.
        var auth = endpoints.MapGroup("/api/cell/auth")
            .WithMetadata(CellOperatorRoute.Instance)
            .AllowAnonymous();
        auth.MapGet("/sign-in", SignInAsync).WithRateLimit(Partition.SignIn);
        // The cast makes the handler a route handler, whose result is written, and not a RequestDelegate.
        auth.MapPost("/sign-out", (Delegate)SignOutAsync);
    }

    // Only a local path is a return address, so sign-in can never send the browser to another site.
    private static async Task<IResult> SignInAsync(
        HttpContext httpContext,
        string? returnUrl,
        CellOptions cell,
        IAuthenticationSchemeProvider schemes)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            returnUrl = "/";
        }
        else if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal) || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The return address must be a local path.");
        }

        if (await schemes.GetSchemeAsync(CellConsoleSignIn.Scheme) is null || !CellOperatorSession.IsConsoleHost(httpContext, cell.ConsoleHost))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This host offers no console sign-in.");
        }

        return Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, [CellConsoleSignIn.Scheme]);
    }

    private static async Task<IResult> SignOutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CellOperatorSession.Scheme);
        return Results.NoContent();
    }

    private static async Task<IResult> ListTenantsAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var platform = Open(dataSource);
        var tenants = await platform.Tenants.AsNoTracking()
            .Where(tenant => tenant.PreviewOf == null)
            .Select(tenant => new { tenant.Id, tenant.Status })
            .ToListAsync(cancellationToken);
        var hosts = await ReadHostsAsync(platform, tenants.Select(tenant => tenant.Id).ToList(), cancellationToken);

        return Results.Ok(new CellTenantListResponse(
            [.. tenants
                .OrderBy(tenant => tenant.Id, StringComparer.Ordinal)
                .Select(tenant => new CellTenantResponse(tenant.Id, tenant.Status, null, HostsOf(hosts, tenant.Id)))]));
    }

    private static async Task<IResult> GetTenantAsync(string id, NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var platform = Open(dataSource);
        var tenant = await platform.Tenants.AsNoTracking()
            .Where(row => row.Id == id && row.PreviewOf == null)
            .Select(row => new { row.Id, row.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (tenant is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No tenant has this id.");
        }

        var hosts = await ReadHostsAsync(platform, [tenant.Id], cancellationToken);
        return Results.Ok(new CellTenantResponse(tenant.Id, tenant.Status, null, HostsOf(hosts, tenant.Id)));
    }

    private static PlatformDbContext Open(NpgsqlDataSource dataSource) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>().UseNpgsql(dataSource).Options);

    private static async Task<ILookup<string, string>> ReadHostsAsync(
        PlatformDbContext platform,
        List<string> tenantIds,
        CancellationToken cancellationToken)
    {
        var rows = await platform.TenantHosts.AsNoTracking()
            .Where(host => tenantIds.Contains(host.TenantId))
            .Select(host => new { host.TenantId, host.Host })
            .ToListAsync(cancellationToken);
        return rows.ToLookup(row => row.TenantId, row => row.Host, StringComparer.Ordinal);
    }

    private static string[] HostsOf(ILookup<string, string> hosts, string tenantId) =>
        [.. hosts[tenantId].Order(StringComparer.Ordinal)];
}

/// <summary>One tenant of the cell. <c>Application</c> is null until the create job records it.</summary>
internal sealed record CellTenantResponse(string Id, string Status, string? Application, IReadOnlyList<string> Hosts);

/// <summary>The tenants of the cell, ordered by id.</summary>
internal sealed record CellTenantListResponse(IReadOnlyList<CellTenantResponse> Items);
