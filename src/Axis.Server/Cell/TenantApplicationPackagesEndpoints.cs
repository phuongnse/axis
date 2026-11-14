using System.Text.Json;
using Axis.Administration;
using Axis.Core.Tenancy;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// The routes of a tenant administrator's application packages switch on the tenant's own host, see
/// <c>docs/reference/user-administration.md#application-packages</c>. They check, in order, a session (<c>401</c>), the
/// administrator role (<c>403</c>) and a tenant with a directory row that is not a preview tenant (<c>404</c>), and for <c>PUT</c>
/// then the body (<c>400</c>) and the quota (<c>403</c>). They do not read <c>Extensions:ApplicationPackages</c>, so the switch
/// works with the setting off (decision D148).
/// <para>
/// <c>PUT</c> stores the switch and raises the tenant's version, so every host reads it at its next refresh, with a platform audit
/// record in the same transaction that names <c>applicationPackages</c>. While the tenant's <c>packageCpuSecondsPerDay</c> is 0, a
/// <c>PUT</c> that turns packages on is the quota's <c>403</c> (<c>docs/reference/tenant-quotas.md#refusals</c>) and writes nothing.
/// Turning packages off is never refused. <c>GET</c> answers the stored switch.
/// </para>
/// </summary>
internal static class TenantApplicationPackagesEndpoints
{
    internal const string SetCommand = "tenant application-packages set";

    public static void MapTenantApplicationPackagesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/tenant/application-packages", ReadAsync);
        endpoints.MapPut("/api/tenant/application-packages", SetAsync);
    }

    private static async Task<IResult> ReadAsync(
        HttpContext httpContext,
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        await using var platform = CellEndpoints.Open(dataSource);
        var stored = await platform.Tenants.AsNoTracking()
            .Where(row => row.Id == open.TenantId && row.PreviewOf == null)
            .Select(row => new { row.ApplicationPackages })
            .SingleOrDefaultAsync(cancellationToken);
        return stored is null
            ? NoSetting()
            : Results.Ok(new TenantApplicationPackagesResponse(stored.ApplicationPackages));
    }

    private static async Task<IResult> SetAsync(
        HttpContext httpContext,
        CellPlatformWriter writer,
        TenantQuotas quotas,
        TenantDailyCounterStore counters,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var (opening, refusal) = await OpenAsync(httpContext);
        if (opening is not { } open)
        {
            return refusal!;
        }

        TenantApplicationPackagesRequest? body = null;
        if (httpContext.Request.HasJsonContentType())
        {
            try
            {
                body = await httpContext.Request.ReadFromJsonAsync<TenantApplicationPackagesRequest>(cancellationToken);
            }
            catch (JsonException)
            {
            }
        }

        if (body?.On is not { } on)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The request needs on: true or false.");
        }

        // Only a body that turns packages on is refused, so the quota check follows the body check.
        if (on && quotas.Limit(open.TenantId, QuotaKeys.PackageCpuSecondsPerDay) is 0)
        {
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            var used = await counters.ValueAsync(open.TenantId, TenantDailyCounterStore.PackageCpuSeconds, today, cancellationToken);
            QuotaRefusal.Log(loggers, open.TenantId, QuotaKeys.PackageCpuSecondsPerDay);
            return QuotaRefusal.Problem(QuotaKeys.PackageCpuSecondsPerDay, 0, used);
        }

        await using var platform = writer.Open();
        await using var transaction = await platform.Database.BeginTransactionAsync(cancellationToken);
        var row = await platform.Tenants.SingleOrDefaultAsync(tenant => tenant.Id == open.TenantId && tenant.PreviewOf == null, cancellationToken);
        if (row is null)
        {
            return NoSetting();
        }

        row.ApplicationPackages = on;
        row.Version++;
        await PlatformAuditRecords.WriteAsync(platform, SetCommand, open.TenantId, ["applicationPackages"], open.CallerId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.NoContent();
    }

    private static IResult NoSetting() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no application packages setting.");

    // A signed-in user from the session only, because a service account's bearer token has no right here (401), the administrator
    // role (403) and a tenant with a directory row that is not a preview tenant (404).
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

        if (services.GetRequiredService<ITenantContextAccessor>().Current is not { } tenant
            || services.GetRequiredService<ITenantCatalog>().Find(tenant.TenantId) is not { PreviewOf: null })
        {
            return (null, NoSetting());
        }

        return (new Opening(tenant.TenantId, caller.Id), null);
    }

    private sealed record Opening(string TenantId, string CallerId);
}

/// <summary>The body of <c>PUT /api/tenant/application-packages</c>.</summary>
internal sealed record TenantApplicationPackagesRequest(bool? On);

/// <summary>The answer of <c>GET /api/tenant/application-packages</c>.</summary>
internal sealed record TenantApplicationPackagesResponse(bool On);
