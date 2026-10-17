using System.Diagnostics;
using Axis.Core.Telemetry;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Axis.Worker;

internal static class WorkerTelemetry
{
    /// <summary>
    /// Traces the worker's outgoing HTTP calls, PostgreSQL commands and Axis spans, and publishes the
    /// standard and Axis metrics. Traces are exported over OTLP only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>
    /// or <c>OTEL_EXPORTER_OTLP_TRACES_ENDPOINT</c> is set, and metrics only when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> or <c>OTEL_EXPORTER_OTLP_METRICS_ENDPOINT</c> is set.
    /// <c>OTEL_SERVICE_NAME</c> overrides the service name.
    /// </summary>
    public static IServiceCollection AddWorkerTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var exportTraces = !string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
            || !string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"]);
        var exportMetrics = !string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
            || !string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"]);
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(configuration["OTEL_SERVICE_NAME"] ?? "axis-worker"))
            .WithTracing(tracing =>
            {
                tracing.AddSource(AxisTelemetry.SourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddNpgsql()
                    .AddProcessor(new HttpClientSpanRedaction());
                if (exportTraces)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddStandardMetrics();
                if (exportMetrics)
                {
                    metrics.AddOtlpExporter();
                }
            });
        return services;
    }

    /// <summary>
    /// Publishes the Axis metrics, the .NET runtime metrics, and the ASP.NET Core, HttpClient and DNS
    /// metrics. HttpClient and DNS metrics keep only <see cref="AxisMetrics.HttpTagKeys"/>, so no bound
    /// address reaches an exporter.
    /// </summary>
    internal static MeterProviderBuilder AddStandardMetrics(this MeterProviderBuilder metrics) =>
        metrics.AddMeter(AxisMetrics.MeterName, "System.Runtime", "System.Net.Http", "System.Net.NameResolution")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddView(instrument => instrument.Meter.Name is "System.Net.Http" or "System.Net.NameResolution"
                ? new MetricStreamConfiguration { TagKeys = [.. AxisMetrics.HttpTagKeys] }
                : null);
}

/// <summary>Removes the bound address from outgoing HTTP spans when they end, before an exporter sees them.</summary>
internal sealed class HttpClientSpanRedaction : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data) => AxisTelemetry.RedactHttpClientSpan(data);
}
