using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Axis.Server.Http;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// The request for a host of a tenant's own and the read of a tenant's host requests, shared by the provisioning API's
/// <c>POST /api/cell/tenants/{id}/hosts</c> and the tenant administrator's <c>/api/tenant/hosts</c> routes, so both answer the same and run
/// the same checks, see <c>docs/reference/tenant-lifecycle.md#hosts</c> and
/// <c>docs/reference/tenant-lifecycle.md#hosts-by-a-tenant-administrator</c>. The caller names the tenant and the actor, the operator's id
/// or the administrator's id.
/// </summary>
internal static class CellHostRequests
{
    private const int MaxHostLength = 253;

    private const int MaxHostLabelLength = 63;

    private static readonly TimeSpan _hostRequestWindow = TimeSpan.FromDays(7);

    [SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads", Justification = "The query must translate to lower(host) to use the unique index on lower(host).")]
    internal static async Task<IResult> RequestAsync(
        string tenantId,
        CellHostRequest? request,
        string actor,
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        CellOptions cell,
        CancellationToken cancellationToken)
    {
        var trimmed = request?.Host?.Trim();
        if (trimmed is null || !IsValidHost(trimmed.ToLowerInvariant()))
        {
            return RefusedRequests.Problem(
                httpContext, StatusCodes.Status400BadRequest, "The host must be a DNS name such as approvals.example.com.");
        }

        // Hosts are ASCII, so lower() in the database matches this lower case.
        var host = trimmed.ToLowerInvariant();
        await using var platform = CellEndpoints.Open(dataSource);
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);

        // The lock keeps two parallel retries from inserting two pending requests with different tokens.
        await platform.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended('axis-host-request:' || {tenantId} || ':' || {host}, 0))",
            cancellationToken);
        if (!await platform.Tenants.AnyAsync(row => row.Id == tenantId && row.PreviewOf == null, cancellationToken))
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status404NotFound, "No tenant has this id.");
        }

        // The title names neither the host nor the tenant that holds it.
        if (await platform.TenantHosts.AnyAsync(row => row.Host.ToLower() == host, cancellationToken))
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status409Conflict, "This host already belongs to a tenant.");
        }

        // The title names neither the host nor the deleted tenant that held it.
        if (await DeletedTenantHolds.IsHostHeldBackAsync(platform, [host], cancellationToken))
        {
            return RefusedRequests.Problem(httpContext, StatusCodes.Status409Conflict, "This host is held back after a tenant was deleted.");
        }

        // A request whose expiry has passed is not pending, although its state says so until a later step marks it expired.
        var now = DateTimeOffset.UtcNow;
        var pending = await platform.TenantHostRequests.AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.Host == host && row.State == "pending" && row.ExpiresAt > now)
            .OrderByDescending(row => row.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (pending is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(HostResponse(pending, cell));
        }

        var created = new TenantHostRequestRow
        {
            TenantId = tenantId,
            Host = host,
            Token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)),
            State = "pending",
            RequestedAt = now,
            ExpiresAt = now + _hostRequestWindow,
            Actor = actor,
        };
        platform.TenantHostRequests.Add(created);
        await platform.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Json(HostResponse(created, cell), statusCode: StatusCodes.Status201Created);
    }

    // The token stays out of the select. A request whose expiry has passed is not pending, as in RequestAsync, see docs/reference/tenant-lifecycle.md#hosts.
    internal static async Task<List<CellHostRequestStateResponse>> ReadAsync(
        PlatformDbContext platform, string tenantId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var rows = await platform.TenantHostRequests.AsNoTracking()
            .Where(row => row.TenantId == tenantId)
            .Select(row => new { row.Host, row.State, row.RequestedAt, row.ExpiresAt })
            .ToListAsync(cancellationToken);
        return [.. rows
            .GroupBy(row => row.Host, StringComparer.Ordinal)
            .Select(group => group.MaxBy(row => row.RequestedAt)!)
            .OrderBy(row => row.Host, StringComparer.Ordinal)
            .Select(row => new CellHostRequestStateResponse(
                row.Host, row.State == "pending" && row.ExpiresAt <= now ? "expired" : row.State, row.ExpiresAt))];
    }

    private static CellHostResponse HostResponse(TenantHostRequestRow request, CellOptions cell) =>
        new(
            request.Host,
            request.Token,
            request.State,
            request.ExpiresAt,
            new CellDnsRecord("_axis-verify." + request.Host, request.Token),
            new CellCnameRecord(request.Host, $"{request.TenantId}.{cell.TenantDomain!.Trim()}"));

    // A host of a tenant's own must be a public DNS name that can carry a CNAME, so it has at least two labels and is no IP address.
    private static bool IsValidHost(string host)
    {
        if (host.Length is 0 or > MaxHostLength || Uri.CheckHostName(host) != UriHostNameType.Dns)
        {
            return false;
        }

        var labels = host.Split('.');
        return labels.Length >= 2 && labels.All(IsValidHostLabel);
    }

    private static bool IsValidHostLabel(string label) =>
        label.Length is >= 1 and <= MaxHostLabelLength
        && label[0] != '-'
        && label[^1] != '-'
        && label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
}
