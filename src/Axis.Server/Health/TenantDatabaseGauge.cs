using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Axis.Core.Telemetry;

namespace Axis.Server.Health;

/// <summary>
/// The gauge of whether each tenant database answered the last readiness probe. The probe records its
/// result here, so a metrics scrape only reads memory and never queries a tenant database. A tenant
/// reports no point until the first probe has run. The meter belongs to the host's meter factory.
/// </summary>
internal sealed class TenantDatabaseGauge
{
    private readonly ConcurrentDictionary<string, bool> _answered = new(StringComparer.Ordinal);

    public TenantDatabaseGauge(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(AxisMetrics.MeterName);
        meter.CreateObservableGauge<long>(
            AxisMetrics.TenantDatabaseAvailable,
            Measure,
            unit: null,
            description: "Whether the tenant database answered the last readiness probe.");
    }

    /// <summary>Keeps whether the database of <paramref name="tenant"/> answered the probe that just ran.</summary>
    public void Record(string tenant, bool answered) => _answered[tenant] = answered;

    private IEnumerable<Measurement<long>> Measure()
    {
        foreach (var (tenant, answered) in _answered)
        {
            yield return new Measurement<long>(answered ? 1 : 0, new KeyValuePair<string, object?>(AxisTelemetry.TenantTag, tenant));
        }
    }
}
