using Axis.Tenancy;
using Axis.Tenancy.Realms;

namespace Axis.Server.Users;

/// <summary>
/// Rechecks the realm's users count against the tenant's users quota and switches the deny step of the first-login flow, see
/// <c>docs/reference/tenant-quotas.md#refusals</c>. It runs in a cell only, and only for a tenant with both realm clients. The recheck
/// has its own time limit and ignores an aborted request, so a client that disconnects after a command does not leave the step out
/// of step. A failure is logged with the tenant and the message, never a body or a secret, and is never thrown.
/// </summary>
internal static partial class UsersQuotaStep
{
    private static readonly TimeSpan _limit = TimeSpan.FromSeconds(5);

    public static async Task RefreshAsync(IServiceProvider services, string tenantId)
    {
        var quotas = services.GetRequiredService<TenantQuotas>();
        if (!quotas.IsCell
            || services.GetRequiredService<ITenantCatalog>().Find(tenantId) is not { Realm: { } realm, RealmUsers: { } users })
        {
            return;
        }

        try
        {
            using var limit = new CancellationTokenSource(_limit);
            using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(UsersQuotaStep));
            await new RealmUsersQuota(client).RefreshAsync(realm, users, quotas.Limit(tenantId, QuotaKeys.Users), limit.Token);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or OperationCanceledException)
        {
            LogRecheckFailed(services.GetRequiredService<ILogger<UserLog>>(), tenantId, exception.Message);
        }
    }

    [LoggerMessage(LogLevel.Warning, "The recheck of the users quota step of tenant {TenantId} failed. {Reason}")]
    private static partial void LogRecheckFailed(ILogger logger, string tenantId, string reason);
}
