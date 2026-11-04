using System.Diagnostics.Metrics;

namespace Axis.Core.Telemetry;

/// <summary>
/// The worker's poll metrics: a gauge of the tenants in its active list and a counter of its polls. Neither has a tenant
/// attribute, so the cost does not grow with the number of tenants. The meter belongs to the host's meter factory, so a host
/// counts only its own polls.
/// </summary>
public sealed class WorkerPollMetrics
{
    private readonly Counter<long> _polls;
    private Func<int>? _activeTenants;

    public WorkerPollMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(AxisMetrics.MeterName);
        meter.CreateObservableGauge<long>(
            AxisMetrics.WorkerActiveTenants,
            ObserveActive,
            unit: "{tenant}",
            description: "The tenants in the active list of the worker.");
        _polls = meter.CreateCounter<long>(
            AxisMetrics.WorkerPolls,
            "{poll}",
            "The polls of a tenant by the worker, by whether the poll found work due.");
    }

    /// <summary>Sets where the gauge reads the length of the active list. It reports nothing until then.</summary>
    public void ObserveActiveTenants(Func<int> count) => _activeTenants = count;

    /// <summary>Counts one poll of a tenant. <paramref name="found"/> is whether the tenant had work due.</summary>
    public void RecordPoll(bool found) =>
        _polls.Add(1, new KeyValuePair<string, object?>(AxisTelemetry.PollFoundTag, found));

    private IEnumerable<Measurement<long>> ObserveActive()
    {
        if (_activeTenants is { } count)
        {
            yield return new Measurement<long>(count());
        }
    }
}
