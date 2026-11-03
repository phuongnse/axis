using Axis.Core.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

namespace Axis.Admin;

/// <summary>The OpenTelemetry export of the metrics that <c>cell backup</c> records.</summary>
internal static class CellBackupMetrics
{
    /// <summary>
    /// Starts a meter provider that exports the Axis metrics over OTLP, and returns <see langword="null"/> when neither
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> nor <c>OTEL_EXPORTER_OTLP_METRICS_ENDPOINT</c> is set. The exporter reads its settings from
    /// <paramref name="configuration"/>, and <c>OTEL_SERVICE_NAME</c> defaults to <c>axis-admin</c>. Disposing the provider flushes the export.
    /// </summary>
    public static MeterProvider? Start(IConfiguration configuration)
    {
        if (string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
            && string.IsNullOrEmpty(configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"]))
        {
            return null;
        }

        return Sdk.CreateMeterProviderBuilder()
            .ConfigureServices(services => services.AddSingleton(configuration))
            .ConfigureResource(resource => resource.AddService(configuration["OTEL_SERVICE_NAME"] ?? "axis-admin"))
            .AddMeter(AxisMetrics.MeterName)
            .AddOtlpExporter()
            .Build();
    }
}
