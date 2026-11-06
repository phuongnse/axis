using Axis.Administration;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Users;
using Axis.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// The routes of a tenant administrator's export on the tenant's own host, see
/// <c>docs/reference/tenant-lifecycle.md#exports-by-a-tenant-administrator</c>. They check, in order, a session (<c>401</c>), the
/// administrator role (<c>403</c>) and a cell tenant that is not a preview tenant (<c>404</c>). They work while the tenant is
/// <c>active</c> and while it is <c>readOnly</c>.
/// <para>
/// <c>POST /api/tenant/exports</c> queues the <c>export</c> job of the tenant with the administrator as actor, writes a
/// <c>tenant export start</c> platform audit record and answers <c>202</c> with the export. While an export of the tenant is queued or
/// running, whoever started it, it answers <c>200</c> with that export and queues nothing. It needs no <c>Idempotency-Key</c> and no
/// body. <c>GET /api/tenant/exports/{exportId}</c> answers like the provisioning API's exports route, see <see cref="CellExportReads"/>.
/// </para>
/// </summary>
internal static class TenantExportEndpoints
{
    public static void MapTenantExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/tenant/exports", StartAsync);
        endpoints.MapGet("/api/tenant/exports/{exportId:guid}", ReadAsync);
    }

    private static async Task<IResult> StartAsync(
        HttpContext httpContext,
        CellPlatformWriter writer,
        CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        // The lock, the check, the job and its record share one transaction, so they are saved together or not at all and two
        // parallel starts queue one job.
        await using var platform = writer.Open();
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
        await platform.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended('axis-tenant-export:' || {open.TenantId}, 0))",
            cancellationToken);
        var running = await platform.Jobs.AsNoTracking()
            .Where(job => job.Kind == "export" && job.TenantId == open.TenantId && (job.State == "queued" || job.State == "running"))
            .OrderByDescending(job => job.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (running is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            httpContext.Response.Headers.Location = Location(running.Id);
            return Results.Ok(new CellExportResponse(running.Id, running.TenantId, running.State, running.CreatedAt, null));
        }

        var created = CellEndpoints.NewJob("export", open.TenantId, open.CallerId, null, null, null);
        platform.Jobs.Add(created);
        await PlatformAuditRecords.WriteAsync(platform, TenantExports.StartCommand, open.TenantId, [$"export:{created.Id:D}"], open.CallerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Accepted(Location(created.Id), new CellExportResponse(created.Id, created.TenantId, created.State, created.CreatedAt, null));
    }

    private static async Task<IResult> ReadAsync(
        Guid exportId,
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        CellPlatformWriter writer,
        CellExportStorage storage,
        CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        return await CellExportReads.AnswerAsync(open.TenantId, exportId, open.CallerId, httpContext, dataSource, writer, storage, cancellationToken);
    }

    private static string Location(Guid exportId) => $"/api/tenant/exports/{exportId:D}";

    // A signed-in user from the session only, because a service account's bearer token has no export right (401), the administrator
    // role (403) and a cell tenant that is not a preview tenant (404).
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

        var tenantId = ExportTenant(
            services.GetRequiredService<CellOptions>(), services.GetRequiredService<ITenantCatalog>(), services.GetRequiredService<ITenantContextAccessor>());
        if (tenantId is null)
        {
            return (null, Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no exports."));
        }

        return (new Opening(tenantId, caller.Id), null);
    }

    /// <summary>
    /// The id of the current tenant when it has exports: a cell with a tenant in the catalog that is not a preview tenant.
    /// Otherwise <c>null</c>. The routes answer <c>404</c> for <c>null</c> and <c>GET /api/me</c> answers <c>tenantExports</c> false.
    /// </summary>
    internal static string? ExportTenant(CellOptions cell, ITenantCatalog catalog, ITenantContextAccessor tenants) =>
        cell.IsCell && tenants.Current is { } tenant && catalog.Find(tenant.TenantId) is { PreviewOf: null }
            ? tenant.TenantId
            : null;

    private sealed record Opening(string TenantId, string CallerId);
}
