using Axis.Core.Tenancy;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Npgsql;

namespace Axis.Server.Tenancy;

/// <summary>Marks an endpoint that refuses a tenant over its storage quota, so a test can find the endpoints that do.</summary>
internal sealed record StorageQuotaMetadata;

internal static partial class StorageQuota
{
    /// <summary>
    /// Refuses the request with the quota <c>403</c> when, in a cell, the tenant's latest storage sample is at or over
    /// its storage quota, see <c>docs/reference/tenant-quotas.md#refusals</c>. The filter runs before the handler, so a
    /// refused request reads no body and writes nothing, not even an audit record. A tenant with no limit, a tenant with
    /// no sample yet and a server outside a cell are never refused. When the platform database cannot be read, the
    /// request goes on and a warning is logged.
    /// </summary>
    public static TBuilder WithStorageQuota<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint => endpoint.Metadata.Add(new StorageQuotaMetadata()));
        builder.AddEndpointFilterFactory((_, next) => async invocation =>
        {
            var context = invocation.HttpContext;
            var services = context.RequestServices;
            if (services.GetRequiredService<ITenantContextAccessor>().Current?.TenantId is not { } tenantId
                || services.GetRequiredService<TenantQuotas>().Limit(tenantId, QuotaKeys.Storage) is not { } limit)
            {
                return await next(invocation);
            }

            var loggers = services.GetRequiredService<ILoggerFactory>();
            long? used;
            try
            {
                used = await services.GetRequiredService<TenantUsageStore>().LatestStorageBytesAsync(tenantId, context.RequestAborted);
            }
            catch (NpgsqlException)
            {
                ReadFailed(loggers.CreateLogger(typeof(StorageQuota).FullName!), tenantId);
                return await next(invocation);
            }

            if (used is not { } bytes || bytes < limit)
            {
                return await next(invocation);
            }

            QuotaRefusal.Log(loggers, tenantId, QuotaKeys.Storage);
            return QuotaRefusal.Problem(QuotaKeys.Storage, limit, bytes);
        });
        return builder;
    }

    [LoggerMessage(LogLevel.Warning, "The storage sample of tenant {TenantId} could not be read, so its write is not checked against the storage quota.")]
    private static partial void ReadFailed(ILogger logger, string tenantId);
}
