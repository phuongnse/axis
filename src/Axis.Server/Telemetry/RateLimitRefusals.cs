using System.Diagnostics.Metrics;
using Axis.Core.Telemetry;
using Axis.Server.Http;

namespace Axis.Server.Telemetry;

/// <summary>
/// Counts the requests a rate limit refused, by partition. The tag holds the partition and never a tenant, a user
/// or an address. The meter belongs to the host's meter factory.
/// </summary>
internal sealed class RateLimitRefusals
{
    private readonly Counter<long> _refused;

    public RateLimitRefusals(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(AxisMetrics.MeterName);
        _refused = meter.CreateCounter<long>(AxisMetrics.RateLimitRefusals, "{request}", "Requests refused by a rate limit.");
    }

    /// <summary>Counts one request refused in <paramref name="partition"/>.</summary>
    public void Record(Partition partition) =>
        _refused.Add(1, new KeyValuePair<string, object?>(AxisTelemetry.PartitionTag, Name(partition)));

    private static string Name(Partition partition) => partition switch
    {
        Partition.SignIn => "sign_in",
        Partition.Session => "session",
        Partition.Bearer => "bearer",
        Partition.Publishing => "publishing",
        _ => throw new ArgumentOutOfRangeException(nameof(partition)),
    };
}
