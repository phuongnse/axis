using Microsoft.Extensions.Hosting;

namespace Axis.Shared.Infrastructure.Observability;

/// <summary>
/// Host configuration for OpenTelemetry (ADR-018). Bound from the <c>OpenTelemetry</c> config section.
/// </summary>
public sealed class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    /// <summary>Master switch. When false, no OTEL providers or exporters are registered.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Suppress telemetry in the <c>Testing</c> environment unless <see cref="Enabled"/> is overridden.</summary>
    public bool DisableInTesting { get; set; } = true;

    public string ServiceName { get; set; } = "axis-api";

    public string? ServiceVersion { get; set; }

    public OtlpOptions Otlp { get; set; } = new();

    public PrometheusOptions Prometheus { get; set; } = new();

    /// <summary>Ship <see cref="Microsoft.Extensions.Logging"/> records via OTLP (Loki in production).</summary>
    public bool ExportLogs { get; set; } = true;

    public bool IsEnabled(IHostEnvironment environment)
    {
        if (!Enabled)
            return false;

        if (DisableInTesting && environment.IsEnvironment("Testing"))
            return false;

        return true;
    }
}

public sealed class OtlpOptions
{
    /// <summary>OTLP/gRPC or HTTP endpoint, e.g. <c>http://localhost:4317</c>.</summary>
    public string? Endpoint { get; set; }

    /// <summary><c>Grpc</c> (default) or <c>HttpProtobuf</c>.</summary>
    public string Protocol { get; set; } = "Grpc";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);
}

public sealed class PrometheusOptions
{
    /// <summary>Expose the Prometheus scrape endpoint (ADR-018: Prometheus scrapes <c>/metrics</c>).</summary>
    public bool Enabled { get; set; } = true;

    public string ScrapeEndpointPath { get; set; } = "/metrics";
}
