using System.Globalization;
using System.Text.Json.Nodes;
using Axis.ControlPlane.Jobs;
using Npgsql;

namespace Axis.ControlPlane.BillingEvents;

/// <summary>
/// Queues the events that the control plane sends the operator's billing system, see <c>docs/reference/billing-api.md#events</c>. Each event is a
/// <c>sendBillingEvent</c> job, so the job queue's retries and its failed-job metric apply.
/// </summary>
internal static class BillingEventQueue
{
    public const string JobKind = "sendBillingEvent";

    public const string TenantCreated = "tenant.created";

    public const string UsageDaily = "usage.daily";

    /// <summary>
    /// Queues an event in <paramref name="transaction"/>, so the event exists exactly when the change that caused it does. It writes nothing when
    /// <c>ControlPlane:BillingEvents:Url</c> is not set. The payload fixes the event's id, type, timestamp and <paramref name="data"/>, so every
    /// attempt sends the same id and body. Pass no personal data in <paramref name="data"/>: a payload is stored in the database, so the job
    /// reads the person's data from the sign-up <paramref name="signUpId"/> names when it runs.
    /// </summary>
    public static async Task EnqueueAsync(
        NpgsqlTransaction transaction,
        ControlPlaneOptions options,
        string type,
        DateTimeOffset timestamp,
        JsonObject data,
        Guid? signUpId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(data);
        if (!options.BillingEvents.IsSet)
        {
            return;
        }

        var payload = new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString("D"),
            ["type"] = type,

            // Text, not a date: every attempt then sends the same characters.
            ["timestamp"] = Timestamp(timestamp),
            ["data"] = data,
        };
        if (signUpId is { } id)
        {
            payload["signUpId"] = id.ToString("D");
        }

        await JobQueue.EnqueueAsync(transaction.Connection!, transaction, JobKind, payload, null, cancellationToken);
    }

    /// <summary>The ISO 8601 text of a time in UTC, as the events carry it.</summary>
    public static string Timestamp(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
