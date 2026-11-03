using System.Diagnostics;
using Axis.Core.Tenancy;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Npgsql;

namespace Axis.Server.Tenancy;

/// <summary>
/// Takes the <c>concurrentQueries</c> lease of the tenant for a record list read or a data source rows read, see
/// <c>docs/reference/tenant-quotas.md#work-at-once</c>. When none is free the read waits, retrying every 250 ms, for up
/// to five seconds, and then answers <c>503</c>. Outside a cell, or for a tenant with no limit, no lease is taken and the
/// platform database is not read. When the platform database cannot be reached the read goes ahead without a lease and
/// the server logs a warning, as under <c>directory</c> the server keeps serving while the platform database is down.
/// </summary>
internal sealed partial class QueryLeases(
    TenantLeases leases,
    TenantQuotas quotas,
    ITenantContextAccessor tenants,
    ILogger<QueryLeases> logger)
{
    /// <summary>How long a read waits for a free lease.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    /// <summary>How long a waiting read lets pass between two tries.</summary>
    public static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Takes the lease, waiting for one when the tenant has none free. <see cref="QueryLeaseOutcome.Busy"/> is set when the wait
    /// ended with none. Dispose <see cref="QueryLeaseOutcome.Lease"/> when the read ends.
    /// </summary>
    public async Task<QueryLeaseOutcome> TakeAsync(CancellationToken cancellationToken)
    {
        if (tenants.Current?.TenantId is not { } tenantId || quotas.Limit(tenantId, QuotaKeys.ConcurrentQueries) is not { } limit)
        {
            return QueryLeaseOutcome.None;
        }

        var clock = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                if (await leases.TryTakeAsync(tenantId, QuotaKeys.ConcurrentQueries, limit, cancellationToken) is { } lease)
                {
                    return new QueryLeaseOutcome(false, lease);
                }

                var left = Wait - clock.Elapsed;
                if (left <= TimeSpan.Zero)
                {
                    return new QueryLeaseOutcome(true, NoLease.Instance);
                }

                await Task.Delay(left < Retry ? left : Retry, cancellationToken);
            }
        }
        catch (NpgsqlException exception)
        {
            LogUnavailable(tenantId, exception.GetType().Name);
            return QueryLeaseOutcome.None;
        }
    }

    /// <summary>The <c>503</c> answer of a read that found no free lease. It tells the caller to try again in a second.</summary>
    public static IResult Busy(HttpContext context)
    {
        context.Response.Headers.RetryAfter = "1";
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "This tenant is busy. Try again in a moment.");
    }

    [LoggerMessage(LogLevel.Warning, "A concurrentQueries lease of tenant {TenantId} could not be taken over ConnectionStrings:Platform ({Cause}). The read runs without one.")]
    private partial void LogUnavailable(string tenantId, string cause);

    private sealed class NoLease : IAsyncDisposable
    {
        public static readonly NoLease Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>The result of <see cref="TakeAsync"/>: <see cref="Busy"/>, or a <see cref="Lease"/> to dispose when the read ends.</summary>
    internal readonly record struct QueryLeaseOutcome(bool Busy, IAsyncDisposable Lease)
    {
        public static QueryLeaseOutcome None => new(false, NoLease.Instance);
    }
}
