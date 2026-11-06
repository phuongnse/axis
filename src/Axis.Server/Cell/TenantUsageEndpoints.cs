using Axis.Core.Tenancy;
using Axis.Server.Http;
using Axis.Server.Studio;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;
using Npgsql;

namespace Axis.Server.CellApi;

/// <summary>
/// The usage route of a tenant administrator on the tenant's own host, see <c>docs/reference/tenant-quotas.md#usage</c>.
/// <c>GET /api/tenant/usage</c> answers each quota with its limit and what the tenant uses now, and the tenant's own day rows of
/// <c>platform.tenant_usage</c> for today and the 29 days before it, newest first. It holds sizes and counts only. It checks, in
/// order, a caller (<c>401</c>), the administrator role (<c>403</c>) and a tenant of a cell with a users client that is not a preview
/// tenant (<c>404</c>), the checks of the users routes. It answers <c>503</c> when the platform database cannot be read.
/// <para>
/// What a tenant uses now is the latest storage sample, the enabled users of the realm, Studio's application count and the emails
/// sent today. A concurrency quota, <c>workerWeight</c> and <c>authoringRequests</c> have no use to count and answer <c>null</c>. When
/// the identity provider cannot count the users, <c>users</c> answers <c>null</c> and the server logs a warning.
/// </para>
/// </summary>
internal static partial class TenantUsageEndpoints
{
    /// <summary>How many days the route answers, today included.</summary>
    private const int Days = 30;

    public static void MapTenantUsageEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/tenant/usage", ReadAsync);

    private static async Task<IResult> ReadAsync(
        HttpContext httpContext,
        UsersSettings users,
        TenantQuotas quotas,
        ITenantCatalog catalog,
        ITenantContextAccessor tenants,
        TenantUsageStore usage,
        TenantDailyCounterStore counters,
        TenantDatabase database,
        StudioOptions options,
        IHttpClientFactory httpClients,
        TimeProvider time,
        ILogger<TenantUsageLog> logger,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        if (!users.Allows(user.Roles))
        {
            return Grants.Forbidden();
        }

        if (!quotas.IsCell
            || tenants.Current is not { } tenant
            || catalog.Find(tenant.TenantId) is not { PreviewOf: null, RealmUsers: { } access })
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "This tenant has no usage.");
        }

        var tenantId = tenant.TenantId;
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        long? storage;
        long emails;
        IReadOnlyList<TenantUsageRow> rows;
        try
        {
            storage = await usage.LatestStorageBytesAsync(tenantId, cancellationToken);
            emails = await counters.ValueAsync(tenantId, TenantDailyCounterStore.Emails, today, cancellationToken);
            rows = await usage.ListDaysAsync(tenantId, today.AddDays(1 - Days), cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            LogUnreadable(logger, tenantId, exception.Message);
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The usage cannot be read now. Try again in a moment.");
        }

        var used = new Dictionary<string, long?>(StringComparer.Ordinal)
        {
            [QuotaKeys.Storage] = storage,
            [QuotaKeys.Users] = await CountUsersAsync(httpClients, access, tenantId, logger, cancellationToken),
            [QuotaKeys.Applications] = await StudioApplicationEndpoints.CountApplicationsAsync(
                database, StudioEndpoints.ForRequest(httpContext, options), cancellationToken),
            [QuotaKeys.EmailsPerDay] = emails,
        };
        var limits = quotas.For(tenantId);
        return Results.Ok(new TenantUsageResponse(
            QuotaKeys.All.ToDictionary(
                quota => quota,
                quota => new TenantQuotaUseResponse(limits[quota], used.GetValueOrDefault(quota)),
                StringComparer.Ordinal),
            [.. rows.Select(row => new TenantUsageDayResponse(
                row.Day, row.DatabaseBytes, row.FileBytes, row.Users, row.Applications, row.EmailsSent, row.ConnectorCalls, row.WorkItemsRun))]));
    }

    private static async Task<long?> CountUsersAsync(
        IHttpClientFactory httpClients, TenantRealmUsers access, string tenantId, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClients.CreateClient(nameof(TenantUsageEndpoints));
            return await new RealmUsers(client, access).CountEnabledAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            LogUsersFailed(logger, tenantId, exception.Message);
            return null;
        }
    }

    [LoggerMessage(LogLevel.Warning, "The users of tenant {TenantId} could not be counted for its usage. {Reason}")]
    private static partial void LogUsersFailed(ILogger logger, string tenantId, string reason);

    [LoggerMessage(LogLevel.Warning, "The usage of tenant {TenantId} could not be read from the platform database. {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string tenantId, string reason);
}

/// <summary>The category of the log of <see cref="TenantUsageEndpoints"/>.</summary>
internal sealed class TenantUsageLog;

/// <summary>The answer of <c>GET /api/tenant/usage</c>: each quota by key, and the day rows, newest first.</summary>
internal sealed record TenantUsageResponse(IReadOnlyDictionary<string, TenantQuotaUseResponse> Quotas, IReadOnlyList<TenantUsageDayResponse> Days);

/// <summary>A quota's <c>limit</c>, <c>null</c> for no limit, and what the tenant <c>used</c> now, <c>null</c> when nothing counts it.</summary>
internal sealed record TenantQuotaUseResponse(long? Limit, long? Used);

/// <summary>One day of <c>platform.tenant_usage</c>. It holds sizes and counts only.</summary>
internal sealed record TenantUsageDayResponse(
    DateOnly Day, long DatabaseBytes, long FileBytes, int? Users, int? Applications, int EmailsSent, long ConnectorCalls, long WorkItemsRun);
