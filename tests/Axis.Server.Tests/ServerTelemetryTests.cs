using System.Diagnostics.Metrics;
using Axis.Server.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Axis.Server.Tests;

/// <summary>
/// The server's metric setup keeps no bound address on the HttpClient and DNS metrics, and keeps the
/// attributes that tell a failure from a success. Outside Development every OTLP endpoint must be <c>https</c>.
/// </summary>
public sealed class ServerTelemetryTests
{
    [Theory]
    [InlineData("OTEL_EXPORTER_OTLP_ENDPOINT")]
    [InlineData("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")]
    [InlineData("OTEL_EXPORTER_OTLP_METRICS_ENDPOINT")]
    [InlineData("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT")]
    public void An_http_otlp_endpoint_outside_Development_stops_the_server_naming_the_variable_and_not_its_value(string variable)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [variable] = "http://collector.example.test:4318" })
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddServerTelemetry(configuration, new HostingEnvironment { EnvironmentName = "Production" }));

        Assert.Contains(variable, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("collector.example.test", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Production", "https://collector.example.test:4318")]
    [InlineData("Production", "")]
    [InlineData("Development", "http://collector.example.test:4318")]
    public void An_https_or_empty_otlp_endpoint_outside_Development_and_any_endpoint_in_Development_are_accepted(string environment, string endpoint)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"] = endpoint })
            .Build();

        var services = new ServiceCollection().AddServerTelemetry(configuration, new HostingEnvironment { EnvironmentName = environment });

        Assert.NotEmpty(services);
    }

    [Fact]
    public void The_http_client_and_dns_metrics_drop_the_address_keys_and_keep_the_allowed_ones()
    {
        var exported = new List<MetricSnapshot>();
        using var http = new Meter("System.Net.Http");
        using var dns = new Meter("System.Net.NameResolution");
        var requests = http.CreateHistogram<double>("http.client.request.duration", "s");
        var lookups = dns.CreateHistogram<double>("dns.lookup.duration", "s");
        using (var provider = Sdk.CreateMeterProviderBuilder().AddStandardMetrics().AddInMemoryExporter(exported).Build())
        {
            requests.Record(
                0.1,
                new("server.address", "purchasing.example.test"),
                new("server.port", 443),
                new("network.peer.address", "10.0.0.7"),
                new("http.request.method", "GET"),
                new("error.type", "timeout"));
            lookups.Record(0.01, new("dns.question.name", "purchasing.example.test"), new("error.type", "host_not_found"));
            provider.ForceFlush();
        }

        var request = Keys(exported, "http.client.request.duration");
        var lookup = Keys(exported, "dns.lookup.duration");
        Assert.Equal(["error.type", "http.request.method"], request.Order(StringComparer.Ordinal));
        Assert.Equal(["error.type"], lookup);
    }

    private static string[] Keys(List<MetricSnapshot> exported, string name)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var point in exported.Where(metric => metric.Name == name).SelectMany(metric => metric.MetricPoints))
        {
            foreach (var tag in point.Tags)
            {
                keys.Add(tag.Key);
            }
        }

        return [.. keys];
    }
}
