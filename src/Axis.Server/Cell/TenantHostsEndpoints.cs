using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Users;
using Axis.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// The routes of a tenant administrator's hosts on the tenant's own host, see
/// <c>docs/reference/tenant-lifecycle.md#hosts-by-a-tenant-administrator</c>. They check, in order, a session (<c>401</c>), the
/// administrator role (<c>403</c>) and a cell tenant that is not a preview tenant (<c>404</c>). They work while the tenant is
/// <c>active</c>, and while it is <c>readOnly</c> the list answers and the add is refused.
/// <para>
/// <c>POST /api/tenant/hosts</c> takes <c>{ "host" }</c> and runs the request of the provisioning API's host route with the administrator as
/// the request's actor, see <see cref="CellHostRequests"/>. <c>GET /api/tenant/hosts</c> answers <c>{ items }</c>, the newest request of
/// each host as <c>{ host, state, expiresAt }</c> without the token, sorted by host.
/// </para>
/// </summary>
internal static class TenantHostsEndpoints
{
    public static void MapTenantHostsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenant/hosts", ListAsync);
        endpoints.MapPost("/api/tenant/hosts", RequestAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        await using var platform = CellEndpoints.Open(dataSource);
        return Results.Ok(new TenantHostsResponse(await CellHostRequests.ReadAsync(platform, open.TenantId, time.GetUtcNow(), cancellationToken)));
    }

    private static async Task<IResult> RequestAsync(
        CellHostRequest? request,
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        CellOptions cell,
        CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        return await CellHostRequests.RequestAsync(open.TenantId, request, open.CallerId, httpContext, dataSource, cell, cancellationToken);
    }

    // A signed-in user from the session only (401), the administrator role (403) and a cell tenant that is not a preview tenant (404).
    private static async Task<(Opening? Opening, IResult? Refusal)> OpenAsync(HttpContext httpContext)
    {
        var services = httpContext.RequestServices;
        var authenticated = await httpContext.AuthenticateAsync(Session.Scheme);
        if (!authenticated.Succeeded || Session.Read(authenticated.Principal) is not { } caller)
        {
            return (null, Grants.SignInRequired());
        }

        if (!services.GetRequiredService<UsersSettings>().Allows(caller.Roles))
        {
            return (null, Grants.Forbidden());
        }

        var tenantId = TenantExportEndpoints.CellTenant(
            services.GetRequiredService<CellOptions>(), services.GetRequiredService<ITenantCatalog>(), services.GetRequiredService<ITenantContextAccessor>());
        if (tenantId is null)
        {
            return (null, Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant cannot add hosts."));
        }

        return (new Opening(tenantId, caller.Id), null);
    }

    private sealed record Opening(string TenantId, string CallerId);
}

internal sealed record TenantHostsResponse(IReadOnlyList<CellHostRequestStateResponse> Items);
