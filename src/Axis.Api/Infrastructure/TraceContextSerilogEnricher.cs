using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace Axis.Api.Infrastructure;

/// <summary>
/// Adds W3C trace/span identifiers to Serilog events so Loki queries can correlate logs with Tempo traces.
/// </summary>
internal sealed class TraceContextSerilogEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        Activity? activity = Activity.Current;
        if (activity is null)
            return;

        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
