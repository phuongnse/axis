using Axis.ControlPlane.Audit;
using Npgsql;

namespace Axis.ControlPlane.Subscriptions;

/// <summary>
/// Applies a recorded payment to its subscription, see <c>docs/reference/subscriptions.md#states</c>. When the payment settled its invoice and
/// no open invoice of the subscription is past its due date, a <c>trialing</c>, <c>pastDue</c> or <c>readOnly</c> subscription becomes <c>active</c>
/// with a new step id, in the transaction of the payment. The move writes the audit record <c>subscription.active</c>, queues <c>writeQuotas</c>
/// and, from <c>pastDue</c> or <c>readOnly</c>, <c>resumeTenant</c> with the new step id. A subscription with no period end starts its first paid
/// period at the later of the trial's end and the payment, and the end of that period is its period end and next date. A subscription that
/// has a period end keeps it, as the hourly job moves it forward.
/// </summary>
internal static class SubscriptionPayments
{
    /// <summary>
    /// Makes the subscription of <paramref name="invoiceId"/> <c>active</c> when the payment just recorded in <paramref name="transaction"/> allows it, and
    /// returns whether it did. The caller has locked the subscription's row, as <see cref="Billing.Payments.RecordAsync"/> does.
    /// </summary>
    public static async Task<bool> ApplyAsync(
        NpgsqlTransaction transaction,
        Guid invoiceId,
        AuditActorKind actorKind,
        string actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var connection = transaction.Connection!;
        Guid subscriptionId;
        string state;
        string? period;
        DateTimeOffset? periodEndsAt;
        DateTimeOffset trialEndsAt;
        await using (var select = new NpgsqlCommand(
            """
            SELECT s.id, s.state, s.period, s.period_ends_at, s.trial_ends_at
            FROM billing.invoices i JOIN control_plane.subscriptions s ON s.id = i.subscription_id
            WHERE i.id = @invoice AND i.status = 'paid'
              AND NOT EXISTS (SELECT 1 FROM billing.invoices o WHERE o.subscription_id = s.id AND o.status = 'open' AND o.due_at < @now)
            """,
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("invoice", invoiceId);
            select.Parameters.AddWithValue("now", now);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return false;
            }

            subscriptionId = reader.GetGuid(0);
            state = reader.GetString(1);
            period = reader.IsDBNull(2) ? null : reader.GetString(2);
            periodEndsAt = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3);
            trialEndsAt = reader.GetFieldValue<DateTimeOffset>(4);
        }

        if (state is not ("trialing" or "pastDue" or "readOnly"))
        {
            return false;
        }

        var end = periodEndsAt ?? SubscriptionClock.PeriodEnd(trialEndsAt > now ? trialEndsAt : now, period);
        var newStep = Guid.CreateVersion7();
        await using (var update = new NpgsqlCommand(
            "UPDATE control_plane.subscriptions SET state = 'active', step_id = @newStep, period_ends_at = @end, next_at = @end WHERE id = @id AND state = @from",
            connection,
            transaction))
        {
            update.Parameters.AddWithValue("newStep", newStep);
            update.Parameters.AddWithValue("end", end);
            update.Parameters.AddWithValue("id", subscriptionId);
            update.Parameters.AddWithValue("from", state);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                return false;
            }
        }

        await AuditRecords.AppendAsync(transaction, actorKind, actorId, "subscription.active", subscriptionId.ToString(), cancellationToken);
        await SubscriptionQuotas.EnqueueAsync(connection, transaction, subscriptionId, cancellationToken);
        if (state is "pastDue" or "readOnly")
        {
            await SubscriptionResume.EnqueueAsync(connection, transaction, subscriptionId, newStep, cancellationToken);
        }

        return true;
    }
}
