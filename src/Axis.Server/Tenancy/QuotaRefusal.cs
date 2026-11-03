using Axis.Tenancy;

namespace Axis.Server.Tenancy;

/// <summary>
/// The answer and the log entry for a request that a tenant's quota refuses, see <c>docs/reference/tenant-quotas.md#refusals</c>.
/// Only the quotas that refuse have a wording here. The lease quotas wait instead, and emails are held.
/// </summary>
internal static partial class QuotaRefusal
{
    /// <summary>
    /// The <c>403</c> problem details with the <c>reason</c> <c>quota-exceeded</c>, the <paramref name="quota"/> key, the
    /// <paramref name="limit"/> and what the tenant has <paramref name="used"/>. The title never names the tenant.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="quota"/> is not <c>storage</c>, <c>users</c> or <c>applications</c>.</exception>
    public static IResult Problem(string quota, long limit, long used)
    {
        var words = quota switch
        {
            QuotaKeys.Storage => "storage",
            QuotaKeys.Users => "users",
            QuotaKeys.Applications => "applications",
            _ => throw new ArgumentException($"'{quota}' is not a quota that refuses.", nameof(quota)),
        };
        return Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: $"This tenant has reached its {words} quota.",
            extensions: new Dictionary<string, object?>
            {
                ["reason"] = "quota-exceeded",
                ["quota"] = quota,
                ["limit"] = limit,
                ["used"] = used,
            });
    }

    /// <summary>Writes the <c>quota.refused</c> warning. It holds the tenant id and the quota key and never text from the request.</summary>
    public static void Log(ILoggerFactory loggers, string tenantId, string quota) =>
        Refused(loggers.CreateLogger(typeof(QuotaRefusal).FullName!), tenantId, quota);

    [LoggerMessage(EventName = "quota.refused", Level = LogLevel.Warning, Message = "quota.refused: tenant {TenantId} reached its {Quota} quota.")]
    private static partial void Refused(ILogger logger, string tenantId, string quota);
}
