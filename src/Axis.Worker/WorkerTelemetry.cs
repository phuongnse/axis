using System.Diagnostics;
using Axis.Core.Telemetry;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Axis.Worker;

internal static class WorkerTelemetry
{
    /// <summary>
    /// Traces the worker's outgoing HTTP calls, PostgreSQL commands and Axis spans, and sends logs through
    /// the OpenTelemetry logging provider. Traces are exported over OTLP only when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> or <c>OTEL_EXPORTER_OTLP_TRACES_ENDPOINT</c> is set, and logs
    /// only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> or <c>OTEL_EXPORTER_OTLP_LOGS_ENDPOINT</c> is set.
    /// <c>OTEL_SERVICE_NAME</c> overrides the service name.
    /// </summary>
    public static IServiceCollection AddWorkerTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var exportTraces = !string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
            || !string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"]);
        var exportLogs = !string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
            || !string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"]);

        // Each log entry carries the trace and span ids of the current activity in its scope.
        services.Configure<LoggerFactoryOptions>(options =>
            options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
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
            .WithLogging(
                logging =>
                {
                    if (exportLogs)
                    {
                        logging.AddOtlpExporter();
                    }
                },
                options => options.IncludeScopes = true);
        return services;
    }
}

/// <summary>Removes the bound address from outgoing HTTP spans when they end, before an exporter sees them.</summary>
internal sealed class HttpClientSpanRedaction : BaseProcessor<Activity>
{
    public override void OnEnd(Activity data) => AxisTelemetry.RedactHttpClientSpan(data);
}
