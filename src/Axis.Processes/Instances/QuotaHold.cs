using System.Collections.Concurrent;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Holds a work item whose tenant used up a daily quota until 00:00 UTC of the next day, and stores the first hold's notice for the
/// tenant's administrators, see docs/reference/tenant-quotas.md#emails-a-day. The email handler and the package handler both hold through it,
/// each with the platform texts of its own notice.
/// </summary>
internal sealed partial class QuotaHold(IHeldNotices notices, string subjectKey, string bodyKey, ILogger logger)
{
    private readonly ConcurrentDictionary<string, byte> _warnedNotice = new(StringComparer.Ordinal);

    /// <summary>
    /// Makes the item due at 00:00 UTC of the day after <paramref name="day"/> in one transaction. The first hold of the day also stores the
    /// notice with <paramref name="limit"/> in that transaction, once it has taken the day's notice counter. Returns
    /// <see cref="WorkItemOutcome.LostClaim"/> when another worker holds the item now, so nothing was written.
    /// </summary>
    public async Task<WorkItemOutcome> HoldAsync(ClaimedWorkItem item, NpgsqlConnection connection, DateOnly day, long limit, CancellationToken cancellationToken)
    {
        var notice = false;
        try
        {
            notice = await notices.TakeHeldNoticeAsync(item.TenantId, day, cancellationToken);
        }
        catch (NpgsqlException)
        {
            // Holding the item is what the quota requires, so a missing notice is the smaller harm.
            if (_warnedNotice.TryAdd(item.TenantId, 0))
            {
                LogNoticeUnavailable(item.TenantId);
            }
        }

        var dueAt = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        try
        {
            await using var transaction = await WorkItemQueue.BeginAsync(connection, item, cancellationToken);
            if (!await WorkItemQueue.RescheduleAtAsync(connection, transaction, item, dueAt, cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id);
                if (notice)
                {
                    await ReturnNoticeAsync(item.TenantId, day, cancellationToken);
                }

                return WorkItemOutcome.LostClaim;
            }

            if (notice)
            {
                await TenantNotices.InsertHeldAsync(transaction, subjectKey, bodyKey, limit, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch when (notice)
        {
            await ReturnNoticeAsync(item.TenantId, day, CancellationToken.None);
            throw;
        }

        LogHeld(item.Id, item.Kind, item.TenantId, dueAt);
        return WorkItemOutcome.Rescheduled;
    }

    /// <summary>
    /// Gives back the notice counter of a hold whose transaction did not commit. When the platform database cannot be
    /// reached the counter stays taken, which is logged, and that day's notice is lost.
    /// </summary>
    private async Task ReturnNoticeAsync(string tenantId, DateOnly day, CancellationToken cancellationToken)
    {
        try
        {
            await notices.ReturnHeldNoticeAsync(tenantId, day, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            LogNoticeNotReturned(tenantId, exception.GetType().Name);
        }
    }

    [LoggerMessage(LogLevel.Warning, "The platform database could not be reached to count the held-notice of tenant {TenantId}, so the held work item stores no notice. This is logged once for the tenant.")]
    private partial void LogNoticeUnavailable(string tenantId);

    [LoggerMessage(LogLevel.Warning, "The held-notice counter of tenant {TenantId} was not given back, because the platform database failed with {Error}.")]
    private partial void LogNoticeNotReturned(string tenantId, string error);

    [LoggerMessage(LogLevel.Warning, "Work item {ItemId} was claimed by another worker, so its hold was rolled back.")]
    private partial void LogLostClaim(Guid itemId);

    [LoggerMessage(LogLevel.Information, "Work item {ItemId} of kind {Kind} of tenant {TenantId} is held, since a daily quota is used up. It is due at {DueAt:O}.")]
    private partial void LogHeld(Guid itemId, string kind, string tenantId, DateTimeOffset dueAt);
}
