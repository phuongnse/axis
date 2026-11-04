using Axis.Server.Authentication;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// The routes of the cell's provisioning API under <c>/api/cell/</c>, see <c>docs/reference/tenant-lifecycle.md#provisioning-api</c>.
/// They answer only on a platform host, see <see cref="CellHosts"/>, and a tenant host answers <c>404</c> for every path under
/// the group. Every route of the group needs a bearer token of the operator realm with the operator role, see
/// <see cref="CellOperator"/>: <c>401</c> without a valid token, <c>403</c> without the role.
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
