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

    /// <summary>The provider events that were kept and applied nothing because they do not fit their invoice, by <see cref="PaymentProviderTag"/> and <see cref="PaymentReasonTag"/>.</summary>
    public const string PaymentsOutOfPlace = "axis.controlplane.payments.out_of_place";

    /// <summary>The attribute that names a payment provider.</summary>
    public const string PaymentProviderTag = "axis.payment.provider";

    /// <summary>The attribute that names why an event is out of place. An invoice id and an amount are never attributes.</summary>
    public const string PaymentReasonTag = "axis.payment.reason";

    private static readonly Counter<long> _failedJobs = AxisMetrics.Meter.CreateCounter<long>(
        JobsFailed,
        "{job}",
        "Jobs that ended failed after their last attempt.");

    private static readonly Counter<long> _outOfPlacePayments = AxisMetrics.Meter.CreateCounter<long>(
        PaymentsOutOfPlace,
        "{event}",
        "Payment provider events that were kept and applied nothing because they do not fit their invoice.");

    public static void RecordJobFailed(string kind) =>
        _failedJobs.Add(1, new KeyValuePair<string, object?>(JobKindTag, kind));

    public static void RecordPaymentOutOfPlace(string provider, string reason) =>
        _outOfPlacePayments.Add(
            1,
            new KeyValuePair<string, object?>(PaymentProviderTag, provider),
            new KeyValuePair<string, object?>(PaymentReasonTag, reason));
}
