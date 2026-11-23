using Axis.ControlPlane.Audit;
using Axis.ControlPlane.BillingEvents;
using Npgsql;

namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// Ends a subscription at its paid-through date, see <c>docs/reference/billing-api.md#ending-a-subscription</c>. An <c>active</c> subscription becomes
/// <c>cancelled</c> with a new step id and keeps its paid-through date and its next date, so the tenant works until the day after its paid-through date.
/// Then the clock moves it to <c>readOnly</c>, as <c>docs/reference/subscriptions.md#states</c> says. The caller has locked the subscription's row.
/// </summary>
internal static class SubscriptionCancel
{
    /// <summary>
    /// Makes the subscription <c>cancelled</c> in <paramref name="transaction"/>, writes the record <c>subscription.cancelled</c> and queues the
    /// <c>subscription.stateChanged</c> event at <paramref name="timestamp"/>. Returns false when the subscription is not <c>active</c>, and then changes nothing.
    /// </summary>
    public static async Task<bool> CancelAsync(
        NpgsqlTransaction transaction,
        Guid subscriptionId,
        AuditActorKind actorKind,
        string actor,
        ControlPlaneOptions options,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await using (var update = new NpgsqlCommand(
            "UPDATE control_plane.subscriptions SET state = 'cancelled', step_id = @newStep WHERE id = @id AND state = 'active'",
            transaction.Connection,
            transaction))
        {
            update.Parameters.AddWithValue("newStep", Guid.CreateVersion7());
            update.Parameters.AddWithValue("id", subscriptionId);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return false;
            }
        }

        await AuditRecords.AppendAsync(transaction, actorKind, actor, "subscription.cancelled", subscriptionId.ToString(), cancellationToken);
        await BillingEventQueue.EnqueueStateChangedAsync(transaction, options, subscriptionId, "active", timestamp, cancellationToken);
        return true;
    }
}
