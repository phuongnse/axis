using System.Diagnostics;

namespace Axis.Core.Telemetry;

/// <summary>
/// The Axis trace source, its span names and attribute keys, and the helpers that keep spans free of
/// values. Spans carry names only, never a field value, a secret, a body or a user id. The libraries
/// use only <see cref="ActivitySource"/>, and each host sets up the OpenTelemetry SDK that exports.
/// </summary>
public static class AxisTelemetry
{
    /// <summary>The name of <see cref="Source"/>, which a host adds to its tracer provider.</summary>
    public const string SourceName = "Axis";

    /// <summary>A work item run on a worker. It starts a new trace linked to the span that queued the item.</summary>
    public const string WorkItemRun = "axis.work_item.run";

    /// <summary>One step of a process instance.</summary>
    public const string ProcessStep = "axis.process.step";

    /// <summary>One attempt of a connector call.</summary>
    public const string ConnectorAttempt = "axis.connector.attempt";

    /// <summary>A lookup that reconciles an unknown connector call.</summary>
    public const string ConnectorLookup = "axis.connector.lookup";

    /// <summary>One run of a package operation.</summary>
    public const string PackageCall = "axis.package.call";

    /// <summary>The activation of one application folder at startup.</summary>
    public const string Activation = "axis.activation";

    public const string TenantTag = "axis.tenant";
    public const string ApplicationTag = "axis.application";
    public const string ProcessTag = "axis.process";
    public const string StepTag = "axis.step";
    public const string ConnectorTag = "axis.connector";
    public const string PackageTag = "axis.package";
    public const string OperationTag = "axis.operation";
    public const string WorkItemKindTag = "axis.work_item.kind";
    public const string OutcomeTag = "axis.outcome";
    public const string PartitionTag = "axis.partition";

    /// <summary>The standard tag that names the type of the exception that failed a span. Its message is never recorded.</summary>
    public const string ErrorTypeTag = "error.type";

    /// <summary>
    /// The name of the custom property that holds the path template of the connector call an outgoing
    /// HTTP span belongs to. Custom properties are never exported.
    /// </summary>
    public const string UrlTemplateProperty = "axis.url_template";

    /// <summary>The source of the Axis spans.</summary>
    public static ActivitySource Source { get; } = new(SourceName);

    /// <summary>The trace id of the current span in hex, or <see langword="null"/> when there is no current span.</summary>
    public static string? CurrentTraceId() => Activity.Current?.TraceId.ToHexString();

    /// <summary>The span id of the current span in hex, or <see langword="null"/> when there is no current span.</summary>
    public static string? CurrentSpanId() => Activity.Current?.SpanId.ToHexString();

    /// <summary>
    /// Marks the span as failed and records the exception's type. The message is left out because it
    /// can hold a value.
    /// </summary>
    public static void RecordFailure(Activity? activity, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error);
        activity.SetTag(ErrorTypeTag, exception.GetType().FullName);
    }

    /// <summary>
    /// Removes the bound address from an outgoing HTTP client span. The span loses
    /// <c>server.address</c>, <c>server.port</c> and <c>url.query</c>. Its <c>url.full</c> becomes the
    /// path template of the connector call that sent it, or is removed when the call has none.
    /// Spans that are not HTTP client spans stay as they are.
    /// </summary>
    public static void RedactHttpClientSpan(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (activity.Kind != ActivityKind.Client || activity.GetTagItem("http.request.method") is null)
        {
            return;
        }

        activity.SetTag("server.address", null);
        activity.SetTag("server.port", null);
        activity.SetTag("url.query", null);
        activity.SetTag("url.full", activity.Parent?.GetCustomProperty(UrlTemplateProperty) as string);
    }
}
