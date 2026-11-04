using Axis.Administration;
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
/// create job records it. The one tenant also answers its <c>quotas</c>: each quota key with its effective <c>value</c> and
/// whether the tenant <c>overridden</c> it, worked out from its <c>platform.tenant_quotas</c> rows and the cell's defaults.
/// </para>
/// <para>
/// <c>PUT /api/cell/tenants/{id}/quotas</c> takes <c>{ "set": { "users": 100, "storage": null }, "remove": ["applications"] }</c>.
/// A set value of <see langword="null"/> is no limit and a removed key goes back to the cell's default. A key the request
/// does not name stays as it is. It applies at once with no job and answers <c>200</c> with the tenant's quotas. An unknown key,
/// a negative value, a key both set and removed, or a request that names no key answers <c>400</c> and writes nothing. It writes
/// over the schema role, see <see cref="CellPlatformWriter"/>, and saves the rows with one platform audit record, command
/// <c>tenant quotas set</c>, naming the keys of the request, or saves neither. The record carries no operator, as
/// <c>platform.audit_records</c> has no such column yet.
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
        cell.MapPut("/tenants/{id}/quotas", SetQuotasAsync);

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

    private static async Task<IResult> GetTenantAsync(
        string id,
        NpgsqlDataSource dataSource,
        QuotaDefaults defaults,
        CancellationToken cancellationToken)
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
        var quotas = await ReadQuotasAsync(platform, tenant.Id, defaults, cancellationToken);
        return Results.Ok(new CellTenantDetailResponse(tenant.Id, tenant.Status, null, HostsOf(hosts, tenant.Id), quotas));
    }

    private static async Task<IResult> SetQuotasAsync(
        string id,
        CellQuotasRequest? request,
        CellPlatformWriter writer,
        QuotaDefaults defaults,
        CancellationToken cancellationToken)
    {
        var set = request?.Set ?? [];
        var remove = request?.Remove ?? [];
        var problem = ValidateQuotas(request, set, remove);
        if (problem is not null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: problem);
        }

        var named = set.Keys.Concat(remove).ToHashSet(StringComparer.Ordinal);
        await using var platform = writer.Open();
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
        if (!await platform.Tenants.AnyAsync(row => row.Id == id && row.PreviewOf == null, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No tenant has this id.");
        }

        foreach (var (quota, value) in set)
        {
            await platform.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO platform.tenant_quotas (tenant_id, quota, value, updated_at)
                VALUES ({id}, {quota}, CAST({value} AS bigint), now())
                ON CONFLICT (tenant_id, quota) DO UPDATE SET value = EXCLUDED.value, updated_at = EXCLUDED.updated_at
                """,
                cancellationToken);
        }

        if (remove.Count > 0)
        {
            await platform.TenantQuotas
                .Where(row => row.TenantId == id && remove.Contains(row.Quota))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await PlatformAuditRecords.WriteAsync(
            platform, "tenant quotas set", id, [.. QuotaKeys.All.Where(named.Contains)], cancellationToken);
        var quotas = await ReadQuotasAsync(platform, id, defaults, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new CellQuotasResponse(quotas));
    }

    private static string? ValidateQuotas(CellQuotasRequest? request, Dictionary<string, long?> set, List<string> remove)
    {
        if (request is null || set.Count + remove.Count == 0)
        {
            return "The request must set or remove at least one quota.";
        }

        var unknown = set.Keys.Concat(remove).Where(key => key is null || !QuotaKeys.All.Contains(key)).ToList();
        if (unknown.Count > 0)
        {
            return $"'{unknown[0]}' is not a quota. The quotas are {string.Join(", ", QuotaKeys.All)}.";
        }

        var negative = set.FirstOrDefault(entry => entry.Value < 0).Key;
        if (negative is not null)
        {
            return $"The value of '{negative}' must be zero or more, or null for no limit.";
        }

        var both = remove.FirstOrDefault(set.ContainsKey);
        return both is null ? null : $"'{both}' is both set and removed.";
    }

    private static async Task<Dictionary<string, CellQuotaResponse>> ReadQuotasAsync(
        PlatformDbContext platform,
        string tenantId,
        QuotaDefaults defaults,
        CancellationToken cancellationToken)
    {
        var rows = await platform.TenantQuotas.AsNoTracking()
            .Where(row => row.TenantId == tenantId)
            .ToDictionaryAsync(row => row.Quota, row => row.Value, StringComparer.Ordinal, cancellationToken);
        return QuotaKeys.All.ToDictionary(
            key => key,
            key => rows.TryGetValue(key, out var value) ? new CellQuotaResponse(value, true) : new CellQuotaResponse(defaults[key], false),
            StringComparer.Ordinal);
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

/// <summary>One tenant of the cell with its effective quotas, keyed by quota key.</summary>
internal sealed record CellTenantDetailResponse(
    string Id,
    string Status,
    string? Application,
    IReadOnlyList<string> Hosts,
    IReadOnlyDictionary<string, CellQuotaResponse> Quotas);

/// <summary>The body of the quotas write: the quotas to set, where <c>null</c> is no limit, and the quotas to take back to the default.</summary>
internal sealed record CellQuotasRequest(Dictionary<string, long?>? Set, List<string>? Remove);

/// <summary>One effective quota. <c>Value</c> is null for no limit. <c>Overridden</c> is false when the cell's default applies.</summary>
internal sealed record CellQuotaResponse(long? Value, bool Overridden);

/// <summary>The effective quotas of a tenant, keyed by quota key.</summary>
internal sealed record CellQuotasResponse(IReadOnlyDictionary<string, CellQuotaResponse> Quotas);
