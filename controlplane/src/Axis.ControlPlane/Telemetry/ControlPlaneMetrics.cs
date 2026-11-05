using System.Diagnostics.Metrics;
using Axis.Core.Telemetry;

namespace Axis.ControlPlane.Telemetry;

/// <summary>The control plane's metrics, on the Axis meter so <c>AddStandardMetrics</c> exports them. Attributes name things and never hold a value or an id.</summary>
internal static class ControlPlaneMetrics
{
    /// <summary>The jobs that ended <c>failed</c> after their last attempt, by <see cref="JobKindTag"/>.</summary>
    public const string JobsFailed = "axis.controlplane.jobs.failed";

    /// <summary>The attribute that names the kind of a job. A job id is never an attribute.</summary>
    public const string JobKindTag = "axis.job.kind";

    private static readonly Counter<long> _failedJobs = AxisMetrics.Meter.CreateCounter<long>(
        JobsFailed,
        "{job}",
        "Jobs that ended failed after their last attempt.");

    public static void RecordJobFailed(string kind) =>
        _failedJobs.Add(1, new KeyValuePair<string, object?>(JobKindTag, kind));
}
