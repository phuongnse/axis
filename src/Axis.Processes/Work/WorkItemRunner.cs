using Axis.Core.Tenancy;
using Axis.Processes.Storage;
using Axis.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.Processes.Work;

/// <summary>
/// Claims work items of the registered handler kinds and runs each in one transaction under the
/// item's tenant context. The transaction commits only while the claim is still current, so a
/// worker that lost its claim commits nothing.
/// </summary>
public sealed partial class WorkItemRunner
{
    private readonly ITenantConnectionFactory _connections;
    private readonly ITenantContextAccessor _accessor;
    private readonly ILogger<WorkItemRunner> _logger;
    private readonly Dictionary<string, IWorkItemHandler> _handlers = new(StringComparer.Ordinal);
    private readonly string[] _kinds;

    /// <exception cref="InvalidOperationException">Two handlers have the same kind.</exception>
    public WorkItemRunner(
        ITenantConnectionFactory connections,
        ITenantContextAccessor accessor,
        IEnumerable<IWorkItemHandler> handlers,
        ILogger<WorkItemRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _connections = connections;
        _accessor = accessor;
        _logger = logger;
        foreach (var handler in handlers)
        {
            if (!_handlers.TryAdd(handler.Kind, handler))
            {
                throw new InvalidOperationException($"More than one work item handler is registered for kind '{handler.Kind}'.");
            }
        }

        _kinds = [.. _handlers.Keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Returns whether every processes migration is applied to the tenant database. A database
    /// without the migrations history table has every migration pending.
    /// </summary>
    public async Task<bool> IsMigratedAsync(string tenantId, CancellationToken cancellationToken)
    {
        var previous = _accessor.Current;
        _accessor.Current = new TenantContext(tenantId);
        try
        {
            await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
            await using var context = new ProcessesDbContext(new DbContextOptionsBuilder<ProcessesDbContext>().UseNpgsql(connection).Options);
            return !(await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any();
        }
        finally
        {
            _accessor.Current = previous;
        }
    }

    /// <summary>
    /// Claims the next due item of the tenant whose kind has a handler, for <paramref name="lease"/>.
    /// Returns <see langword="null"/> when there is none.
    /// </summary>
    public async Task<ClaimedWorkItem?> ClaimAsync(string tenantId, TimeSpan lease, CancellationToken cancellationToken)
    {
        if (_kinds.Length == 0)
        {
            return null;
        }

        var previous = _accessor.Current;
        _accessor.Current = new TenantContext(tenantId);
        try
        {
            await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
            return await WorkItemQueue.ClaimAsync(connection, tenantId, _kinds, lease, cancellationToken);
        }
        finally
        {
            _accessor.Current = previous;
        }
    }

    /// <summary>
    /// Runs a claimed item. When the handler succeeds, its writes commit together with the item's
    /// deletion. When it throws, its writes roll back, and a following transaction deletes the item
    /// and calls the handler's failure callback. Both commit only if the claim is still current.
    /// Cancellation rolls back with no failure callback, so the item is claimed again after its lease.
    /// </summary>
    /// <exception cref="InvalidOperationException">No handler is registered for the item's kind.</exception>
    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_handlers.TryGetValue(item.Kind, out var handler))
        {
            throw new InvalidOperationException($"No work item handler is registered for kind '{item.Kind}'.");
        }

        var previous = _accessor.Current;
        _accessor.Current = new TenantContext(item.TenantId);
        try
        {
            await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
            Exception? error = null;
            await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
            {
                try
                {
                    await handler.HandleAsync(new WorkItemContext(item, connection, transaction), cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    error = exception;
                }

                if (error is null)
                {
                    if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        LogLostClaim(item.Id, item.Kind, item.TenantId);
                        return WorkItemOutcome.LostClaim;
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return WorkItemOutcome.Completed;
                }

                await transaction.RollbackAsync(cancellationToken);
            }

            LogFailed(error, item.Id, item.Kind, item.TenantId);

            // An exception from here on rolls back as the transaction is disposed, so the item stays
            // claimed until its lease expires.
            await using var failure = await connection.BeginTransactionAsync(cancellationToken);
            if (!await WorkItemQueue.CompleteAsync(connection, failure, item, cancellationToken))
            {
                await failure.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, item.Kind, item.TenantId);
                return WorkItemOutcome.LostClaim;
            }

            await handler.OnFailedAsync(new WorkItemContext(item, connection, failure), error, cancellationToken);
            await failure.CommitAsync(cancellationToken);
            return WorkItemOutcome.Failed;
        }
        finally
        {
            _accessor.Current = previous;
        }
    }

    /// <summary>Claims and runs the next item of the tenant. Returns <see langword="false"/> when nothing was claimed.</summary>
    public async Task<bool> RunNextAsync(string tenantId, TimeSpan lease, CancellationToken cancellationToken)
    {
        var item = await ClaimAsync(tenantId, lease, cancellationToken);
        if (item is null)
        {
            return false;
        }

        await ExecuteAsync(item, cancellationToken);
        return true;
    }

    [LoggerMessage(LogLevel.Warning, "Work item {ItemId} of kind {Kind} for tenant {TenantId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, string kind, string tenantId);

    [LoggerMessage(LogLevel.Error, "Work item {ItemId} of kind {Kind} for tenant {TenantId} failed. Its writes were rolled back.")]
    private partial void LogFailed(Exception exception, Guid itemId, string kind, string tenantId);
}
