using Axis.Data.Records;
using Axis.Processes.Work;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Runs a <c>computed.backfill</c> work item: the recompute of the stored computed values of one entity that
/// is pending, because activation left it to the worker or because work pinned to an older release wrote a
/// row after the last backfill ended. Each claim runs one batch of up to 500 out-of-date rows in one
/// transaction. It extends the lease and reads the entity's pending row without a lock. It recomputes the batch
/// with the application's active release, even when the pending row names an older one, and with the releases
/// whose fingerprint equals the active release's as the up-to-date ones. A batch that found rows makes the item
/// due at once, since the next batch selects the out-of-date rows again and needs no cursor. A batch that found
/// none locks the pending row, checks again, and deletes the row and the item if still no row is out of date.
/// A write of an older release that is still open holds the pending row, so it either makes the check find a
/// row, which reschedules the item, or finds the row gone and inserts a new one. The lock is held only for that
/// last check, because pinned writes lock the record row before the pending row. A row stays out of date when a
/// user changed it after the batch read it, so a later batch takes it. Without a pending row, or when the active
/// release has no such entity, the item has nothing left to do and is deleted.
/// </summary>
internal sealed partial class ComputedBackfillHandler(ReleaseModelCache models, ILogger<ComputedBackfillHandler> logger) : IExternalWorkItemHandler
{
    public string Kind => ComputedBackfill.WorkItemKind;

    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(connection);

        var entityId = item.EntityId ?? throw new InvalidOperationException($"The work item '{item.Id}' names no entity.");
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await WorkItemQueue.ExtendLeaseAsync(connection, transaction, item, item.Lease, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, entityId);
            return WorkItemOutcome.LostClaim;
        }

        Guid applicationId;
        Guid storedTarget;
        Guid[] storedCurrent;
        await using (var read = new NpgsqlCommand(
            "SELECT application_id, target_release_id, current_release_ids FROM axis.pending_computed_fields WHERE entity_id = @entity",
            connection,
            transaction))
        {
            read.Parameters.AddWithValue("entity", entityId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.CloseAsync();
                return await CompleteAsync(item, connection, transaction, entityId, cancellationToken);
            }

            applicationId = reader.GetGuid(0);
            storedTarget = reader.GetGuid(1);
            storedCurrent = reader.GetFieldValue<Guid[]>(2);
        }

        var (targetReleaseId, currentReleaseIds) = await ComputedBackfill.ResolveTargetAsync(
            connection, transaction, entityId, applicationId, storedTarget, storedCurrent, cancellationToken);
        var target = await models.GetAsync(new WorkItemContext(item, connection, transaction), targetReleaseId, cancellationToken);
        if (target.FindEntity(entityId) is not { } entity)
        {
            LogNoEntity(entityId, targetReleaseId);
            await ComputedBackfill.DeleteAsync(connection, transaction, entityId, cancellationToken);
            return await CompleteAsync(item, connection, transaction, entityId, cancellationToken);
        }

        var found = await ComputedBackfill.RunBatchAsync(connection, transaction, target, entity, currentReleaseIds, targetReleaseId, cancellationToken);
        if (found == 0 && await ComputedBackfill.TryEndAsync(connection, transaction, entity, cancellationToken))
        {
            return await CompleteAsync(item, connection, transaction, entityId, cancellationToken);
        }

        if (!await WorkItemQueue.RescheduleAsync(connection, transaction, item, TimeSpan.Zero, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, entityId);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
        return WorkItemOutcome.Rescheduled;
    }

    private async Task<WorkItemOutcome> CompleteAsync(
        ClaimedWorkItem item,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            LogLostClaim(item.Id, entityId);
            return WorkItemOutcome.LostClaim;
        }

        await transaction.CommitAsync(cancellationToken);
        return WorkItemOutcome.Completed;
    }

    [LoggerMessage(LogLevel.Warning, "Computed backfill work item {ItemId} for entity {EntityId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, Guid entityId);

    [LoggerMessage(LogLevel.Warning, "Computed backfill for entity {EntityId} ended without a batch: release {ReleaseId} has no such entity.")]
    private partial void LogNoEntity(Guid entityId, Guid releaseId);
}
