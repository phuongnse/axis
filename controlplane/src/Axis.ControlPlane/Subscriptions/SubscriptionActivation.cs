using Axis.ControlPlane.Audit;
using Npgsql;

namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// What follows a move to <c>active</c>, see <c>docs/reference/subscriptions.md#states</c>. The billing API's paid-through date calls it in the
/// transaction of the move, so the records and the jobs commit with it or not at all.
/// </summary>
internal static class SubscriptionActivation
{
    /// <summary>
    /// Appends the audit record <c>subscription.active</c>, queues <c>writeQuotas</c> and, when the subscription was <c>pastDue</c> or <c>readOnly</c>,
    /// queues <c>resumeTenant</c> with <paramref name="newStep"/>, the step id that the move gave the subscription.
    /// </summary>
    public static async Task QueueAsync(
        NpgsqlTransaction transaction,
        Guid subscriptionId,
        string fromState,
        Guid newStep,
        AuditActorKind actorKind,
        string actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var connection = transaction.Connection!;
        await AuditRecords.AppendAsync(transaction, actorKind, actor, "subscription.active", subscriptionId.ToString(), cancellationToken);
        await SubscriptionQuotas.EnqueueAsync(connection, transaction, subscriptionId, cancellationToken);
        if (fromState is "pastDue" or "readOnly")
        {
            await SubscriptionResume.EnqueueAsync(connection, transaction, subscriptionId, newStep, cancellationToken);
        }
    }
}
