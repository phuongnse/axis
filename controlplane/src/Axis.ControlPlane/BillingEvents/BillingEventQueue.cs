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

    public const string SubscriptionStateChanged = "subscription.stateChanged";

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

    /// <summary>
    /// Queues <c>subscription.stateChanged</c> for a move of a subscription's state, see <c>docs/reference/billing-api.md#subscription-state-changed</c>.
    /// Call it in the move's transaction, after the update, so the event holds what the move left. It reads the subscription's row and writes nothing
    /// when <c>ControlPlane:BillingEvents:Url</c> is not set. <paramref name="timestamp"/> is the time of the move.
    /// </summary>
    public static async Task EnqueueStateChangedAsync(
        NpgsqlTransaction transaction,
        ControlPlaneOptions options,
        Guid subscriptionId,
        string previousState,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(options);
        if (!options.BillingEvents.IsSet)
        {
            return;
        }

        string tenantId;
        string state;
        DateOnly? paidThrough;
        DateTimeOffset? nextAt;
        await using (var select = new NpgsqlCommand(
            "SELECT tenant_id, state, paid_through, next_at FROM control_plane.subscriptions WHERE id = @id",
            transaction.Connection,
            transaction))
        {
            select.Parameters.AddWithValue("id", subscriptionId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            tenantId = reader.GetString(0);
            state = reader.GetString(1);
            paidThrough = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2);
            nextAt = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3);
        }

        var data = new JsonObject
        {
            ["tenantId"] = tenantId,
            ["state"] = state,
            ["previousState"] = previousState,
            ["paidThrough"] = paidThrough?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["nextAt"] = nextAt is { } next ? Timestamp(next) : null,
        };
        await EnqueueAsync(transaction, options, SubscriptionStateChanged, timestamp, data, null, cancellationToken);
    }

    /// <summary>The ISO 8601 text of a time in UTC, as the events carry it.</summary>
    public static string Timestamp(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
