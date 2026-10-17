using System.Diagnostics.Metrics;
using Axis.Server.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Axis.Server.Tests;

/// <summary>
/// The server's metric setup keeps no bound address on the HttpClient and DNS metrics, and keeps the
/// attributes that tell a failure from a success.
/// </summary>
public sealed class ServerTelemetryTests
{
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
