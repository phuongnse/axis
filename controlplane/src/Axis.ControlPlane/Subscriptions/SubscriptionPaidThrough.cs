using Axis.ControlPlane.Audit;
using Npgsql;

namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// Sets the date a subscription is paid through, see <c>docs/reference/billing-api.md#setting-the-paid-through-date</c>. A <c>trialing</c>, <c>pastDue</c>,
/// <c>readOnly</c> or <c>cancelled</c> subscription becomes <c>active</c> with a new step id, and <see cref="SubscriptionActivation"/> writes its records and
/// queues its jobs. An <c>active</c> subscription keeps its state and step id and takes the new date. The tenant is paid through the end of the day, so the
/// next date is the start of the following day in UTC, and the end of the paid period is cleared, so the subscription's
/// period is no longer rolled forward. The caller has locked the subscription's row and has checked its state.
/// </summary>
internal static class SubscriptionPaidThrough
{
    /// <summary>
    /// Sets <paramref name="paidThrough"/> on the subscription in <paramref name="transaction"/>. Returns false when the subscription's state is not one
    /// that takes a paid-through date, and then changes nothing.
    /// </summary>
    public static async Task<bool> SetAsync(
        NpgsqlTransaction transaction,
        Guid subscriptionId,
        DateOnly paidThrough,
        AuditActorKind actorKind,
        string actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var connection = transaction.Connection!;
        string state;
        await using (var select = new NpgsqlCommand("SELECT state FROM control_plane.subscriptions WHERE id = @id", connection, transaction))
        {
            select.Parameters.AddWithValue("id", subscriptionId);
            state = (string?)await select.ExecuteScalarAsync(cancellationToken) ?? "";
        }

        var nextAt = new DateTimeOffset(paidThrough.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (state == "active")
        {
            await using var keep = new NpgsqlCommand(
                "UPDATE control_plane.subscriptions SET paid_through = @date, next_at = @next, period_ends_at = NULL WHERE id = @id AND state = 'active'",
                connection,
                transaction);
            keep.Parameters.AddWithValue("date", paidThrough);
            keep.Parameters.AddWithValue("next", nextAt);
            keep.Parameters.AddWithValue("id", subscriptionId);
            if (await keep.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return false;
            }

            await AuditRecords.AppendAsync(transaction, actorKind, actor, "subscription.paidThroughSet", subscriptionId.ToString(), cancellationToken);
            return true;
        }

        if (state is not ("trialing" or "pastDue" or "readOnly" or "cancelled"))
        {
            return false;
        }

        var newStep = Guid.CreateVersion7();
        await using (var update = new NpgsqlCommand(
            """
            UPDATE control_plane.subscriptions
            SET state = 'active', step_id = @newStep, paid_through = @date, next_at = @next, period_ends_at = NULL
            WHERE id = @id AND state = @from
            """,
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("newStep", newStep);
            update.Parameters.AddWithValue("date", paidThrough);
            update.Parameters.AddWithValue("next", nextAt);
            update.Parameters.AddWithValue("id", subscriptionId);
            update.Parameters.AddWithValue("from", state);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return false;
            }
        }

        await SubscriptionActivation.QueueAsync(transaction, subscriptionId, state, newStep, actorKind, actor, cancellationToken);
        return true;
    }
}
