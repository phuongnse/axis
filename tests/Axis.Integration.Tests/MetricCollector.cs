using System.Collections;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Axis.Integration.Tests;

/// <summary>
/// Collects the Axis metrics of the whole test process with an in-memory exporter. The fixtures build
/// the worker as a bare service provider that never starts, so its own meter provider does not run,
/// and a test needs this one. Tests share the process, so a test looks for a point with the tags of
/// its own and never counts points.
/// </summary>
internal sealed class MetricCollector : IDisposable
{
    private readonly LockedList _metrics = new();
    private readonly MeterProvider _provider;

    public MetricCollector() =>
        _provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter("Axis")
            .AddInMemoryExporter(_metrics)
            .Build();

    /// <summary>The metrics exported so far, flushed first.</summary>
    public IReadOnlyList<MetricSnapshot> Metrics
    {
        get
        {
            _provider.ForceFlush();
            return _metrics.Snapshot();
        }
    }

    /// <summary>Returns whether a point of <paramref name="name"/> has every one of <paramref name="tags"/>.</summary>
    public bool HasPoint(string name, params (string Key, string Value)[] tags) =>
        Metrics.Where(metric => metric.Name == name)
            .SelectMany(metric => metric.MetricPoints)
            .Any(point => Matches(point, tags));

    /// <summary>
    /// The value of the latest gauge point of <paramref name="name"/> that has every one of
    /// <paramref name="tags"/>, or <see langword="null"/> when there is none.
    /// </summary>
    public double? GaugeValue(string name, params (string Key, string Value)[] tags)
    {
        double? value = null;
        foreach (var metric in Metrics.Where(metric => metric.Name == name))
        {
            foreach (var point in metric.MetricPoints)
            {
                if (Matches(point, tags))
                {
                    value = metric.MetricType == MetricType.LongGauge ? point.GetGaugeLastValueLong() : point.GetGaugeLastValueDouble();
                }
            }
        }

        return value;
    }

    private static bool Matches(MetricPoint point, (string Key, string Value)[] tags)
    {
        var pointTags = new Dictionary<string, object?>();
        foreach (var tag in point.Tags)
        {
            pointTags[tag.Key] = tag.Value;
        }

        return tags.All(tag => pointTags.TryGetValue(tag.Key, out var value) && Equals(value, tag.Value));
    }

    public void Dispose() => _provider.Dispose();

    private sealed class LockedList : ICollection<MetricSnapshot>
    {
        private readonly List<MetricSnapshot> _items = [];
        private readonly Lock _lock = new();

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _items.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        public List<MetricSnapshot> Snapshot()
        {
            lock (_lock)
            {
                return [.. _items];
            }
        }

        public void Add(MetricSnapshot item)
        {
            lock (_lock)
            {
                _items.Add(item);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _items.Clear();
            }
        }

        public bool Contains(MetricSnapshot item) => Snapshot().Contains(item);

        public void CopyTo(MetricSnapshot[] array, int arrayIndex) => Snapshot().CopyTo(array, arrayIndex);

        public bool Remove(MetricSnapshot item)
        {
            lock (_lock)
            {
                return _items.Remove(item);
            }
        }

        public IEnumerator<MetricSnapshot> GetEnumerator() => Snapshot().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
