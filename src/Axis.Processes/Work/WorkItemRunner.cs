using System.Diagnostics;
using Axis.Core.Telemetry;
using Axis.Core.Tenancy;
using Axis.Processes.Storage;
using Axis.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Axis.Processes.Work;

/// <summary>
/// Claims work items of the registered handler kinds and runs each under the item's tenant context.
/// A <see cref="IWorkItemHandler"/> runs in one transaction, which commits only while the claim is
/// still current, so a worker that lost its claim commits nothing. An
/// <see cref="IExternalWorkItemHandler"/> runs with no transaction open, and checks the claim in
/// each transaction it opens.
/// </summary>
public sealed partial class WorkItemRunner
{
    private readonly ITenantConnectionFactory _connections;
    private readonly ITenantContextAccessor _accessor;
    private readonly ILogger<WorkItemRunner> _logger;
    private readonly Dictionary<string, IWorkItemHandler> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IExternalWorkItemHandler> _externalHandlers = new(StringComparer.Ordinal);
    private readonly string[] _kinds;

    /// <exception cref="InvalidOperationException">Two handlers of either sort have the same kind.</exception>
    public WorkItemRunner(
        ITenantConnectionFactory connections,
        ITenantContextAccessor accessor,
        IEnumerable<IWorkItemHandler> handlers,
        ILogger<WorkItemRunner> logger,
        IEnumerable<IExternalWorkItemHandler>? externalHandlers = null)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _connections = connections;
        _accessor = accessor;
        _logger = logger;
        foreach (var handler in handlers)
        {
            if (!_handlers.TryAdd(handler.Kind, handler))
            {
                throw DuplicateKind(handler.Kind);
            }
        }

        foreach (var handler in externalHandlers ?? [])
        {
            if (_handlers.ContainsKey(handler.Kind) || !_externalHandlers.TryAdd(handler.Kind, handler))
            {
                throw DuplicateKind(handler.Kind);
            }
        }

        _kinds = [.. _handlers.Keys.Concat(_externalHandlers.Keys).Order(StringComparer.Ordinal)];
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
    /// An external handler gets the open connection with no transaction, and reports its own outcome.
    /// </summary>
    /// <exception cref="InvalidOperationException">No handler is registered for the item's kind.</exception>
    public async Task<WorkItemOutcome> ExecuteAsync(ClaimedWorkItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var start = Stopwatch.GetTimestamp();
        _handlers.TryGetValue(item.Kind, out var handler);
        IExternalWorkItemHandler? externalHandler = null;
        if (handler is null && !_externalHandlers.TryGetValue(item.Kind, out externalHandler))
        {
            throw new InvalidOperationException($"No work item handler is registered for kind '{item.Kind}'.");
        }

        // The run starts a new trace, so it does not nest under whatever span the caller has. It links to the span that queued the item.
        var previousActivity = Activity.Current;
        Activity.Current = null;
        using var activity = AxisTelemetry.Source.StartActivity(
            AxisTelemetry.WorkItemRun,
            ActivityKind.Internal,
            default(ActivityContext),
            [new(AxisTelemetry.TenantTag, item.TenantId), new(AxisTelemetry.WorkItemKindTag, item.Kind)],
            item.QueuedBy is { } queuedBy ? [new ActivityLink(queuedBy)] : null);
        var previous = _accessor.Current;
        _accessor.Current = new TenantContext(item.TenantId);
        try
        {
            await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
            if (handler is null)
            {
                return Finish(activity, item, start, await externalHandler!.ExecuteAsync(item, connection, cancellationToken));
            }

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
                    AxisTelemetry.RecordFailure(activity, exception);
                }

                if (error is null)
                {
                    if (!await WorkItemQueue.CompleteAsync(connection, transaction, item, cancellationToken))
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        LogLostClaim(item.Id, item.Kind, item.TenantId);
                        return Finish(activity, item, start, WorkItemOutcome.LostClaim);
                    }

                    await transaction.CommitAsync(cancellationToken);
                    return Finish(activity, item, start, WorkItemOutcome.Completed);
                }

                await transaction.RollbackAsync(cancellationToken);
            }

            // The exception type only: its message and inner exceptions can quote record values.
            LogFailed(item.Id, item.Kind, item.TenantId, error.GetType().FullName ?? error.GetType().Name);

            // An exception from here on rolls back as the transaction is disposed, so the item stays
            // claimed until its lease expires.
            await using var failure = await connection.BeginTransactionAsync(cancellationToken);
            if (!await WorkItemQueue.CompleteAsync(connection, failure, item, cancellationToken))
            {
                await failure.RollbackAsync(cancellationToken);
                LogLostClaim(item.Id, item.Kind, item.TenantId);
                return Finish(activity, item, start, WorkItemOutcome.LostClaim);
            }

            await handler.OnFailedAsync(new WorkItemContext(item, connection, failure), error, cancellationToken);
            await failure.CommitAsync(cancellationToken);
            return Finish(activity, item, start, WorkItemOutcome.Failed);
        }
        catch (Exception exception)
        {
            AxisTelemetry.RecordFailure(activity, exception);
            throw;
        }
        finally
        {
            _accessor.Current = previous;

            // Stopping first leaves the caller's span current, whatever stopping sets.
            activity?.Stop();
            Activity.Current = previousActivity;
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

    private static WorkItemOutcome Finish(Activity? activity, ClaimedWorkItem item, long start, WorkItemOutcome outcome)
    {
        var name = outcome switch
        {
            WorkItemOutcome.Failed => "failed",
            WorkItemOutcome.LostClaim => "fenced",
            _ => "completed",
        };
        activity?.SetTag(AxisTelemetry.OutcomeTag, name);
        AxisMetrics.RecordWorkItem(item.TenantId, item.Kind, name, Stopwatch.GetElapsedTime(start));
        return outcome;
    }

    private static InvalidOperationException DuplicateKind(string kind) =>
        new($"More than one work item handler is registered for kind '{kind}'.");

    [LoggerMessage(LogLevel.Warning, "Work item {ItemId} of kind {Kind} for tenant {TenantId} was claimed by another worker, so its writes were rolled back.")]
    private partial void LogLostClaim(Guid itemId, string kind, string tenantId);

    [LoggerMessage(LogLevel.Error, "Work item {ItemId} of kind {Kind} for tenant {TenantId} failed with {ErrorType}. Its writes were rolled back.")]
    private partial void LogFailed(Guid itemId, string kind, string tenantId, string errorType);
}
