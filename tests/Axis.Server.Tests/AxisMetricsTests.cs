using System.Diagnostics.Metrics;
using Axis.Core.Telemetry;
using Axis.Server.Health;
using Axis.Server.Http;
using Axis.Server.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Server.Tests;

/// <summary>
/// Each instrument of the <c>Axis</c> meter records only the attribute keys that the
/// <see href="../../docs/reference/process-execution.md#worker-metrics">worker metrics</see> table lists for it, or the
/// <see href="../../docs/architecture.md#health-checks">health checks</see> section for the server's gauge.
/// </summary>
public sealed class AxisMetricsTests
{
    // The table in docs/reference/process-execution.md and the health checks section of docs/architecture.md, as metric name to the attribute keys it may carry.
    private static readonly Dictionary<string, string[]> _allowed = new(StringComparer.Ordinal)
    {
        ["axis.work_items.duration"] = ["axis.tenant", "axis.work_item.kind", "axis.outcome"],
        ["axis.process.instances.ended"] = ["axis.tenant", "axis.application", "axis.process", "axis.outcome"],
        ["axis.work_items.due"] = ["axis.tenant", "axis.work_item.kind"],
        ["axis.work_items.oldest_due_age"] = ["axis.tenant", "axis.work_item.kind"],
        ["axis.computed.pending_fields"] = ["axis.tenant", "axis.application"],
        ["axis.tenant.database.available"] = ["axis.tenant"],
        ["axis.rate_limits.refused"] = ["axis.partition"],
        ["axis.calls.attempts"] = ["axis.tenant", "axis.application", "axis.connector", "axis.package", "axis.operation", "axis.outcome"],
    };

    [Fact]
    public void Each_instrument_records_only_the_attribute_keys_the_design_lists_for_it()
    {
        var keys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var published = new HashSet<string>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == AxisMetrics.MeterName)
            {
                published.Add(instrument.Name);
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>(Record);
        listener.SetMeasurementEventCallback<double>(Record);
        listener.Start();
        using var gauges = new AxisGauges();
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var tenantDatabases = new TenantDatabaseGauge(services.GetRequiredService<IMeterFactory>());
        var rateLimitRefusals = new RateLimitRefusals(services.GetRequiredService<IMeterFactory>());

        void Record<T>(Instrument instrument, T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
            where T : struct
        {
            lock (keys)
            {
                if (!keys.TryGetValue(instrument.Name, out var set))
                {
                    keys[instrument.Name] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                foreach (var tag in tags)
                {
                    set.Add(tag.Key);
                }
            }
        }

        AxisMetrics.RecordWorkItem("default", "process.step", "completed", TimeSpan.FromMilliseconds(5));
        AxisMetrics.RecordInstanceEnded("default", "PurchaseRequests", "PurchaseRequestApproval", "completed");
        AxisMetrics.RecordInstanceEnded("default", null, null, "failed");
        AxisMetrics.RecordCallAttempt("default", "PurchaseRequests", AxisTelemetry.ConnectorTag, "PurchasingSystem", "createOrder", "succeeded");
        AxisMetrics.RecordCallAttempt("default", null, AxisTelemetry.PackageTag, "PaymentReferences", "reserve", "retryable");

        gauges.Publish("default", [new WorkItemGauge("process.step", 3, 12.5)], [new PendingFieldsGauge("PurchaseRequests", 2)]);
        tenantDatabases.Record("default", true);
        rateLimitRefusals.Record(Partition.Session);
        listener.RecordObservableInstruments();

        Assert.Equal([.. _allowed.Keys.Order(StringComparer.Ordinal)], [.. published.Order(StringComparer.Ordinal)]);
        Assert.Equal([.. _allowed.Keys.Order(StringComparer.Ordinal)], [.. keys.Keys.Order(StringComparer.Ordinal)]);
        foreach (var (metric, recorded) in keys)
        {
            Assert.Empty(recorded.Except(_allowed[metric], StringComparer.Ordinal));
        }
    }

    [Fact]
    public void A_call_attempt_names_its_target_by_the_connector_or_package_tag_only()
    {
        Assert.Throws<ArgumentException>(() => AxisMetrics.RecordCallAttempt("default", null, "axis.step", "x", "y", "succeeded"));
    }
}
