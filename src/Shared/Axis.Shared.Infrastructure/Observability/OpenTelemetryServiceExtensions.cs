using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Axis.Shared.Infrastructure.Observability;

public static class OpenTelemetryServiceExtensions
{
    /// <summary>
    /// Registers tracing, metrics, and optional log export per ADR-018.
    /// Call from <c>Program.cs</c> on the host builder before module infrastructure.
    /// </summary>
    public static IHostApplicationBuilder AddAxisOpenTelemetry(this IHostApplicationBuilder builder)
    {
        OpenTelemetryOptions options = builder.Configuration
            .GetSection(OpenTelemetryOptions.SectionName)
            .Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();

        if (!options.IsEnabled(builder.Environment))
            return builder;

        string serviceName = options.ServiceName;
        string serviceVersion = options.ServiceVersion
            ?? typeof(OpenTelemetryServiceExtensions).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(aspNet =>
                    {
                        aspNet.Filter = context => ShouldInstrumentHttpRequest(context.Request);
                        aspNet.RecordException = true;
                    })
                    .AddHttpClientInstrumentation(http =>
                    {
                        http.RecordException = true;
                    })
                    .AddEntityFrameworkCoreInstrumentation();

                if (options.Otlp.IsConfigured)
                    tracing.AddOtlpExporter(export => ConfigureOtlpExporter(export, options.Otlp));
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();

                if (options.Prometheus.Enabled)
                    metrics.AddPrometheusExporter();

                if (options.Otlp.IsConfigured)
                    metrics.AddOtlpExporter(export => ConfigureOtlpExporter(export, options.Otlp));
            });

        if (options.ExportLogs && options.Otlp.IsConfigured)
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
                logging.ParseStateValues = true;
                logging.AddOtlpExporter(export => ConfigureOtlpExporter(export, options.Otlp));
            });
        }

        builder.Services.AddSingleton(options);
        return builder;
    }

    /// <summary>
    /// Maps the Prometheus scrape endpoint when enabled. Call after <see cref="WebApplication"/> is built.
    /// </summary>
    public static WebApplication UseAxisOpenTelemetry(this WebApplication app)
    {
        OpenTelemetryOptions options = app.Configuration
            .GetSection(OpenTelemetryOptions.SectionName)
            .Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();

        if (!options.IsEnabled(app.Environment))
            return app;

        if (options.Prometheus.Enabled)
            app.UseOpenTelemetryPrometheusScrapingEndpoint(options.Prometheus.ScrapeEndpointPath);

        return app;
    }

    private static bool ShouldInstrumentHttpRequest(HttpRequest request)
    {
        PathString path = request.Path;
        if (path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static void ConfigureOtlpExporter(
        OtlpExporterOptions export,
        OtlpOptions options)
    {
        export.Endpoint = new Uri(options.Endpoint!);
        export.Protocol = string.Equals(options.Protocol, "HttpProtobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;
    }
}
