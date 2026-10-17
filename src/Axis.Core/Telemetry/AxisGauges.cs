using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Axis.Core.Telemetry;

/// <summary>One work item kind of a tenant: how many items are due and how long the oldest has been due.</summary>
public sealed record WorkItemGauge(string Kind, long Due, double OldestDueAgeSeconds);

/// <summary>One application of a tenant and the number of its pending computed field names.</summary>
public sealed record PendingFieldsGauge(string Application, long Fields);

/// <summary>
/// The gauges of due work items and pending computed fields. A worker reads the tenant databases on its
/// own schedule and publishes the result here, so a metrics scrape only reads memory and never queries a
/// tenant database. Each instance owns its meter and its points, so a stopped worker stops reporting.
/// </summary>
public sealed class AxisGauges : IDisposable
{
    private readonly Meter _meter = new(AxisMetrics.MeterName);
    private readonly ConcurrentDictionary<string, TenantGauges> _tenants = new(StringComparer.Ordinal);

    public AxisGauges()
    {
        _meter.CreateObservableGauge(
            AxisMetrics.WorkItemsDue,
            () => Measure(tenant => tenant.WorkItems.Select(row => (row.Kind, row.Due)), AxisTelemetry.WorkItemKindTag),
            "items",
            "Work items whose due time has passed.");
        _meter.CreateObservableGauge(
            AxisMetrics.WorkItemsOldestDueAge,
            () => Measure(tenant => tenant.WorkItems.Select(row => (row.Kind, row.OldestDueAgeSeconds)), AxisTelemetry.WorkItemKindTag),
            "s",
            "How long the oldest due work item has been due.");
        _meter.CreateObservableGauge(
            AxisMetrics.ComputedPendingFields,
            () => Measure(tenant => tenant.Applications.Select(row => (row.Application, row.Fields)), AxisTelemetry.ApplicationTag),
            "fields",
            "Computed field names that wait for a backfill.");
    }

    /// <summary>Replaces the points of <paramref name="tenant"/>.</summary>
    public void Publish(string tenant, IReadOnlyList<WorkItemGauge> workItems, IReadOnlyList<PendingFieldsGauge> applications)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(workItems);
        ArgumentNullException.ThrowIfNull(applications);
        _tenants[tenant] = new TenantGauges([.. workItems], [.. applications]);
    }

    /// <summary>Removes the points of <paramref name="tenant"/>, so it reports no data.</summary>
    public void Remove(string tenant) => _tenants.TryRemove(tenant, out _);

    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<T>> Measure<T>(Func<TenantGauges, IEnumerable<(string Name, T Value)>> rows, string tag)
        where T : struct
    {
        foreach (var (tenant, gauges) in _tenants)
        {
            foreach (var (name, value) in rows(gauges))
            {
                yield return new Measurement<T>(
                    value,
                    new KeyValuePair<string, object?>(AxisTelemetry.TenantTag, tenant),
                    new KeyValuePair<string, object?>(tag, name));
            }
        }
    }

    private sealed record TenantGauges(IReadOnlyList<WorkItemGauge> WorkItems, IReadOnlyList<PendingFieldsGauge> Applications);
}
