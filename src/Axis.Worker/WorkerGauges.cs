using Axis.Core.Telemetry;
using Axis.Core.Tenancy;
using Axis.Data.Records;
using Axis.Processes.Work;
using Axis.Tenancy;

namespace Axis.Worker;

/// <summary>
/// Reads the due work items and the pending computed fields of every migrated tenant database and
/// publishes them to <see cref="AxisGauges"/>. A kind the worker handles and an application with an
/// active release report 0 when nothing is due or pending. A tenant whose database cannot be read
/// reports no data until a later read succeeds, so old values never pass for healthy queues.
/// </summary>
public sealed partial class WorkerGauges(
    ITenantCatalog tenants,
    WorkItemRunner runner,
    ITenantConnectionFactory connections,
    ITenantContextAccessor accessor,
    AxisGauges gauges,
    ILogger<WorkerGauges> logger)
{
    // Tenants whose database was migrated. A new schema arrives with a new release, which restarts the worker.
    private readonly HashSet<string> _migrated = new(StringComparer.Ordinal);

    /// <summary>Reads every migrated tenant once. A tenant that fails is logged and does not stop the others.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        // Read at each refresh, because under the directory source the tenants arrive after the first load.
        foreach (var tenantId in tenants.Tenants.Select(tenant => tenant.Id).ToArray())
        {
            try
            {
                if (!_migrated.Contains(tenantId))
                {
                    if (!await runner.IsMigratedAsync(tenantId, cancellationToken))
                    {
                        continue;
                    }

                    _migrated.Add(tenantId);
                }

                await RefreshTenantAsync(tenantId, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                gauges.Remove(tenantId);
                LogReadFailed(exception, tenantId);
            }
        }
    }

    private async Task RefreshTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkItemGauge> due;
        IReadOnlyList<PendingFieldsGauge> pending;
        var previous = accessor.Current;
        accessor.Current = new TenantContext(tenantId);
        try
        {
            await using var connection = await connections.OpenConnectionAsync(cancellationToken);
            due = await WorkItemQueue.ReadDueAsync(connection, tenantId, cancellationToken);
            pending = await PendingComputedFields.CountByApplicationAsync(connection, cancellationToken);
        }
        finally
        {
            accessor.Current = previous;
        }

        var found = due.Select(row => row.Kind).ToHashSet(StringComparer.Ordinal);
        var workItems = runner.Kinds.Where(kind => !found.Contains(kind))
            .Select(kind => new WorkItemGauge(kind, 0, 0))
            .Concat(due)
            .ToList();
        gauges.Publish(tenantId, workItems, pending);
    }

    [LoggerMessage(LogLevel.Warning, "Reading the gauges of tenant {TenantId} failed.")]
    private partial void LogReadFailed(Exception exception, string tenantId);
}
