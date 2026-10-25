using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Axis.Core.Telemetry;

/// <summary>
/// The Axis meter and the metrics recorded while work runs. Attributes name things and never hold a
/// value, a secret, a bound address or a user id. The libraries use only <see cref="Meter"/>, and each
/// host sets up the OpenTelemetry SDK that exports.
/// </summary>
public static class AxisMetrics
{
    /// <summary>The name of <see cref="Meter"/>, which a host adds to its meter provider.</summary>
    public const string MeterName = "Axis";

    /// <summary>The duration of one work item run, in seconds, by tenant, kind and outcome.</summary>
    public const string WorkItemDuration = "axis.work_items.duration";

    /// <summary>The work items whose due time has passed, by tenant and kind. A claimed item counts until it is done.</summary>
    public const string WorkItemsDue = "axis.work_items.due";

    /// <summary>The age in seconds of the oldest work item whose due time has passed, by tenant and kind.</summary>
    public const string WorkItemsOldestDueAge = "axis.work_items.oldest_due_age";

    /// <summary>The pending computed field names, by tenant and application.</summary>
    public const string ComputedPendingFields = "axis.computed.pending_fields";

    /// <summary>The process instances that ended <c>completed</c> or <c>failed</c>.</summary>
    public const string InstancesEnded = "axis.process.instances.ended";

    /// <summary>The attempts of connector calls and package operations. Lookups are not attempts.</summary>
    public const string CallAttempts = "axis.calls.attempts";

    /// <summary>The dead letters that workers wrote, by tenant and work item kind.</summary>
    public const string DeadLettersCreated = "axis.dead_letters.created";

    /// <summary>Whether each tenant database answered the last readiness probe of the server, 1 or 0, by tenant.</summary>
    public const string TenantDatabaseAvailable = "axis.tenant.database.available";

    /// <summary>
    /// The attribute keys that the HTTP client and DNS metrics keep. Any other key, such as
    /// <c>server.address</c>, <c>server.port</c>, <c>network.peer.address</c> or <c>dns.question.name</c>,
    /// is dropped, so no bound address reaches an exporter.
    /// </summary>
    public static IReadOnlyList<string> HttpTagKeys { get; } =
    [
        AxisTelemetry.ErrorTypeTag,
        "http.connection.state",
        "http.request.method",
        "http.response.status_code",
        "network.protocol.version",
        "url.scheme",
    ];

    /// <summary>The meter of the Axis metrics.</summary>
    public static Meter Meter { get; } = new(MeterName);

    private static readonly Histogram<double> _workItemDurations = Meter.CreateHistogram(
        WorkItemDuration,
        "s",
        "How long a work item run took.",
        advice: new InstrumentAdvice<double>
        {
            HistogramBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60],
        });

    private static readonly Counter<long> _endedInstances = Meter.CreateCounter<long>(
        InstancesEnded,
        "{instance}",
        "Process instances that ended.");

    private static readonly Counter<long> _attemptedCalls = Meter.CreateCounter<long>(
        CallAttempts,
        "{attempt}",
        "Attempts of connector calls and package operations.");

    private static readonly Counter<long> _createdDeadLetters = Meter.CreateCounter<long>(
        DeadLettersCreated,
        "{dead_letter}",
        "Dead letters that workers wrote.");

    /// <summary>Records one run of a work item. <paramref name="outcome"/> is <c>completed</c>, <c>failed</c> or <c>fenced</c>.</summary>
    public static void RecordWorkItem(string tenant, string kind, string outcome, TimeSpan duration) =>
        _workItemDurations.Record(
            duration.TotalSeconds,
            new KeyValuePair<string, object?>(AxisTelemetry.TenantTag, tenant),
            new KeyValuePair<string, object?>(AxisTelemetry.WorkItemKindTag, kind),
            new KeyValuePair<string, object?>(AxisTelemetry.OutcomeTag, outcome));

    /// <summary>Counts a dead letter of the work item kind <paramref name="kind"/>.</summary>
    public static void RecordDeadLetter(string tenant, string kind) =>
        _createdDeadLetters.Add(
            1,
            new KeyValuePair<string, object?>(AxisTelemetry.TenantTag, tenant),
            new KeyValuePair<string, object?>(AxisTelemetry.WorkItemKindTag, kind));

    /// <summary>
    /// Counts a process instance that ended in <paramref name="state"/>, <c>completed</c> or <c>failed</c>.
    /// A <see langword="null"/> name is left out.
    /// </summary>
    public static void RecordInstanceEnded(string tenant, string? application, string? process, string state)
    {
        var tags = new TagList { { AxisTelemetry.TenantTag, tenant } };
        Add(ref tags, AxisTelemetry.ApplicationTag, application);
        Add(ref tags, AxisTelemetry.ProcessTag, process);
        tags.Add(AxisTelemetry.OutcomeTag, state);
        _endedInstances.Add(1, tags);
    }

    /// <summary>
    /// Counts one attempt of a connector call or a package operation. <paramref name="targetTag"/> is
    /// <see cref="AxisTelemetry.ConnectorTag"/> or <see cref="AxisTelemetry.PackageTag"/>, and
    /// <paramref name="target"/> is the connector or package name. <paramref name="outcome"/> is the
    /// call outcome class. A <see langword="null"/> application is left out.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="targetTag"/> is neither of the two.</exception>
    public static void RecordCallAttempt(string tenant, string? application, string targetTag, string target, string operation, string outcome)
    {
        if (targetTag is not (AxisTelemetry.ConnectorTag or AxisTelemetry.PackageTag))
        {
            throw new ArgumentException($"The target tag must be '{AxisTelemetry.ConnectorTag}' or '{AxisTelemetry.PackageTag}'.", nameof(targetTag));
        }

        var tags = new TagList { { AxisTelemetry.TenantTag, tenant } };
        Add(ref tags, AxisTelemetry.ApplicationTag, application);
        tags.Add(targetTag, target);
        tags.Add(AxisTelemetry.OperationTag, operation);
        tags.Add(AxisTelemetry.OutcomeTag, outcome);
        _attemptedCalls.Add(1, tags);
    }

    private static void Add(ref TagList tags, string key, string? value)
    {
        if (value is not null)
        {
            tags.Add(key, value);
        }
    }
}
