using System.Diagnostics;
using Axis.Core.Telemetry;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Axis.Worker;

internal static class WorkerTracing
{
    /// <summary>
    /// Traces the worker's outgoing HTTP calls, PostgreSQL commands and Axis spans. Traces are exported
    /// over OTLP only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> or <c>OTEL_EXPORTER_OTLP_TRACES_ENDPOINT</c>
    /// is set. <c>OTEL_SERVICE_NAME</c> overrides the service name.
    /// </summary>
    public static IServiceCollection AddWorkerTracing(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var exportTraces = !string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
            || !string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"]);
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
            });
        return services;
    }
}

/// <summary>Removes the bound address from outgoing HTTP spans when they end, before an exporter sees them.</summary>
internal sealed class HttpClientSpanRedaction : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data) => AxisTelemetry.RedactHttpClientSpan(data);
}
