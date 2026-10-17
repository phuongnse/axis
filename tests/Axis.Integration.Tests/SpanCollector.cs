using System.Collections;
using System.Diagnostics;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Axis.Integration.Tests;

/// <summary>
/// Collects the finished spans of the whole test process with an in-memory exporter: the Axis spans,
/// incoming requests, outgoing HTTP calls and Npgsql commands, as the hosts trace them. The fixtures
/// build the worker as a bare service provider that never starts, so its own tracer provider does not
/// run, and a test needs this one. It redacts outgoing HTTP spans like the hosts do. Tests share the
/// process, so a test filters the spans by a trace id or a marker value of its own.
/// </summary>
internal sealed class SpanCollector : IDisposable
{
    private readonly LockedList _spans = new();
    private readonly TracerProvider _provider;

    public SpanCollector() =>
        _provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("Axis")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddNpgsql()
            .AddProcessor(new Axis.Server.Telemetry.HttpClientSpanRedaction())
            .AddInMemoryExporter(_spans)
            .Build();

    /// <summary>The spans that finished so far.</summary>
    public IReadOnlyList<Activity> Spans
    {
        get
        {
            _provider.ForceFlush();
            return _spans.Snapshot();
        }
    }

    public void Dispose() => _provider.Dispose();

    private sealed class LockedList : ICollection<Activity>
    {
        private readonly List<Activity> _items = [];
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

        public List<Activity> Snapshot()
        {
            lock (_lock)
            {
                return [.. _items];
            }
        }

        public void Add(Activity item)
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

        public bool Contains(Activity item) => Snapshot().Contains(item);

        public void CopyTo(Activity[] array, int arrayIndex) => Snapshot().CopyTo(array, arrayIndex);

        public bool Remove(Activity item)
        {
            lock (_lock)
            {
                return _items.Remove(item);
            }
        }

        public IEnumerator<Activity> GetEnumerator() => Snapshot().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
